using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;
using Tubumu.Core.Extensions;
using TubumuMeeting.Mediasoup;

namespace TubumuMeeting.Meeting
{
    public class Room : EventEmitter, IEquatable<Room>
    {
        private readonly object _locker = new object();

        public Guid RoomId { get; }

        public string Name { get; }

        public bool Closed { get; private set; }

        [JsonIgnore]
        public Router Router { get; set; }

        [JsonIgnore]
        public Dictionary<int, Peer> Peers { get; } = new Dictionary<int, Peer>();

        public Room(Guid roomId, string name)
        {
            RoomId = roomId;
            Name = name.IsNullOrWhiteSpace() ? "Meeting" : name;
            Closed = false;
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

                peer.JoinRoom(this);
                Emit("peerjoined", peer);
            }

            peer.On("joinedroom", m =>
            {
                lock (_locker)
                {
                    Peers[peer.PeerId] = peer;
                }
            });

            peer.On("leftroom", m =>
            {
                lock (_locker)
                {
                    Peers.Remove(peer.PeerId);
                }
            });

            return true;
        }

        public bool Close()
        {
            Closed = true;

            Emit("closed", this);

            return true;
        }

        public bool Equals(Room other)
        {
            return RoomId == other.RoomId;
        }

        public override int GetHashCode()
        {
            return RoomId.GetHashCode();
        }
    }
}
