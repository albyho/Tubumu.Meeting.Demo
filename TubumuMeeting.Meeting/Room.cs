using System;
using System.Collections.Generic;
using System.Text;
using Tubumu.Core.Extensions;
using TubumuMeeting.Mediasoup;

namespace TubumuMeeting.Meeting
{
    public class Room : EventEmitter
    {
        private readonly object _locker = new object();

        public Worker Worker { get; }

        public Guid RoomId { get; }

        public string Name { get; }

        public Dictionary<int, Peer> Peers { get; } = new Dictionary<int, Peer>();

        public Room(Worker worker, Guid roomId, string name)
        {
            Worker = worker;
            RoomId = roomId;
            Name = name.IsNullOrWhiteSpace() ? "Guest" : name;
        }

        public bool HandlePeer(int peerId, string name)
        {
            var peer = new Peer(peerId, name);

            lock (_locker)
            {
                if (Peers.ContainsKey(peer.PeerId))
                {
                    Emit("peerexists", peer.PeerId);
                    return false;
                }
                Peers.Add(peer.PeerId, peer);
                Emit("peerjoined", peer.PeerId);

                peer.On("close", _ =>
                {
                    lock (_locker)
                    {
                        Emit("peerleft", peer.PeerId);
                        Peers.Remove(peer.PeerId);
                    }
                });
            }

            return true;
        }
    }

    public class Peer : EventEmitter
    {
        public int PeerId { get; }

        public string Name { get; }

        public Peer(int peerId, string name)
        {
            PeerId = peerId;
            Name = name.IsNullOrWhiteSpace() ? "Guest" : name;
        }
    }
}
