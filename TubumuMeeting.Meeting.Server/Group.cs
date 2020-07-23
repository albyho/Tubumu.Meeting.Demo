using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Nito.AsyncEx;
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

        /// <summary>
        /// Logger.
        /// </summary>
        private readonly ILogger<Group> _logger;

        private readonly AsyncLock _peerLocker = new AsyncLock();

        private readonly object _roomLocker = new object();

        public bool Closed { get; private set; }

        public Router Router { get; private set; }

        public WebRtcTransportSettings WebRtcTransportSettings { get; private set; }

        public Dictionary<string, Room> Rooms { get; } = new Dictionary<string, Room>();

        public Dictionary<string, Peer> Peers { get; } = new Dictionary<string, Peer>();

        public Group(ILoggerFactory loggerFactory, Router router, WebRtcTransportSettings webRtcTransportSettings, Guid groupId, string name)
        {
            _loggerFactory = loggerFactory;
            _logger = _loggerFactory.CreateLogger<Group>();

            GroupId = groupId;
            Name = name.IsNullOrWhiteSpace() ? "Default" : name;
            Closed = false;
            Router = router;
            WebRtcTransportSettings = webRtcTransportSettings;
        }

        public bool PeerJoinGroup(Peer peer)
        {
            using (_peerLocker.Lock())
            {
                if(Peers.TryGetValue(peer.PeerId, out var _))
                {
                    throw new Exception($"PeerJoinGroup() | Peer:{peer.PeerId} was already in Group:{GroupId}");
                }

                Peers[peer.PeerId] = peer;
                peer.JoinGroup(this);

                return true;
            }
        }

        public bool PeerLeaveGroup(Peer peer)
        {
            using (_peerLocker.Lock())
            {
                if (Peers.TryGetValue(peer.PeerId, out var _))
                {
                   _logger.LogWarning($"PeerLeaveGroup() | Peer:{peer.PeerId} is not in Group:{GroupId}");
                    return false;
                }

                if (peer.Group != null)
                {
                    throw new Exception($"PeerLeaveRoom() | peer.Group is not null");
                }

                Peers.Remove(peer.PeerId);

                return true;
            }
        }

        public RoomInterestedSources PeerJoinRoom(Peer peer, JoinRoomRequest joinRoomRequest)
        {
            using (_peerLocker.Lock())
            {
                if (!Peers.TryGetValue(peer.PeerId, out var _))
                {
                    throw new Exception($"PeerJoinRoom() | Peer:{peer.PeerId} was already in Group:{GroupId}");
                }

                lock (_roomLocker)
                {
                    if (!Rooms.TryGetValue(joinRoomRequest.RoomId, out var room))
                    {
                        room = CreateRoom(joinRoomRequest.RoomId, "Default");
                    }

                    var result = room.PeerJoinRoom(peer, joinRoomRequest.InterestedSources);
                    return result;
                }
            }
        }

        public void Close()
        {
            _logger.LogError($"Close() | Group: {GroupId}");

            if (Closed)
            {
                return;
            }

            // 所有 Peer 离开所有 Room，然后再离开本 Group 。
            using (_peerLocker.Lock())
            {
                Peers.Values.ForEach(m =>
                {
                    m.LeaveRooms();
                    m.LeaveGroup();
                });
            }

            Router.Close();

            Closed = true;
        }

        #region Private Methods

        private Room CreateRoom(string roomId, string name)
        {
            var room = new Room(_loggerFactory, this, roomId, name);
            Rooms[roomId] = room;
            return room;
        }

        #endregion
    }
}
