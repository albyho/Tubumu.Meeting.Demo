using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Tubumu.Core.Extensions;
using TubumuMeeting.Mediasoup;

namespace TubumuMeeting.Meeting.Server
{
    public partial class Room : IEquatable<Room>
    {
        public string RoomId { get; }

        public string Name { get; }

        public bool Equals(Room other)
        {
            return RoomId == other.RoomId;
        }

        public override int GetHashCode()
        {
            return RoomId.GetHashCode();
        }
    }

    public partial class Room
    {
        /// <summary>
        /// Logger factory for create logger.
        /// </summary>
        private readonly ILoggerFactory _loggerFactory;

        /// <summary>
        /// Logger.
        /// </summary>
        private readonly ILogger<Room> _logger;

        private readonly object _roomLocker = new object();

        public bool Closed { get; private set; }

        public Group Group { get; private set; }

        public Dictionary<string, Peer> Peers { get; } = new Dictionary<string, Peer>();

        public Room(ILoggerFactory loggerFactory, Group group, string roomId, string name)
        {
            _loggerFactory = loggerFactory;
            _logger = _loggerFactory.CreateLogger<Room>();

            Group = group;
            RoomId = roomId;
            Name = name.IsNullOrWhiteSpace() ? "Default" : name;
            Closed = false;
        }

        public RoomInterestedSources PeerJoinRoom(Peer peer, string[] interestedSources)
        {
            lock (_roomLocker)
            {
                if (Peers.TryGetValue(peer.PeerId, out var _))
                {
                    throw new Exception($"PeerJoinRoom() | Peer:{peer.PeerId} was already in Room:{RoomId}");
                }

                if (peer.Rooms.TryGetValue(RoomId, out var _))
                {
                    throw new Exception($"PeerLeaveRoom() | peer.Rooms[{RoomId}] is exists");
                }

                Peers[peer.PeerId] = peer;
                return peer.JoinRoom(this, interestedSources);
            }
        }

        public bool PeerLeaveRoom(Peer peer)
        {
            lock (_roomLocker)
            {
                if (!Peers.TryGetValue(peer.PeerId, out var _))
                {
                    throw new Exception($"PeerLeaveRoom() | Peer:{peer.PeerId} is not in Room:{RoomId}");
                }

                if (peer.Rooms.TryGetValue(RoomId, out var _))
                {
                    throw new Exception($"PeerLeaveRoom() | peer.Rooms[{RoomId}] is exists");
                }

                Peers.Remove(peer.PeerId);

                return true;
            }
        }

        public void Close()
        {
            _logger.LogError($"Close() | Room: {RoomId}");

            if (Closed)
            {
                return;
            }

            Closed = true;
        }
    }
}
