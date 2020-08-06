using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Tubumu.Core.Extensions;
using TubumuMeeting.Mediasoup;

namespace TubumuMeeting.Meeting.Server
{
    public partial class Group : IEquatable<Group>
    {
        public Guid GroupId { get; }

        public string Name { get; }

        public bool Equals(Group other)
        {
            return GroupId == other.GroupId;
        }

        public override int GetHashCode()
        {
            return GroupId.GetHashCode();
        }
    }

    public partial class Group
    {
        /// <summary>
        /// Logger factory for create logger.
        /// </summary>
        private readonly ILoggerFactory _loggerFactory;

        private readonly object _groupLocker = new object();

        private readonly object _peerLocker = new object();

        public Router Router { get; private set; }

        public Dictionary<string, Room> Rooms { get; } = new Dictionary<string, Room>();

        public Dictionary<string, Peer> Peers { get; } = new Dictionary<string, Peer>();

        public Group(ILoggerFactory loggerFactory, Router router, Guid groupId, string name)
        {
            GroupId = groupId;
            Name = name.IsNullOrWhiteSpace() ? "Default" : name;
            Router = router;
        }

        public Room CreateRoom(string roomId, string name)
        {
            lock(_groupLocker)
            {
                var room = new Room(_loggerFactory, roomId, name);
                Rooms[roomId] = room;
                return room;
            }
        }

        public void PeerJoinGroup(Peer peer)
        {
            lock (_peerLocker)
            {
                Peers[peer.PeerId] = peer;
            }
        }

        public void PeerLeaveGroup(string peerId)
        {
            lock (_peerLocker)
            {
                Peers.Remove(peerId);
            }
        }
    }
}
