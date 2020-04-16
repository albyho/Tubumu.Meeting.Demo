using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using TubumuMeeting.Mediasoup;

namespace TubumuMeeting.Meeting
{
    public partial class MeetingManager : EventEmitter
    {
        private readonly MediasoupServer _mediasoupServer;

        private readonly object _locker = new object();

        public Dictionary<Guid, Room> Rooms = new Dictionary<Guid, Room>();

        public Dictionary<int, Peer> Peers { get; } = new Dictionary<int, Peer>();

        public List<RoomPeer> RoomPeerList = new List<RoomPeer>();

        public MeetingManager(MediasoupServer mediasoupServer)
        {
            _mediasoupServer = mediasoupServer;
        }

        public Room GetOrCreateRoom(Guid roomId, string name)
        {
            lock (_locker)
            {
                if (Rooms.TryGetValue(roomId, out var room))
                {
                    return room;
                }

                room = new Room(roomId, name);
                Rooms[roomId] = room;

                // Room 不会主动关闭，所以这里不需要监听 Room 的 closed 事件来清理数据。

                return room;
            }
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
                    Emit("peerexists", peer);
                    return false;
                }
                Peers[peerId] = peer;
            }

            peer.On("closed", m =>
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
                Emit("peerclosed", m);
            });

            return true;
        }

        public bool ClosePeer(int peerId)
        {
            lock (_locker)
            {
                if (Peers.TryGetValue(peerId, out var peer))
                {
                    return peer.Close();
                }
            }
            return false;
        }
    }

    public partial class MeetingManager
    {
        public async Task<bool> RoomRelateRouter(Guid roomId)
        {
            Room room;
            lock (_locker)
            {
                if (Rooms.TryGetValue(roomId, out room))
                {
                    return false;
                }

                if (room.Router != null)
                {
                    // 重复关联
                    return false;
                }
            }

            var worker = _mediasoupServer.GetWorker();
            room.Router = await worker.CreateRouter(null);

            return true;
        }

        public bool PeerJoinRoom(Peer peer, Room room)
        {
            return peer.JoinRoom(room);
        }
    }
}
