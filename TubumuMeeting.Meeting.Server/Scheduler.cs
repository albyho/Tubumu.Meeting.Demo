using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.Threading;
using TubumuMeeting.Mediasoup;

namespace TubumuMeeting.Meeting.Server
{
    public class Scheduler
    {
        #region Private Fields

        /// <summary>
        /// Logger factory for create logger.
        /// </summary>
        private readonly ILoggerFactory _loggerFactory;

        /// <summary>
        /// Logger
        /// </summary>
        private readonly ILogger<Scheduler> _logger;

        private readonly MediasoupOptions _mediasoupOptions;

        private readonly MediasoupServer _mediasoupServer;

        private Dictionary<string, Peer> _peers { get; } = new Dictionary<string, Peer>();

        private readonly AsyncReaderWriterLock _peersLocker = new AsyncReaderWriterLock();

        private Dictionary<string, Room> _rooms { get; } = new Dictionary<string, Room>();

        private readonly AsyncAutoResetEvent _roomsLocker = new AsyncAutoResetEvent();

        private Dictionary<string, List<RoomWithRoomAppData>> _peerRooms { get; } = new Dictionary<string, List<RoomWithRoomAppData>>();

        private readonly AsyncReaderWriterLock _peerRoomsLocker = new AsyncReaderWriterLock();

        private Dictionary<string, List<PeerWithRoomAppData>> _roomPeers { get; } = new Dictionary<string, List<PeerWithRoomAppData>>();

        private readonly AsyncReaderWriterLock _roomPeersLocker = new AsyncReaderWriterLock();

        private readonly AsyncAutoResetEvent _peerAppDataLocker = new AsyncAutoResetEvent();

        private readonly AsyncAutoResetEvent _roomAppDataLocker = new AsyncAutoResetEvent();

        private Router _router;

        #endregion

        public RtpCapabilities DefaultRtpCapabilities { get; private set; }

        public Scheduler(ILoggerFactory loggerFactory, MediasoupOptions mediasoupOptions, MediasoupServer mediasoupServer)
        {
            _loggerFactory = loggerFactory;
            _logger = _loggerFactory.CreateLogger<Scheduler>();
            _mediasoupOptions = mediasoupOptions;
            _mediasoupServer = mediasoupServer;

            // 按创建 Route 时一样方式创建 RtpCodecCapabilities
            var rtpCodecCapabilities = mediasoupOptions.MediasoupSettings.RouterSettings.RtpCodecCapabilities;
            // This may throw.
            DefaultRtpCapabilities = ORTC.GenerateRouterRtpCapabilities(rtpCodecCapabilities);

            _roomsLocker.Set();
            _peerAppDataLocker.Set();
            _roomAppDataLocker.Set();
        }

        public async Task<bool> JoinAsync(string peerId, JoinRequest joinRequest)
        {
            using (await _peersLocker.WriteLockAsync())
            {
                if (_peers.TryGetValue(peerId, out var peer))
                {
                    _logger.LogError($"PeerJoinAsync() | Peer:{peerId} was joined.");
                    return false;
                }

                if (_router == null)
                {
                    // Router media codecs.
                    var mediaCodecs = _mediasoupOptions.MediasoupSettings.RouterSettings.RtpCodecCapabilities;

                    // Create a mediasoup Router.
                    var worker = _mediasoupServer.GetWorker();
                    _router = await worker.CreateRouterAsync(new RouterOptions
                    {
                        MediaCodecs = mediaCodecs
                    });
                }

                peer = new Peer(_loggerFactory,
                    _mediasoupOptions.MediasoupSettings.WebRtcTransportSettings,
                    _router,
                    joinRequest.RtpCapabilities,
                    joinRequest.SctpCapabilities,
                    peerId,
                    joinRequest.DisplayName,
                    joinRequest.Sources,
                    joinRequest.AppData
                    );

                _peers[peerId] = peer;

                return true;
            }
        }

        public async Task<LeaveResult?> LeaveAsync(string peerId)
        {
            using (await _peersLocker.WriteLockAsync())
            {
                if (!_peers.TryGetValue(peerId, out var peer))
                {
                    // _logger.LogWarning($"PeerLeave() | Peer:{peerId} is not exists.");
                    return null;
                }

                using (await _peerRoomsLocker.WriteLockAsync())
                {
                    var otherPeerIds = new HashSet<string>();
                    if (_peerRooms.TryGetValue(peerId, out var peerRooms))
                    {
                        _peerRooms.Remove(peerId);
                        await peer.LeaveAsync();
                        _peers.Remove(peerId);

                        using (await _roomPeersLocker.WriteLockAsync())
                        {
                            foreach (var room in peerRooms)
                            {
                                if (_roomPeers.TryGetValue(room.Room.RoomId, out var roomPeers))
                                {
                                    var otherPeersInRoom = roomPeers.Where(m => m.Peer.PeerId != peerId);
                                    foreach (var otherPeer in otherPeersInRoom)
                                    {
                                        otherPeerIds.Add(otherPeer.Peer.PeerId);
                                    }

                                    var selfPeer = roomPeers.First(m => m.Peer.PeerId == peerId);
                                    roomPeers.Remove(selfPeer);
                                }
                            }
                        }
                    }

                    return new LeaveResult
                    {
                        SelfPeer = peer,
                        OtherPeerIds = otherPeerIds.ToArray(),
                    };
                }
            }
        }

        public async Task<PeerAppDataResult> SetPeerAppDataAsync(string peerId, SetPeerAppDataRequest setPeerAppDataRequest)
        {
            using (await _peersLocker.ReadLockAsync())
            {
                if (!_peers.TryGetValue(peerId, out var peer))
                {
                    throw new Exception($"SetPeerAppDataAsync() | Peer:{peerId} is not exists.");
                }

                await _peerAppDataLocker.WaitAsync();
                foreach (var item in setPeerAppDataRequest.PeerAppData)
                {
                    peer.AppData[item.Key] = item.Value;
                }
                _peerAppDataLocker.Set();

                using (await _peerRoomsLocker.ReadLockAsync())
                {
                    var peerIds = new HashSet<string>();
                    if (_peerRooms.TryGetValue(peerId, out var peerRooms))
                    {
                        using (await _roomPeersLocker.ReadLockAsync())
                        {
                            foreach (var room in peerRooms)
                            {
                                if (_roomPeers.TryGetValue(room.Room.RoomId, out var roomPeers))
                                {
                                    foreach (var otherPeer in roomPeers)
                                    {
                                        peerIds.Add(otherPeer.Peer.PeerId);
                                    }
                                }
                            }
                        }
                    }

                    return new PeerAppDataResult
                    {
                        SelfPeer = peer,
                        PeerIds = peerIds.ToArray(),
                    };
                }
            }
        }

        public async Task<PeerAppDataResult> UnsetPeerAppDataAsync(string peerId, UnsetPeerAppDataRequest unsetPeerAppDataRequest)
        {
            using (await _peersLocker.ReadLockAsync())
            {
                if (!_peers.TryGetValue(peerId, out var peer))
                {
                    throw new Exception($"UnsetPeerAppDataAsync() | Peer:{peerId} is not exists.");
                }

                await _peerAppDataLocker.WaitAsync();
                foreach (var item in unsetPeerAppDataRequest.Keys)
                {
                    peer.AppData.Remove(item);
                }
                _peerAppDataLocker.Set();

                using (await _peerRoomsLocker.ReadLockAsync())
                {
                    var peerIds = new HashSet<string>();
                    if (_peerRooms.TryGetValue(peerId, out var peerRooms))
                    {
                        using (await _roomPeersLocker.ReadLockAsync())
                        {
                            foreach (var room in peerRooms)
                            {
                                if (_roomPeers.TryGetValue(room.Room.RoomId, out var roomPeers))
                                {
                                    foreach (var otherPeer in roomPeers)
                                    {
                                        peerIds.Add(otherPeer.Peer.PeerId);
                                    }
                                }
                            }
                        }
                    }

                    return new PeerAppDataResult
                    {
                        SelfPeer = peer,
                        PeerIds = peerIds.ToArray(),
                    };
                }
            }
        }

        public async Task<PeerAppDataResult> ClearPeerAppDataAsync(string peerId)
        {
            using (await _peersLocker.ReadLockAsync())
            {
                if (!_peers.TryGetValue(peerId, out var peer))
                {
                    throw new Exception($"ClearPeerAppDataAsync() | Peer:{peerId} is not exists.");
                }

                await _peerAppDataLocker.WaitAsync();
                peer.AppData.Clear();
                _peerAppDataLocker.Set();

                using (await _peerRoomsLocker.ReadLockAsync())
                {
                    var peerIds = new HashSet<string>();
                    if (_peerRooms.TryGetValue(peerId, out var peerRooms))
                    {
                        using (await _roomPeersLocker.ReadLockAsync())
                        {
                            foreach (var room in peerRooms)
                            {
                                if (_roomPeers.TryGetValue(room.Room.RoomId, out var roomPeers))
                                {
                                    foreach (var otherPeer in roomPeers)
                                    {
                                        peerIds.Add(otherPeer.Peer.PeerId);
                                    }
                                }
                            }
                        }
                    }

                    return new PeerAppDataResult
                    {
                        SelfPeer = peer,
                        PeerIds = peerIds.ToArray(),
                    };
                }
            }
        }

        public async Task<WebRtcTransport> CreateWebRtcTransportAsync(string peerId, CreateWebRtcTransportRequest createWebRtcTransportRequest)
        {
            using (await _peersLocker.ReadLockAsync())
            {
                if (!_peers.TryGetValue(peerId, out var peer))
                {
                    throw new Exception($"CreateWebRtcTransport() | Peer:{peerId} is not exists");
                }

                return await peer.CreateWebRtcTransportAsync(createWebRtcTransportRequest);
            }
        }

        public async Task<bool> ConnectWebRtcTransportAsync(string peerId, ConnectWebRtcTransportRequest connectWebRtcTransportRequest)
        {
            using (await _peersLocker.ReadLockAsync())
            {
                if (!_peers.TryGetValue(peerId, out var peer))
                {
                    throw new Exception($"ConnectWebRtcTransportAsync() | Peer:{peerId} is not exists");
                }
                return await peer.ConnectWebRtcTransportAsync(connectWebRtcTransportRequest);
            }
        }

        public async Task<JoinRoomResult> JoinRoomAsync(string peerId, JoinRoomRequest joinRoomRequest)
        {
            using (await _peersLocker.ReadLockAsync())
            {
                if (!_peers.TryGetValue(peerId, out var peer))
                {
                    throw new Exception($"JoinRoomAsync() | Peer:{peerId} is not exists.");
                }

                if (joinRoomRequest.RoomSources.Except(peer.Sources).Any())
                {
                    throw new Exception($"JoinRoomAsync() | Peer:{peerId} don't has some sources which is in Room:{joinRoomRequest.RoomId}.");
                }

                await _roomsLocker.WaitAsync();
                // Room 如果不存在则创建
                if (!_rooms.TryGetValue(joinRoomRequest.RoomId, out var room))
                {
                    room = new Room(_loggerFactory, _router, joinRoomRequest.RoomId, "Default");
                    _rooms[room.RoomId] = room;
                }

                PeerWithRoomAppData roomPeer;
                var otherPeers = new List<PeerWithRoomAppData>();
                using (await _peerRoomsLocker.WriteLockAsync())
                {
                    if (!_peerRooms.TryGetValue(peerId, out var peerRooms))
                    {
                        peerRooms = new List<RoomWithRoomAppData>();
                        _peerRooms[peerId] = peerRooms;
                    }

                    var roomSources = joinRoomRequest.RoomSources ?? Array.Empty<string>();
                    var roomAppData = joinRoomRequest.RoomAppData ?? new Dictionary<string, object>();

                    var peerRoom = peerRooms.FirstOrDefault(m => m.Room.RoomId == room.RoomId);
                    if (peerRoom == null)
                    {
                        peerRoom = new RoomWithRoomAppData(room);
                        peerRooms.Add(peerRoom);
                    }
                    peerRoom.PeerId = peerId;
                    peerRoom.RoomSources = roomSources;
                    peerRoom.RoomAppData = roomAppData;

                    using (await _roomPeersLocker.WriteLockAsync())
                    {
                        if (!_roomPeers.TryGetValue(room.RoomId, out var roomPeers))
                        {
                            roomPeers = new List<PeerWithRoomAppData>();
                            _roomPeers[room.RoomId] = roomPeers;
                        }

                        roomPeer = roomPeers.FirstOrDefault(m => m.Peer.PeerId == peerId);
                        if (roomPeer == null)
                        {
                            roomPeer = new PeerWithRoomAppData(peer);
                            roomPeers.Add(roomPeer);
                        }
                        roomPeer.RoomId = joinRoomRequest.RoomId;
                        roomPeer.RoomSources = roomSources;
                        roomPeer.RoomAppData = roomAppData;

                        foreach (var roomLocal in peerRooms)
                        {
                            if (_roomPeers.TryGetValue(roomLocal.Room.RoomId, out var roomPeersLocal))
                            {
                                foreach (var otherPeer in roomPeersLocal)
                                {
                                    otherPeers.Add(otherPeer);
                                }
                            }
                        }
                    }
                }

                _roomsLocker.Set();

                return new JoinRoomResult
                {
                    SelfPeer = roomPeer,
                    Peers = otherPeers.ToArray(),
                };
            }
        }

        public async Task<LeaveRoomResult> LeaveRoomAsync(string peerId, string roomId)
        {
            using (await _peersLocker.ReadLockAsync())
            {
                if (!_peers.TryGetValue(peerId, out var peer))
                {
                    throw new Exception($"LeaveRoom() | Peer:{peerId} is not exists.");
                }

                using (await _peerRoomsLocker.WriteLockAsync())
                {
                    // Peer 和 Room 的关系
                    if (!_peerRooms.TryGetValue(peerId, out var peerRooms))
                    {
                        throw new Exception($"LeaveRoom() | Peer:{peerId} is not exists in Room:{roomId}.");
                    }

                    peerRooms.RemoveAll(m => m.Room.RoomId == roomId);

                    using (await _roomPeersLocker.WriteLockAsync())
                    {
                        // Room 和 Peer 的关系
                        if (!_roomPeers.TryGetValue(roomId, out var roomPeers))
                        {
                            throw new Exception($"LeaveRoom() | Peer:{peerId} is not exists in Room:{roomId}.");
                        }

                        var otherPeerIds = new List<string>();
                        foreach (var room in peerRooms)
                        {
                            foreach (var otherPeer in roomPeers)
                            {
                                otherPeerIds.Add(otherPeer.Peer.PeerId);
                            }

                            var selfPeer = roomPeers.First(m => m.Peer.PeerId == peerId);
                            roomPeers.Remove(selfPeer);
                        }

                        roomPeers.RemoveAll(m => m.Peer.PeerId == peerId);

                        // 离开房间
                        await peer.LeaveRoomAsync(roomId);

                        return new LeaveRoomResult
                        {
                            SelfPeer = peer,
                            OtherPeerIds = otherPeerIds.ToArray(),
                        };
                    }
                }
            }
        }

        public async Task<PeerRoomAppDataResult> SetRoomAppDataAsync(string peerId, SetRoomAppDataRequest setRoomAppDataRequest)
        {
            using (await _peersLocker.ReadLockAsync())
            {
                if (!_peers.TryGetValue(peerId, out var peer))
                {
                    throw new Exception($"SetRoomAppDataAsync() | Peer:{peerId} is not exists.");
                }

                using (await _peerRoomsLocker.ReadLockAsync())
                {
                    if (!_peerRooms.TryGetValue(peerId, out var peerRooms))
                    {
                        throw new Exception($"SetRoomAppDataAsync() | Peer:{peerId} is not in any room.");
                    }

                    var peerRoom = peerRooms.FirstOrDefault(m => m.Room.RoomId == setRoomAppDataRequest.RoomId);
                    if (peerRoom == null)
                    {
                        throw new Exception($"SetRoomAppDataAsync() | Peer:{peerId} is not exists in Room:{setRoomAppDataRequest.RoomId}.");
                    }

                    using (await _roomPeersLocker.ReadLockAsync())
                    {
                        if (!_roomPeers.TryGetValue(setRoomAppDataRequest.RoomId, out var roomPeers))
                        {
                            throw new Exception($"SetRoomAppDataAsync() | Peer:{peerId} is not in any room.");
                        }

                        var roomPeer = roomPeers.FirstOrDefault(m => m.Peer.PeerId == peerId);
                        if (roomPeer == null)
                        {
                            throw new Exception($"SetRoomAppDataAsync() | Peer:{peerId} is not exists in Room:{setRoomAppDataRequest.RoomId}.");
                        }

                        await _roomAppDataLocker.WaitAsync();
                        foreach (var item in setRoomAppDataRequest.RoomAppData)
                        {
                            peerRoom.RoomAppData[item.Key] = item.Value;
                            roomPeer.RoomAppData[item.Key] = item.Value;
                        }
                        _roomAppDataLocker.Set();

                        // 只通知本房间
                        return new PeerRoomAppDataResult
                        {
                            SelfPeer = peer,
                            RoomAppData = peerRoom.RoomAppData,
                            PeerIds = roomPeers.Select(m => m.Peer.PeerId).ToArray(),
                        };
                    }
                }
            }
        }

        public async Task<PeerRoomAppDataResult> UnsetRoomAppDataAsync(string peerId, UnsetRoomAppDataRequest unsetRoomAppDataRequest)
        {
            using (await _peersLocker.ReadLockAsync())
            {
                if (!_peers.TryGetValue(peerId, out var peer))
                {
                    throw new Exception($"UnsetRoomAppDataAsync() | Peer:{peerId} is not exists.");
                }

                using (await _peerRoomsLocker.ReadLockAsync())
                {
                    if (!_peerRooms.TryGetValue(peerId, out var peerRooms))
                    {
                        throw new Exception($"SetRoomAppDataAsync() | Peer:{peerId} is not in any room.");
                    }

                    var peerRoom = peerRooms.FirstOrDefault(m => m.Room.RoomId == unsetRoomAppDataRequest.RoomId);
                    if (peerRoom == null)
                    {
                        throw new Exception($"SetRoomAppDataAsync() | Peer:{peerId} is not exists in Room:{unsetRoomAppDataRequest.RoomId}.");
                    }

                    using (await _roomPeersLocker.ReadLockAsync())
                    {
                        if (!_roomPeers.TryGetValue(unsetRoomAppDataRequest.RoomId, out var roomPeers))
                        {
                            throw new Exception($"SetRoomAppDataAsync() | Peer:{peerId} is not in any room.");
                        }

                        var roomPeer = roomPeers.FirstOrDefault(m => m.Peer.PeerId == peerId);
                        if (roomPeer == null)
                        {
                            throw new Exception($"SetRoomAppDataAsync() | Peer:{peerId} is not exists in Room:{unsetRoomAppDataRequest.RoomId}.");
                        }

                        await _roomAppDataLocker.WaitAsync();
                        foreach (var item in unsetRoomAppDataRequest.Keys)
                        {
                            peerRoom.RoomAppData.Remove(item);
                            roomPeer.RoomAppData.Remove(item);
                        }
                        _roomAppDataLocker.Set();

                        // 只通知本房间
                        return new PeerRoomAppDataResult
                        {
                            SelfPeer = peer,
                            RoomAppData = peerRoom.RoomAppData,
                            PeerIds = roomPeers.Select(m => m.Peer.PeerId).ToArray(),
                        };
                    }
                }
            }
        }

        public async Task<PeerRoomAppDataResult> ClearRoomAppDataAsync(string peerId, string roomId)
        {
            using (await _peersLocker.ReadLockAsync())
            {
                if (!_peers.TryGetValue(peerId, out var peer))
                {
                    throw new Exception($"ClearRoomAppDataAsync() | Peer:{peerId} is not exists.");
                }

                using (await _peerRoomsLocker.ReadLockAsync())
                {
                    if (!_peerRooms.TryGetValue(peerId, out var peerRooms))
                    {
                        throw new Exception($"SetRoomAppDataAsync() | Peer:{peerId} is not in any room.");
                    }

                    var peerRoom = peerRooms.FirstOrDefault(m => m.Room.RoomId == roomId);
                    if (peerRoom == null)
                    {
                        throw new Exception($"SetRoomAppDataAsync() | Peer:{peerId} is not exists in Room:{roomId}.");
                    }

                    using (await _roomPeersLocker.ReadLockAsync())
                    {
                        if (!_roomPeers.TryGetValue(roomId, out var roomPeers))
                        {
                            throw new Exception($"SetRoomAppDataAsync() | Peer:{peerId} is not in any room.");
                        }

                        var roomPeer = roomPeers.FirstOrDefault(m => m.Peer.PeerId == peerId);
                        if (roomPeer == null)
                        {
                            throw new Exception($"SetRoomAppDataAsync() | Peer:{peerId} is not exists in Room:{roomId}.");
                        }

                        await _roomAppDataLocker.WaitAsync();
                        peerRoom.RoomAppData.Clear();
                        roomPeer.RoomAppData.Clear();
                        _roomAppDataLocker.Set();

                        // 只通知本房间
                        return new PeerRoomAppDataResult
                        {
                            SelfPeer = peer,
                            RoomAppData = peerRoom.RoomAppData,
                            PeerIds = roomPeers.Select(m => m.Peer.PeerId).ToArray(),
                        };
                    }
                }
            }
        }

        public async Task<PullResult> PullAsync(string peerId, PullRequest pullRequest)
        {
            using (await _peersLocker.ReadLockAsync())
            {
                if (!_peers.TryGetValue(peerId, out var peer))
                {
                    throw new Exception($"Pull() | Peer:{peerId} is not exists.");
                }

                if (!_peers.TryGetValue(pullRequest.ProducerPeerId, out var producePeer))
                {
                    throw new Exception($"Pull() | Peer:{pullRequest.ProducerPeerId} is not exists.");
                }

                using (await _peerRoomsLocker.ReadLockAsync())
                {
                    if (!_peerRooms.TryGetValue(peerId, out var consumerPeerRooms))
                    {
                        throw new Exception($"Pull() | Peer:{peerId} is not in any rooms.");
                    }

                    if (!_peerRooms.TryGetValue(pullRequest.ProducerPeerId, out var producerPeerRooms))
                    {
                        throw new Exception($"Pull() | Peer:{pullRequest.ProducerPeerId} is not in any rooms.");
                    }

                    var consumerPeerRoom = consumerPeerRooms.FirstOrDefault(m => m.Room.RoomId == pullRequest.RoomId);
                    if (consumerPeerRoom == null)
                    {
                        throw new Exception($"Pull() | Peer:{peerId} is not in Room:{pullRequest.RoomId}.");
                    }

                    var producerPeerRoom = producerPeerRooms.FirstOrDefault(m => m.Room.RoomId == pullRequest.RoomId);
                    if (consumerPeerRoom == null)
                    {
                        throw new Exception($"Pull() | Peer:{pullRequest.ProducerPeerId} is not in Room:{pullRequest.RoomId}.");
                    }

                    if (pullRequest.RoomSources.Except(producerPeerRoom.RoomSources).Any())
                    {
                        throw new Exception($"Pull() | Peer:{pullRequest.ProducerPeerId} can't produce some sources in Room:{pullRequest.RoomId}.");
                    }

                    var pullResult = await peer.PullAsync(producePeer, pullRequest.RoomId, pullRequest.RoomSources);

                    return new PullResult
                    {
                        RoomId = pullRequest.RoomId,
                        ConsumePeer = peer,
                        ProducePeer = producePeer,
                        ExistsProducers = pullResult.ExistsProducers,
                        ProduceSources = pullResult.ProduceSources,
                    };
                }
            }
        }

        public async Task<ProduceResult> ProduceAsync(string peerId, ProduceRequest produceRequest)
        {
            using (await _peersLocker.ReadLockAsync())
            {
                if (!_peers.TryGetValue(peerId, out var peer))
                {
                    throw new Exception($"ProduceAsync() | Peer:{peerId} is not exists.");
                }

                var peerProduceResult = await peer.ProduceAsync(produceRequest);
                if (peerProduceResult == null)
                {
                    throw new Exception($"ProduceAsync() | Peer:{peerId} produce faild.");
                }

                // NOTE: 这里假设了 Room 存在
                var pullPaddingConsumerPeerWithRoomIds = new List<ConsumerPeerWithRoomId>();
                foreach (var item in peerProduceResult.PullPaddings)
                {
                    // 其他 Peer 消费本 Peer
                    if (_peers.TryGetValue(item.ConsumerPeerId, out var consumerPeer))
                    {
                        pullPaddingConsumerPeerWithRoomIds.Add(new ConsumerPeerWithRoomId
                        {
                            ConsumerPeer = consumerPeer,
                            RoomId = item.RoomId,
                        });
                    }
                }

                var produceResult = new ProduceResult
                {
                    ProducerPeer = peer,
                    Producer = peerProduceResult.Producer,
                    PullPaddingConsumerPeerWithRoomIds = pullPaddingConsumerPeerWithRoomIds.ToArray(),
                };

                return produceResult;
            }
        }

        public async Task<Consumer> ConsumeAsync(string producerPeerId, string cosumerPeerId, string producerId, string roomId)
        {
            using (await _peersLocker.ReadLockAsync())
            {
                if (!_peers.TryGetValue(producerPeerId, out var producerPeer))
                {
                    throw new Exception($"ConsumeAsync() | Producer Peer:{producerPeerId} is not exists.");
                }
                if (!_peers.TryGetValue(cosumerPeerId, out var cosumerPeer))
                {
                    throw new Exception($"ConsumeAsync() | Consumer Peer:{cosumerPeerId} is not exists.");
                }

                // NOTE: 这里假设了 Room 存在
                var consumer = await cosumerPeer.ConsumeAsync(producerPeer, producerId, roomId);
                if (consumer == null)
                {
                    throw new Exception($"ConsumeAsync() | Peer:{cosumerPeerId} consume faild.");
                }

                return consumer;
            }
        }

        public async Task<bool> CloseProducerAsync(string peerId, string producerId)
        {
            using (await _peersLocker.ReadLockAsync())
            {
                if (!_peers.TryGetValue(peerId, out var peer))
                {
                    throw new Exception($"CloseProducerAsync() | Peer:{peerId} is not exists.");
                }
                return await peer.CloseProducerAsync(producerId);
            }
        }

        public async Task<bool> PauseProducerAsync(string peerId, string producerId)
        {
            using (await _peersLocker.ReadLockAsync())
            {
                if (!_peers.TryGetValue(peerId, out var peer))
                {
                    throw new Exception($"PauseProducerAsync() | Peer:{peerId} is not exists.");
                }
                return await peer.PauseProducerAsync(producerId);
            }
        }

        public async Task<bool> ResumeProducerAsync(string peerId, string producerId)
        {
            using (await _peersLocker.ReadLockAsync())
            {
                if (!_peers.TryGetValue(peerId, out var peer))
                {
                    throw new Exception($"ResumeProducerAsync() | Peer:{peerId} is not exists.");
                }
                return await peer.ResumeProducerAsync(producerId);
            }
        }

        public async Task<bool> CloseConsumerAsync(string peerId, string consumerId)
        {
            using (await _peersLocker.ReadLockAsync())
            {
                if (!_peers.TryGetValue(peerId, out var peer))
                {
                    throw new Exception($"CloseConsumerAsync() | Peer:{ peerId} is not exists.");
                }
                return await peer.CloseConsumerAsync(consumerId);
            }
        }

        public async Task<bool> PauseConsumerAsync(string peerId, string consumerId)
        {
            using (await _peersLocker.ReadLockAsync())
            {
                if (!_peers.TryGetValue(peerId, out var peer))
                {
                    throw new Exception($"PauseConsumerAsync() | Peer:{peerId} is not exists.");
                }
                return await peer.PauseConsumerAsync(consumerId);
            }
        }

        public async Task<Consumer> ResumeConsumerAsync(string peerId, string consumerId)
        {
            using (await _peersLocker.ReadLockAsync())
            {
                if (!_peers.TryGetValue(peerId, out var peer))
                {
                    throw new Exception($"ResumeConsumerAsync() | Peer:{peerId} is not exists.");
                }
                return await peer.ResumeConsumerAsync(consumerId);
            }
        }

        public async Task<bool> SetConsumerPreferedLayersAsync(string peerId, SetConsumerPreferedLayersRequest setConsumerPreferedLayersRequest)
        {
            using (await _peersLocker.ReadLockAsync())
            {
                if (!_peers.TryGetValue(peerId, out var peer))
                {
                    throw new Exception($"SetConsumerPreferedLayersAsync() | Peer:{peerId} is not exists.");
                }
                return await peer.SetConsumerPreferedLayersAsync(setConsumerPreferedLayersRequest);
            }
        }

        public async Task<bool> SetConsumerPriorityAsync(string peerId, SetConsumerPriorityRequest setConsumerPriorityRequest)
        {
            using (await _peersLocker.ReadLockAsync())
            {
                if (!_peers.TryGetValue(peerId, out var peer))
                {
                    throw new Exception($"SetConsumerPriorityAsync() | Peer:{peerId} is not exists.");
                }
                return await peer.SetConsumerPriorityAsync(setConsumerPriorityRequest);
            }
        }

        public async Task<bool> RequestConsumerKeyFrameAsync(string peerId, string consumerId)
        {
            using (await _peersLocker.ReadLockAsync())
            {
                if (!_peers.TryGetValue(peerId, out var peer))
                {
                    throw new Exception($"RequestConsumerKeyFrameAsync() | Peer:{peerId} is not exists.");
                }
                return await peer.RequestConsumerKeyFrameAsync(consumerId);
            }
        }

        public async Task<TransportStat> GetTransportStatsAsync(string peerId, string transportId)
        {
            using (await _peersLocker.ReadLockAsync())
            {
                if (!_peers.TryGetValue(peerId, out var peer))
                {
                    throw new Exception($"GetTransportStatsAsync() | Peer:{peerId} is not exists.");
                }
                return await peer.GetTransportStatsAsync(transportId);
            }
        }

        public async Task<ProducerStat> GetProducerStatsAsync(string peerId, string producerId)
        {
            using (await _peersLocker.ReadLockAsync())
            {
                if (!_peers.TryGetValue(peerId, out var peer))
                {
                    throw new Exception($"GetProducerStatsAsync() | Peer:{peerId} is not exists.");
                }
                return await peer.GetProducerStatsAsync(producerId);
            }
        }

        public async Task<ConsumerStat> GetConsumerStatsAsync(string peerId, string consumerId)
        {
            using (await _peersLocker.ReadLockAsync())
            {
                if (!_peers.TryGetValue(peerId, out var peer))
                {
                    throw new Exception($"GetConsumerStatsAsync() | Peer:{peerId} is not exists.");
                }
                return await peer.GetConsumerStatsAsync(consumerId);
            }
        }

        public async Task<IceParameters?> RestartIceAsync(string peerId, string transportId)
        {
            using (await _peersLocker.ReadLockAsync())
            {
                if (!_peers.TryGetValue(peerId, out var peer))
                {
                    throw new Exception($"RestartIceAsync() | Peer:{peerId} is not exists.");
                }
                return await peer.RestartIceAsync(transportId);
            }
        }
    }
}
