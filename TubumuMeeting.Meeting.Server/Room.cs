using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.Threading;
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
            if (other == null)
                return false;

            return RoomId == other.RoomId;
        }

        public override bool Equals(object obj)
        {
            if (obj == null)
                return false;

            if (!(obj is Room tObj))
                return false;
            else
                return RoomId == tObj.RoomId;
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

        /// <summary>
        /// Whether the Room is closed.
        /// </summary>
        private bool _closed;

        private readonly AsyncReaderWriterLock _closeLock = new AsyncReaderWriterLock();

        private readonly Dictionary<string, PeerWithRoomAppData> _peers = new Dictionary<string, PeerWithRoomAppData>();

        private readonly AsyncReaderWriterLock _peersLock = new AsyncReaderWriterLock();

        public Router Router { get; private set; }

        public Room(ILoggerFactory loggerFactory, Router router, string roomId, string name)
        {
            _loggerFactory = loggerFactory;
            _logger = _loggerFactory.CreateLogger<Room>();
            Router = router;
            RoomId = roomId;
            Name = name.NullOrWhiteSpaceReplace("Default");
            _closed = false;
        }

        public async Task<JoinRoomResult> PeerJoinAsync(Peer peer, IEnumerable<string>? roomSources, Dictionary<string, object>? roomAppData)
        {
            using (await _closeLock.ReadLockAsync())
            {
                if (_closed)
                {
                    throw new Exception($"PeerJoinAsync() | Room:{RoomId} was closed.");
                }

                using (await _peersLock.WriteLockAsync())
                {
                    if (_peers.ContainsKey(peer.PeerId))
                    {
                        throw new Exception($"PeerJoinAsync() | Peer:{peer.PeerId} was in Room:{RoomId} already.");
                    }

                    var selfPeer = new PeerWithRoomAppData(peer)
                    {
                        RoomSources = roomSources != null ? roomSources.ToArray() : Array.Empty<string>(),
                        RoomAppData = roomAppData ?? new Dictionary<string, object>()
                    };

                    _peers[peer.PeerId] = selfPeer;

                    return new JoinRoomResult
                    {
                        SelfPeer = selfPeer,
                        Peers = _peers.Values.ToArray(),
                    };
                }
            }
        }

        public async Task<LeaveRoomResult> PeerLeaveAsync(string peerId)
        {
            using (await _closeLock.ReadLockAsync())
            {
                if (_closed)
                {
                    throw new Exception($"PeerJoinAsync() | Room:{RoomId} was closed.");
                }

                using (await _peersLock.WriteLockAsync())
                {
                    if (!_peers.TryGetValue(peerId, out var peer))
                    {
                        throw new Exception($"PeerJoinAsync() | Peer:{peerId} is not in Room:{RoomId}.");
                    }

                    _peers.Remove(peerId);

                    return new LeaveRoomResult
                    {
                        SelfPeer = peer.Peer,
                        OtherPeerIds = _peers.Keys.ToArray()
                    };
                }
            }
        }

        public async Task<string[]> GetPeerIdsAsync()
        {
            using (await _closeLock.ReadLockAsync())
            {
                if (_closed)
                {
                    throw new Exception($"PeerJoinAsync() | Room:{RoomId} was closed.");
                }

                using (await _peersLock.ReadLockAsync())
                {
                    return _peers.Keys.ToArray();
                }
            }
        }

        public async Task CloseAsync()
        {
            if (_closed)
            {
                return;
            }

            using (await _closeLock.WriteLockAsync())
            {
                if (_closed)
                {
                    return;
                }

                _logger.LogDebug($"Close() | Room:{RoomId}");

                _closed = true;

                await Router.CloseAsync();
            }
        }
    }
}
