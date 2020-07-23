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
    public class Lobby
    {
        #region Private Fields

        /// <summary>
        /// Logger factory for create logger.
        /// </summary>
        private readonly ILoggerFactory _loggerFactory;

        /// <summary>
        /// Logger
        /// </summary>
        private readonly ILogger<Lobby> _logger;

        private readonly MediasoupOptions _mediasoupOptions;

        private readonly MediasoupServer _mediasoupServer;

        private readonly AsyncLock _groupLocker = new AsyncLock();

        private readonly object _peerLocker = new object();

        private readonly object _peerGroupLocker = new object();

        private readonly object _roomLocker = new object();

        private readonly object _peerRoomLocker = new object();

        #endregion

        public RtpCapabilities DefaultRtpCapabilities { get; private set; }

        public Dictionary<Guid, Group> Groups { get; } = new Dictionary<Guid, Group>();

        public Dictionary<string, Room> Rooms { get; } = new Dictionary<string, Room>();

        public Dictionary<string, Peer> Peers { get; } = new Dictionary<string, Peer>();

        public Lobby(ILoggerFactory loggerFactory, MediasoupOptions mediasoupOptions, MediasoupServer mediasoupServer)
        {
            _loggerFactory = loggerFactory;
            _logger = _loggerFactory.CreateLogger<Lobby>();
            _mediasoupOptions = mediasoupOptions;
            _mediasoupServer = mediasoupServer;

            var rtpCodecCapabilities = mediasoupOptions.MediasoupSettings.RouterSettings.RtpCodecCapabilities;
            // This may throw.
            DefaultRtpCapabilities = ORTC.GenerateRouterRtpCapabilities(rtpCodecCapabilities);
        }

        public async Task<bool> PeerJoinAsync(string peerId,
            RtpCapabilities rtpCapabilities,
            SctpCapabilities? sctpCapabilities,
            string displayName,
            string[]? sources,
            Guid groupId,
            Dictionary<string, object>? appData)
        {
            PeerClose(peerId);

            using (await _groupLocker.LockAsync())
            {
                if (!Groups.TryGetValue(groupId, out var group))
                {
                    group = await CreateGroupAsync(groupId, "Default");
                }

                lock (_peerLocker)
                {
                    if (Peers.TryGetValue(peerId, out var peer))
                    {
                        _logger.LogError($"PeerJoinAsync() | Peer[{peerId}] has already in Group:{groupId}.");
                        return false;
                    }

                    peer = new Peer(peerId, displayName, group)
                    {
                        RtpCapabilities = rtpCapabilities,
                        SctpCapabilities = sctpCapabilities,
                        Sources = sources,
                        AppData = appData
                    };

                    Peers[peerId] = peer;

                    lock (_peerGroupLocker)
                    {
                        group.Peers[peerId] = peer;
                        return true;
                    }
                }
            }
        }

        public async Task<RoomInterestedSources?> PeerJoinRoomAsync(string peerId, Guid groupId, JoinRoomRequest joinRoomRequest)
        {
            using (await _groupLocker.LockAsync())
            {
                if (!Groups.TryGetValue(groupId, out var group))
                {
                    group = await CreateGroupAsync(groupId, "Default");
                }

                lock (_peerGroupLocker)
                {
                    if (!group.Peers.TryGetValue(peerId, out var peer))
                    {
                        _logger.LogError($"PeerJoinRoomsAsync() | Peer[{peerId}] is not exists in Group:{groupId}.");
                        return null;
                    }

                    lock (_roomLocker)
                    {
                        if (!group.Rooms.TryGetValue(joinRoomRequest.RoomId, out var room))
                        {
                            room = CreateRoom(group, joinRoomRequest.RoomId, "Default");
                        }

                        var roomInterestedSources = new RoomInterestedSources(room, joinRoomRequest.InterestedSources);

                        lock (_peerRoomLocker)
                        {
                            room.Peers[peerId] = peer;
                            peer.Rooms[joinRoomRequest.RoomId] = roomInterestedSources;
                        }

                        return roomInterestedSources;
                    }
                }
            }
        }

        public bool PeerLeaveRoom(string peerId, string roomId)
        {
            lock (_peerLocker)
            {
                if (!Peers.TryGetValue(peerId, out var peer))
                {
                    _logger.LogError($"PeerLeaveRooms() | Peer[{peerId}] is not exists.");

                    return false;
                }

                lock (_peerRoomLocker)
                {
                    var roomIdsToRemove = new List<string>();
                    if (peer.Rooms.TryGetValue(roomId, out var room))
                    {
                        room.Room.Peers.Remove(peerId);
                        roomIdsToRemove.Add(room.Room.RoomId);
                    }

                    peer.Rooms.Remove(roomId);

                    return true;
                }
            }
        }

        public void PeerClose(string peerId)
        {
            lock (_peerLocker)
            {
                if (!Peers.TryGetValue(peerId, out var peer))
                {
                    return;
                }

                peer.Close();
                Peers.Remove(peerId);

                lock (_peerGroupLocker)
                {
                    peer.Group.Peers.Remove(peerId);

                    lock (_peerRoomLocker)
                    {
                        foreach (var room in peer.Rooms.Values)
                        {
                            room.Room.Peers.Remove(peerId);
                        }

                        peer.Rooms.Clear();
                    }
                }
            }
        }

        #region Private Methods

        private async Task<Group> CreateGroupAsync(Guid groupId, string name)
        {
            // Router media codecs.
            var mediaCodecs = _mediasoupOptions.MediasoupSettings.RouterSettings.RtpCodecCapabilities;

            // Create a mediasoup Router.
            var worker = _mediasoupServer.GetWorker();
            var router = await worker.CreateRouterAsync(new RouterOptions
            {
                MediaCodecs = mediaCodecs
            });

            var group = new Group(_loggerFactory, router, groupId, name);
            Groups[groupId] = group;
            return group;
        }

        private Room CreateRoom(Group group, string roomId, string name)
        {
            var room = new Room(_loggerFactory, group, roomId, name);
            group.Rooms[roomId] = room;
            Rooms[roomId] = room;
            return room;
        }

        #endregion
    }
}
