using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Nito.AsyncEx;
using TubumuMeeting.Mediasoup;

namespace TubumuMeeting.Meeting.Server
{
    public partial class MeetingManager
    {
        private readonly ILoggerFactory _loggerFactory;

        private readonly ILogger<MeetingManager> _logger;

        private readonly MediasoupOptions _mediasoupOptions;

        private readonly MediasoupServer _mediasoupServer;

        private readonly object _locker = new object();

        private readonly object _peerRoomLocker = new object();

        public Dictionary<Guid, Room> Rooms = new Dictionary<Guid, Room>();

        public Dictionary<int, Peer> Peers { get; } = new Dictionary<int, Peer>();

        public List<PeerRoom> PeerRoomList = new List<PeerRoom>();

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

            lock (_locker)
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
            lock (_locker)
            {
                if (Rooms.TryGetValue(roomId, out var room))
                {
                    room.Close();
                    Rooms.Remove(roomId);

                    var roomPeerToRemove = PeerRoomList.Where(m => m.Room == room).ToArray();
                    lock (_peerRoomLocker)
                    {
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
            var peer = new Peer(peerId, name);
            lock (_locker)
            {
                if (Peers.ContainsKey(peer.PeerId))
                {
                    _logger.LogError($"Peer[{peerId}] is exists.");
                    return false;
                }
                Peers[peerId] = peer;
            }

            return true;
        }

        public bool JoinPeer(int peerId, RtpCapabilities rtpCapabilities)
        {
            lock (_locker)
            {
                if (!Peers.TryGetValue(peerId, out var peer))
                {
                    _logger.LogError($"Peer[{peerId}] is not exists.");
                    return false;
                }
                if (peer.Joined)
                {
                    _logger.LogError($"Peer[{peerId}] is joined.");
                    return false;
                }
                peer.RtpCapabilities = rtpCapabilities;
                peer.Joined = true;
                return true;
            }
        }

        public void ClosePeer(int peerId)
        {
            lock (_locker)
            {
                if (!Peers.TryGetValue(peerId, out var peer))
                {
                    return;
                }

                peer.Close();
                peer.Joined = false;
                peer.RtpCapabilities = null;

                Peers.Remove(peerId);
                var peerRoomToRemove = PeerRoomList.Where(m => m.Peer == peer).ToArray();
                lock (_peerRoomLocker)
                {
                    foreach (var item in peerRoomToRemove)
                    {
                        PeerRoomList.Remove(item);
                    }
                }
            }
        }

        public async Task<bool> PeerEnterRoomAsync(int peerId, Guid roomId)
        {
            await EnsureRouterAsync(roomId);

            lock (_locker)
            {
                if (!Peers.TryGetValue(peerId, out Peer peer))
                {
                    _logger.LogError($"Peer[{peerId}] is not exists.");
                    return false;
                }
                if (!Rooms.TryGetValue(roomId, out Room room))
                {
                    _logger.LogError($"Room[{roomId}] is not exists.");
                    return false;
                }

                lock (_peerRoomLocker)
                {
                    var peerRoom = new PeerRoom(peer, room);
                    // TODO: (alby)目前暂时只允许进入一个房间
                    if (PeerRoomList.Any(m => m == peerRoom))
                    {
                        return false;
                    }
                    PeerRoomList.Add(peerRoom);
                    return true;
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
            lock (_locker)
            {
                if (!Rooms.TryGetValue(roomId, out room))
                {
                    _logger.LogError($"Room[{roomId}] is not exists.");
                    return false;
                }

                if (room.Router != null)
                {
                    return true;
                }
            }

            var worker = _mediasoupServer.GetWorker();
            room.Router = await worker.CreateRouter(new RouterOptions
            {
                MediaCodecs = _mediasoupOptions.MediasoupSettings.RouteSettings.RtpCodecCapabilities
            });

            return true;
        }
    }
}
