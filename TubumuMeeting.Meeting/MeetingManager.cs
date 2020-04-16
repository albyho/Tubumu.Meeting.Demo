using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using TubumuMeeting.Mediasoup;

namespace TubumuMeeting.Meeting
{
    public partial class MeetingManager : EventEmitter
    {
        private readonly ILoggerFactory _loggerFactory;

        private readonly ILogger<MeetingManager> _logger;

        private readonly MediasoupServer _mediasoupServer;

        private readonly object _locker = new object();

        public Dictionary<Guid, Room> Rooms = new Dictionary<Guid, Room>();

        public Dictionary<int, Peer> Peers { get; } = new Dictionary<int, Peer>();

        public List<RoomPeer> RoomPeerList = new List<RoomPeer>();

        public MeetingManager(ILoggerFactory loggerFactory, MediasoupServer mediasoupServer)
        {
            _loggerFactory = loggerFactory;
            _logger = _loggerFactory.CreateLogger<MeetingManager>();
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

            // Room 不会主动关闭，所以这里不需要通过监听 Room 的 closed 事件来清理数据。也避免了死锁。
            room.On("PeerJoined", _ => { });
            room.On("PeerLeft", _ => { });
            room.On("Closed", _ => { });

            return room;
        }

        public Room? RemoveRoom(Guid roomId)
        {
            lock (_locker)
            {
                if (Rooms.TryGetValue(roomId, out var room))
                {
                    room.Close();
                    Rooms.Remove(roomId);

                    var roomPeerToRemove = RoomPeerList.Where(m => m.Room == room).ToArray();
                    foreach (var item in roomPeerToRemove)
                    {
                        item.Peer.LeaveRoom();
                        RoomPeerList.Remove(item);
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

            peer.On("Closed", m =>
            {
                lock (_locker)
                {
                    Peers.Remove(peerId);
                    var roomPeerToRemove = RoomPeerList.Where(m => m.Peer == peer).ToArray();
                    foreach (var item in roomPeerToRemove)
                    {
                        RoomPeerList.Remove(item);
                    }
                }
                Emit("PeerClosed", m);
            });

            return true;
        }

        public void ClosePeer(int peerId)
        {
            lock (_locker)
            {
                if (Peers.TryGetValue(peerId, out var peer))
                {
                    peer.Close();
                    // Peer 可能会主动关闭，所以这里不需要清理数据，而是通过 Peer 的 Closed 事件处理函数来清理。
                }
            }
        }
    }

    public partial class MeetingManager
    {
        public async Task<bool> RoomRelateRouter(Guid roomId)
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
                    _logger.LogError($"Room[{roomId}] is related.");
                    return false;
                }
            }

            var worker = _mediasoupServer.GetWorker();
            room.Router = await worker.CreateRouter(null);

            return true;
        }
    }

    public partial class MeetingManager
    {
        public bool PeerJoinRoom(int peerId, Guid roomId)
        {
            Room room;
            Peer peer;
            lock (_locker)
            {
                if (!Peers.TryGetValue(peerId, out peer))
                {
                    _logger.LogError($"Peer[{peerId}] is not exists.");
                    return false;
                }
                if (!Rooms.TryGetValue(roomId, out room))
                {
                    _logger.LogError($"Room[{roomId}] is not exists.");
                    return false;
                }
            }
            return peer.JoinRoom(room);
        }
    }
}
