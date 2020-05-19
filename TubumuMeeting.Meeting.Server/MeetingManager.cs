using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TubumuMeeting.Mediasoup;

namespace TubumuMeeting.Meeting.Server
{
    public partial class MeetingManager
    {
        #region Private Fields

        private readonly ILoggerFactory _loggerFactory;

        private readonly ILogger<MeetingManager> _logger;

        private readonly MediasoupOptions _mediasoupOptions;

        private readonly MediasoupServer _mediasoupServer;

        private readonly object _roomLocker = new object();

        private readonly object _peerLocker = new object();

        private readonly object _peerRoomLocker = new object();

        #endregion

        public Dictionary<Guid, Room> Rooms { get; } = new Dictionary<Guid, Room>();

        public Dictionary<int, Peer> Peers { get; } = new Dictionary<int, Peer>();

        public HashSet<PeerRoom> PeerRoomList { get; } = new HashSet<PeerRoom>();

        public MeetingManager(ILoggerFactory loggerFactory, MediasoupOptions mediasoupOptions, MediasoupServer mediasoupServer)
        {
            _loggerFactory = loggerFactory;
            _logger = _loggerFactory.CreateLogger<MeetingManager>();
            _mediasoupOptions = mediasoupOptions;
            _mediasoupServer = mediasoupServer;
        }

        public Room GetOrCreateRoom(Guid roomId, string name)
        {
            Room room;

            lock (_roomLocker)
            {
                if (Rooms.TryGetValue(roomId, out room))
                {
                    return room;
                }

                room = new Room(_loggerFactory, roomId, name);
                Rooms[roomId] = room;
            }

            return room;
        }

        public Room? CloseRoom(Guid roomId)
        {
            lock (_roomLocker)
            {
                if (Rooms.TryGetValue(roomId, out var room))
                {
                    room.Close();
                    Rooms.Remove(roomId);

                    lock (_peerRoomLocker)
                    {
                        var roomPeerToRemove = PeerRoomList.Where(m => m.Room == room).ToArray();
                        foreach (var item in roomPeerToRemove)
                        {
                            PeerRoomList.Remove(item);
                        }
                    }
                    return room;
                }

                return null;
            }
        }

        public bool HandlePeer(int peerId, string name)
        {
            ClosePeer(peerId);

            var peer = new Peer(peerId, name);
            lock (_peerLocker)
            {
                Peers[peerId] = peer;
            }

            return true;
        }

        public bool JoinPeer(int peerId, RtpCapabilities rtpCapabilities)
        {
            lock (_peerLocker)
            {
                if (!Peers.TryGetValue(peerId, out var peer))
                {
                    _logger.LogError($"JoinPeer() | Peer[{peerId}] is not exists.");
                    return false;
                }

                if (peer.Joined)
                {
                    _logger.LogError($"JoinPeer() | Peer[{peerId}] is joined.");
                    return false;
                }

                peer.RtpCapabilities = rtpCapabilities;
                peer.Joined = true;
                return true;
            }
        }

        public void ClosePeer(int peerId)
        {
            lock (_peerLocker)
            {
                if (!Peers.TryGetValue(peerId, out var peer))
                {
                    return;
                }

                peer.Close();
                Peers.Remove(peerId);

                lock (_peerRoomLocker)
                {
                    var peerRoomToRemove = PeerRoomList.Where(m => m.Peer == peer).ToArray();
                    foreach (var item in peerRoomToRemove)
                    {
                        PeerRoomList.Remove(item);
                    }
                }
            }
        }

        public async Task<bool> PeerEnterRoomAsync(int peerId, Guid roomId)
        {
            // TODO: (alby)代码清理, Room 会预先创建好。
            GetOrCreateRoom(roomId, "Meeting");

            await EnsureRouterAsync(roomId);

            lock (_peerLocker)
            {
                if (!Peers.TryGetValue(peerId, out Peer peer))
                {
                    _logger.LogError($"PeerEnterRoomAsync() | Peer[{peerId}] is not exists.");
                    return false;
                }

                lock (_roomLocker)
                {
                    if (!Rooms.TryGetValue(roomId, out Room room))
                    {
                        _logger.LogError($"PeerEnterRoomAsync() | Room[{roomId}] is not exists.");
                        return false;
                    }

                    lock (_peerRoomLocker)
                    {
                        var peerRoom = new PeerRoom(peer, room);
                        // TODO: (alby)目前暂时只允许进入一个房间
                        if (PeerRoomList.Contains(peerRoom))
                        {
                            return false;
                        }
                        PeerRoomList.Add(peerRoom);
                        return true;
                    }
                }
            }
        }

        public PeerRoom? GetPeerRoomWithPeerId(int peerId)
        {
            lock (_peerRoomLocker)
            {
                return PeerRoomList.FirstOrDefault(m => m.Peer.PeerId == peerId);
            }
        }

        public PeerRoom? GetPeerRoomWithRoomId(Guid roomId)
        {
            lock (_peerRoomLocker)
            {
                return PeerRoomList.FirstOrDefault(m => m.Room.RoomId == roomId);
            }
        }

        public Room? GetRoomWithPeerId(int peerId)
        {
            // TODO: (alby)目前暂时只允许进入一个房间
            lock (_peerRoomLocker)
            {
                return PeerRoomList.FirstOrDefault(m => m.Peer.PeerId == peerId)?.Room;
            }
        }

        public IEnumerable<Peer> GetPeersWithRoomId(Guid roomId)
        {
            lock (_peerRoomLocker)
            {
                return PeerRoomList.Where(m => m.Room.RoomId == roomId).Select(m => m.Peer);
            }
        }
    }

    public partial class MeetingManager
    {
        private async Task<bool> EnsureRouterAsync(Guid roomId)
        {
            Room room;
            lock (_roomLocker)
            {
                if (!Rooms.TryGetValue(roomId, out room))
                {
                    _logger.LogError($"EnsureRouterAsync() | Room[{roomId}] is not exists.");
                    return false;
                }

                if (room.Router != null)
                {
                    return true;
                }
            }

            // TODO: (alby)线程安全处理

            // Router media codecs.
            var mediaCodecs = _mediasoupOptions.MediasoupSettings.RouteSettings.RtpCodecCapabilities;

            // Create a mediasoup Router.
            var worker = _mediasoupServer.GetWorker();
            var router = await worker.CreateRouter(new RouterOptions
            {
                MediaCodecs = mediaCodecs
            });

            // Create a mediasoup AudioLevelObserver.
            var audioLevelObserver = await router.CreateAudioLevelObserverAsync(new AudioLevelObserverOptions
            {
                MaxEntries = 1,
                Threshold = -80,
                Interval = 800,
            });

            room.Active(router, audioLevelObserver);

            return true;
        }
    }
}
