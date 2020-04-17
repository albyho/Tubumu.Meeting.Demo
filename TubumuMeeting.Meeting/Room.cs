using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Tubumu.Core.Extensions;
using TubumuMeeting.Mediasoup;

namespace TubumuMeeting.Meeting
{
    public class Room : EventEmitter, IEquatable<Room>
    {
        private readonly object _locker = new object();

        private readonly ILoggerFactory _loggerFactory;

        private readonly ILogger<Room> _logger;

        public Guid RoomId { get; }

        public string Name { get; }

        public bool Closed { get; private set; }

        [JsonIgnore]
        public Router Router { get; set; }

        [JsonIgnore]
        public Dictionary<int, Peer> Peers { get; } = new Dictionary<int, Peer>();

        public Room(ILoggerFactory loggerFactory, Guid roomId, string name)
        {
            _loggerFactory = loggerFactory;
            _logger = _loggerFactory.CreateLogger<Room>();

            RoomId = roomId;
            Name = name.IsNullOrWhiteSpace() ? "Meeting" : name;
            Closed = false;
        }

        public bool AddPeer(Peer peer)
        {
            lock (_locker)
            {
                if (Closed)
                {
                    return false;
                }
                if (Peers.ContainsKey(peer.PeerId))
                {
                    _logger.LogError($"Peer[{peer.PeerId}] is exists.");
                    return false;
                }
            }

            Peers[peer.PeerId] = peer;
            Emit("PeerJoined", peer);

            return true;
        }

        public bool RemovePeer(int peerId)
        {
            Peer peer;
            lock (_locker)
            {
                if (Closed)
                {
                    return false;
                }
                if (!Peers.TryGetValue(peerId, out peer))
                {
                    _logger.LogError($"Peer[{peerId}] is not exists.");
                    return false;
                }
            }

            Peers.Remove(peerId);
            Emit("PeerLeft", peer);

            return true;
        }

        public void Close()
        {
            if (Closed)
            {
                return;
            }

            Closed = true;
            Peers.Clear();
            Emit("Closed", this);
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
