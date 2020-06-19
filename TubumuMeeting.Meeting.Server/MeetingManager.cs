using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Nito.AsyncEx;
using Tubumu.Core.Extensions;
using TubumuMeeting.Mediasoup;

namespace TubumuMeeting.Meeting.Server
{
    public partial class MeetingManager
    {
        #region Private Fields

        /// <summary>
        /// Logger factory for create logger.
        /// </summary>
        private readonly ILoggerFactory _loggerFactory;

        /// <summary>
        /// Logger
        /// </summary>
        private readonly ILogger<MeetingManager> _logger;

        private readonly MediasoupOptions _mediasoupOptions;

        private readonly MediasoupServer _mediasoupServer;

        private readonly object _peerLocker = new object();

        private readonly AsyncLock _roomLocker = new AsyncLock();

        private readonly object _peerRoomLocker = new object();

        #endregion

        public Dictionary<Guid, Room> Rooms { get; } = new Dictionary<Guid, Room>();

        public Dictionary<string, Peer> Peers { get; } = new Dictionary<string, Peer>();

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

            using (_roomLocker.Lock())
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
            using (_roomLocker.Lock())
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

        public bool HandlePeer(string peerId, string name)
        {
            ClosePeer(peerId);

            var peer = new Peer(peerId, name);
            lock (_peerLocker)
            {
                if (Peers.TryGetValue(peerId, out var _))
                {
                    _logger.LogError($"JoinPeer() | Peer[{peerId}] is exists.");
                    return false;
                }

                Peers[peerId] = peer;
            }

            return true;
        }

        public bool JoinPeer(string peerId, RtpCapabilities rtpCapabilities, SctpCapabilities? sctpCapabilities)
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
                peer.SctpCapabilities = sctpCapabilities;
                peer.Joined = true;
                return true;
            }
        }

        public void ClosePeer(string peerId)
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

        public async Task<bool> PeerEnterRoomAsync(string peerId, Guid roomId)
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

                using (_roomLocker.Lock())
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

        public PeerRoom? GetPeerRoomWithPeerId(string peerId)
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

        public Room? GetRoomWithPeerId(string peerId)
        {
            // TODO: (alby)目前暂时只允许进入一个房间
            lock (_peerRoomLocker)
            {
                return PeerRoomList.FirstOrDefault(m => m.Peer.PeerId == peerId)?.Room;
            }
        }

        public IEnumerable<Peer> GetPeersWithRoomId(Guid roomId, string? excludePeerId = null)
        {
            lock (_peerRoomLocker)
            {
                var peers = PeerRoomList.Where(m => m.Room.RoomId == roomId).Select(m => m.Peer);
                if (!excludePeerId.IsNullOrWhiteSpace())
                {
                    peers = peers.Where(m => m.PeerId != excludePeerId);
                }
                return peers;
            }
        }
    }

    public partial class MeetingManager
    {
        private async Task<bool> EnsureRouterAsync(Guid roomId)
        {
            using (await _roomLocker.LockAsync())
            {
                if (!Rooms.TryGetValue(roomId, out var room))
                {
                    _logger.LogError($"EnsureRouterAsync() | Room[{roomId}] is not exists.");
                    return false;
                }

                if (room.Router != null)
                {
                    return true;
                }

                // Router media codecs.
                var mediaCodecs = _mediasoupOptions.MediasoupSettings.RouterSettings.RtpCodecCapabilities;

                // Create a mediasoup Router.
                var worker = _mediasoupServer.GetWorker();
                var router = await worker.CreateRouterAsync(new RouterOptions
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
}
