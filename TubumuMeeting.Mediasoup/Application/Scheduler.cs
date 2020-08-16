using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Nito.AsyncEx;

namespace TubumuMeeting.Mediasoup
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

        private readonly AsyncReaderWriterLock _peersLocker = new AsyncReaderWriterLock();

        private readonly AsyncReaderWriterLock _roomsLocker = new AsyncReaderWriterLock();

        private readonly AsyncReaderWriterLock _peerRoomLocker = new AsyncReaderWriterLock();  // 直接或间接访问 peer.Rooms 或 room.Peers 或者访问多个 Peer 的资源需上锁。

        private Router _router;

        #endregion

        #region Public Properties

        public RtpCapabilities DefaultRtpCapabilities { get; private set; }

        public Dictionary<string, Room> Rooms { get; } = new Dictionary<string, Room>();

        public Dictionary<string, Peer> Peers { get; } = new Dictionary<string, Peer>();

        #endregion

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
        }

        public async Task<bool> JoinAsync(string peerId, JoinRequest joinRequest)
        {
            using (await _peersLocker.WriterLockAsync())
            {
                if (Peers.TryGetValue(peerId, out var peer))
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

                Peers[peerId] = peer;

                return true;
            }
        }

        public async Task<LeaveResult?> LeaveAsync(string peerId)
        {
            using (await _peersLocker.WriterLockAsync())
            {
                if (!Peers.TryGetValue(peerId, out var peer))
                {
                    // _logger.LogWarning($"PeerLeave() | Peer:{peerId} is not exists.");
                    return null;
                }

                using (await _peerRoomLocker.ReaderLockAsync())
                {
                    var otherPeers = new HashSet<PeerInfo>();
                    foreach (var room in peer.Rooms.Values)
                    {
                        var otherPeersInRoom = room.Room.Peers.Values.Where(m => m.PeerId != peerId);
                        foreach (var otherPeer in otherPeersInRoom)
                        {
                            otherPeers.Add(otherPeer);
                        }

                        room.Room.Peers.Remove(peerId);
                    }

                    peer.Rooms.Clear();
                    await peer.LeaveAsync();
                    Peers.Remove(peerId);

                    return new LeaveResult
                    {
                        SelfPeer = peer,
                        OtherPeers = otherPeers.ToArray(),
                    };
                }
            }
        }

        public async Task<PeerAppDataResult> SetPeerAppDataAsync(string peerId, SetPeerAppDataRequest setPeerAppDataRequest)
        {
            using (await _peersLocker.ReaderLockAsync())
            {
                if (!Peers.TryGetValue(peerId, out var peer))
                {
                    throw new Exception($"SetPeerAppDataAsync() | Peer:{peerId} is not exists.");
                }

                // NOTE: 这里用 _peerRoomLocker 锁住, 因为涉及 Peer 和 Room 的关系查询而没有写入操作，故使用 ReaderLock。
                using (await _peerRoomLocker.ReaderLockAsync())
                {
                    foreach (var item in setPeerAppDataRequest.PeerAppData)
                    {
                        peer.AppData[item.Key] = item.Value;
                    }

                    var otherPeerIds = (from r in peer.Rooms.Values
                                        from p in r.Room.Peers.Keys
                                        select p).Distinct().ToArray();

                    return new PeerAppDataResult
                    {
                        SelfPeer = peer,
                        OtherPeerIds = otherPeerIds,
                    };
                }
            }
        }

        public async Task<PeerAppDataResult> UnsetPeerAppDataAsync(string peerId, UnsetPeerAppDataRequest unsetPeerAppDataRequest)
        {
            using (await _peersLocker.ReaderLockAsync())
            {
                if (!Peers.TryGetValue(peerId, out var peer))
                {
                    throw new Exception($"UnsetPeerAppDataAsync() | Peer:{peerId} is not exists.");
                }

                using (await _peerRoomLocker.ReaderLockAsync())
                {
                    foreach (var item in unsetPeerAppDataRequest.Keys)
                    {
                        peer.AppData.TryRemove(item, out var _);
                    }

                    var otherPeerIds = (from r in peer.Rooms.Values
                                        from p in r.Room.Peers.Keys
                                        select p).Distinct().ToArray();

                    return new PeerAppDataResult
                    {
                        SelfPeer = peer,
                        OtherPeerIds = otherPeerIds,
                    };
                }
            }
        }

        public async Task<PeerAppDataResult> ClearPeerAppDataAsync(string peerId)
        {
            using (await _peersLocker.ReaderLockAsync())
            {
                if (!Peers.TryGetValue(peerId, out var peer))
                {
                    throw new Exception($"ClearPeerAppDataAsync() | Peer:{peerId} is not exists.");
                }

                using (await _peerRoomLocker.ReaderLockAsync())
                {
                    peer.AppData.Clear();

                    var otherPeerIds = (from r in peer.Rooms.Values
                                        from p in r.Room.Peers.Keys
                                        select p).Distinct().ToArray();

                    return new PeerAppDataResult
                    {
                        SelfPeer = peer,
                        OtherPeerIds = otherPeerIds,
                    };
                }
            }
        }

        public async Task<WebRtcTransport> CreateWebRtcTransportAsync(string peerId, CreateWebRtcTransportRequest createWebRtcTransportRequest)
        {
            using (await _peersLocker.ReaderLockAsync())
            {
                if (!Peers.TryGetValue(peerId, out var peer))
                {
                    throw new Exception($"CreateWebRtcTransport() | Peer:{peerId} is not exists");
                }

                return await peer.CreateWebRtcTransportAsync(createWebRtcTransportRequest);
            }
        }

        public async Task<bool> ConnectWebRtcTransportAsync(string peerId, ConnectWebRtcTransportRequest connectWebRtcTransportRequest)
        {
            using (await _peersLocker.ReaderLockAsync())
            {
                if (!Peers.TryGetValue(peerId, out var peer))
                {
                    throw new Exception($"ConnectWebRtcTransportAsync() | Peer:{peerId} is not exists");
                }
                return await peer.ConnectWebRtcTransportAsync(connectWebRtcTransportRequest);
            }
        }

        public async Task<JoinRoomResult> JoinRoomAsync(string peerId, JoinRoomRequest joinRoomRequest)
        {
            using (await _peersLocker.ReaderLockAsync())
            {
                if (!Peers.TryGetValue(peerId, out var peer))
                {
                    throw new Exception($"JoinRoomAsync() | Peer:{peerId} is not exists.");
                }

                if (joinRoomRequest.RoomSources.Except(peer.Sources).Any())
                {
                    throw new Exception($"Pull() | Peer:{peerId} don't has some sources which is in Room:{joinRoomRequest.RoomId}.");
                }

                using (await _roomsLocker.WriterLockAsync())
                {
                    // Room 如果不存在则创建
                    if (!Rooms.TryGetValue(joinRoomRequest.RoomId, out var room))
                    {
                        room = new Room(_loggerFactory, _router, joinRoomRequest.RoomId, "Default");
                        Rooms[room.RoomId] = room;
                    }

                    using (await _peerRoomLocker.WriterLockAsync())
                    {
                        var roomResources = joinRoomRequest.RoomSources ?? Array.Empty<string>();
                        var roomAppData = joinRoomRequest.RoomAppData ?? new Dictionary<string, object>();
                        var peerInfo = new PeerInfo
                        {
                            RoomId = joinRoomRequest.RoomId,
                            PeerId = peer.PeerId,
                            DisplayName = peer.DisplayName,
                            Sources = peer.Sources,
                            AppData = peer.AppData.ToDictionary(m => m.Key, m => m.Value),
                            RoomSources = roomResources,
                            RoomAppData = roomAppData,
                        };
                        var roomWithRoomAppData = new RoomWithRoomAppData(room, roomResources, roomAppData);

                        room.Peers[peerId] = peerInfo;
                        peer.Rooms[joinRoomRequest.RoomId] = roomWithRoomAppData;

                        var peersInRoom = room.Peers.Values.ToArray();
                        return new JoinRoomResult
                        {
                            SelfPeer = peerInfo,
                            RoomSources = roomWithRoomAppData.RoomSources,
                            RoomAppData = roomWithRoomAppData.RoomAppData.ToDictionary(m => m.Key, m => m.Value),
                            PeersInRoom = peersInRoom,
                        };
                    }
                }
            }
        }

        public async Task<LeaveRoomResult> LeaveRoomAsync(string peerId, string roomId)
        {
            using (await _peersLocker.ReaderLockAsync())
            {
                if (!Peers.TryGetValue(peerId, out var peer))
                {
                    throw new Exception($"LeaveRoom() | Peer:{peerId} is not exists.");
                }

                using (await _peerRoomLocker.WriterLockAsync())
                {
                    if (!peer.Rooms.TryGetValue(roomId, out var room))
                    {
                        throw new Exception($"LeaveRoom() | Peer:{peerId} is not exists in Room:{roomId}.");
                    }

                    // 离开房间
                    await peer.LeaveRoomAsync(roomId);

                    // Peer 和 Room 的关系
                    room.Room.Peers.Remove(peerId);
                    peer.Rooms.Remove(roomId);

                    var otherPeers = room.Room.Peers.Values.ToArray();

                    return new LeaveRoomResult
                    {
                        SelfPeer = peer,
                        OtherPeers = otherPeers,
                    };
                }
            }
        }

        public async Task<PeerRoomAppDataResult> SetRoomAppDataAsync(string peerId, SetRoomAppDataRequest setRoomAppDataRequest)
        {
            using (await _peersLocker.ReaderLockAsync())
            {
                if (!Peers.TryGetValue(peerId, out var peer))
                {
                    throw new Exception($"SetRoomAppDataAsync() | Peer:{peerId} is not exists.");
                }

                using (await _peerRoomLocker.ReaderLockAsync())
                {
                    if (!peer.Rooms.TryGetValue(setRoomAppDataRequest.RoomId, out var room))
                    {
                        throw new Exception($"SetRoomAppDataAsync() | Peer:{peerId} is not exists in Room:{setRoomAppDataRequest.RoomId}.");
                    }

                    foreach (var item in setRoomAppDataRequest.RoomAppData)
                    {
                        room.RoomAppData[item.Key] = item.Value;
                    }

                    var otherPeerIds = room.Room.Peers.Keys.ToArray();

                    return new PeerRoomAppDataResult
                    {
                        SelfPeer = peer,
                        RoomAppData = room.RoomAppData.ToDictionary(m => m.Key, m => m.Value),
                        OtherPeerIds = otherPeerIds,
                    };
                }
            }
        }

        public async Task<PeerRoomAppDataResult> UnsetRoomAppDataAsync(string peerId, UnsetRoomAppDataRequest unsetRoomAppDataRequest)
        {
            using (await _peersLocker.ReaderLockAsync())
            {
                if (!Peers.TryGetValue(peerId, out var peer))
                {
                    throw new Exception($"UnsetRoomAppDataAsync() | Peer:{peerId} is not exists.");
                }

                using (await _peerRoomLocker.ReaderLockAsync())
                {
                    if (!peer.Rooms.TryGetValue(unsetRoomAppDataRequest.RoomId, out var room))
                    {
                        throw new Exception($"UnsetRoomAppDataAsync() | Peer:{peerId} is not exists in Room:{unsetRoomAppDataRequest.RoomId}.");
                    }

                    foreach (var item in unsetRoomAppDataRequest.Keys)
                    {
                        room.RoomAppData.TryRemove(item, out var _);
                    }

                    var otherPeerIds = room.Room.Peers.Keys.ToArray();

                    return new PeerRoomAppDataResult
                    {
                        SelfPeer = peer,
                        RoomAppData = room.RoomAppData.ToDictionary(m => m.Key, m => m.Value),
                        OtherPeerIds = otherPeerIds,
                    };
                }
            }
        }

        public async Task<PeerRoomAppDataResult> ClearRoomAppDataAsync(string peerId, string roomId)
        {
            using (await _peersLocker.ReaderLockAsync())
            {
                if (!Peers.TryGetValue(peerId, out var peer))
                {
                    throw new Exception($"ClearRoomAppDataAsync() | Peer:{peerId} is not exists.");
                }

                using (await _peerRoomLocker.ReaderLockAsync())
                {
                    if (!peer.Rooms.TryGetValue(roomId, out var room))
                    {
                        throw new Exception($"ClearRoomAppDataAsync() | Peer:{peerId} is not exists in Room:{roomId}.");
                    }

                    room.RoomAppData.Clear();

                    var otherPeerIds = room.Room.Peers.Keys.ToArray();

                    return new PeerRoomAppDataResult
                    {
                        SelfPeer = peer,
                        RoomAppData = room.RoomAppData.ToDictionary(m => m.Key, m => m.Value),
                        OtherPeerIds = otherPeerIds,
                    };
                }
            }
        }

        public async Task<PullResult> PullAsync(string peerId, PullRequest pullRequest)
        {
            using (await _peersLocker.ReaderLockAsync())
            {
                if (!Peers.TryGetValue(peerId, out var peer))
                {
                    throw new Exception($"Pull() | Peer:{peerId} is not exists.");
                }

                if (!Peers.TryGetValue(pullRequest.ProducerPeerId, out var producePeer))
                {
                    throw new Exception($"Pull() | Peer:{pullRequest.ProducerPeerId} is not exists.");
                }

                using (await _peerRoomLocker.ReaderLockAsync())
                {
                    if (!peer.Rooms.ContainsKey(pullRequest.RoomId))
                    {
                        throw new Exception($"Pull() | Peer:{peerId} is not exists in Room:{pullRequest.RoomId}.");
                    }

                    if (!producePeer.Rooms.TryGetValue(pullRequest.RoomId, out var room))
                    {
                        throw new Exception($"Pull() | Peer:{pullRequest.ProducerPeerId} is not exists in Room:{pullRequest.RoomId}.");
                    }

                    if (pullRequest.RoomSources.Except(room.RoomSources).Any())
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
            using (await _peersLocker.ReaderLockAsync())
            {
                if (!Peers.TryGetValue(peerId, out var peer))
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
                    if (Peers.TryGetValue(item.ConsumerPeerId, out var consumerPeer))
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
            using (await _peersLocker.ReaderLockAsync())
            {
                if (!Peers.TryGetValue(producerPeerId, out var producerPeer))
                {
                    throw new Exception($"ConsumeAsync() | Producer Peer:{producerPeerId} is not exists.");
                }
                if (!Peers.TryGetValue(cosumerPeerId, out var cosumerPeer))
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
            using (await _peersLocker.ReaderLockAsync())
            {
                if (!Peers.TryGetValue(peerId, out var peer))
                {
                    throw new Exception($"CloseProducerAsync() | Peer:{peerId} is not exists.");
                }
                return await peer.CloseProducerAsync(producerId);
            }
        }

        public async Task<bool> PauseProducerAsync(string peerId, string producerId)
        {
            using (await _peersLocker.ReaderLockAsync())
            {
                if (!Peers.TryGetValue(peerId, out var peer))
                {
                    throw new Exception($"PauseProducerAsync() | Peer:{peerId} is not exists.");
                }
                return await peer.PauseProducerAsync(producerId);
            }
        }

        public async Task<bool> ResumeProducerAsync(string peerId, string producerId)
        {
            using (await _peersLocker.ReaderLockAsync())
            {
                if (!Peers.TryGetValue(peerId, out var peer))
                {
                    throw new Exception($"ResumeProducerAsync() | Peer:{peerId} is not exists.");
                }
                return await peer.ResumeProducerAsync(producerId);
            }
        }

        public async Task<bool> CloseConsumerAsync(string peerId, string consumerId)
        {
            using (await _peersLocker.ReaderLockAsync())
            {
                if (!Peers.TryGetValue(peerId, out var peer))
                {
                    throw new Exception($"CloseConsumerAsync() | Peer:{ peerId} is not exists.");
                }
                return await peer.CloseConsumerAsync(consumerId);
            }
        }

        public async Task<bool> PauseConsumerAsync(string peerId, string consumerId)
        {
            using (await _peersLocker.ReaderLockAsync())
            {
                if (!Peers.TryGetValue(peerId, out var peer))
                {
                    throw new Exception($"PauseConsumerAsync() | Peer:{peerId} is not exists.");
                }
                return await peer.PauseConsumerAsync(consumerId);
            }
        }

        public async Task<Consumer> ResumeConsumerAsync(string peerId, string consumerId)
        {
            using (await _peersLocker.ReaderLockAsync())
            {
                if (!Peers.TryGetValue(peerId, out var peer))
                {
                    throw new Exception($"ResumeConsumerAsync() | Peer:{peerId} is not exists.");
                }
                return await peer.ResumeConsumerAsync(consumerId);
            }
        }

        public async Task<bool> SetConsumerPreferedLayersAsync(string peerId, SetConsumerPreferedLayersRequest setConsumerPreferedLayersRequest)
        {
            using (await _peersLocker.ReaderLockAsync())
            {
                if (!Peers.TryGetValue(peerId, out var peer))
                {
                    throw new Exception($"SetConsumerPreferedLayersAsync() | Peer:{peerId} is not exists.");
                }
                return await peer.SetConsumerPreferedLayersAsync(setConsumerPreferedLayersRequest);
            }
        }

        public async Task<bool> SetConsumerPriorityAsync(string peerId, SetConsumerPriorityRequest setConsumerPriorityRequest)
        {
            using (await _peersLocker.ReaderLockAsync())
            {
                if (!Peers.TryGetValue(peerId, out var peer))
                {
                    throw new Exception($"SetConsumerPriorityAsync() | Peer:{peerId} is not exists.");
                }
                return await peer.SetConsumerPriorityAsync(setConsumerPriorityRequest);
            }
        }

        public async Task<bool> RequestConsumerKeyFrameAsync(string peerId, string consumerId)
        {
            using (await _peersLocker.ReaderLockAsync())
            {
                if (!Peers.TryGetValue(peerId, out var peer))
                {
                    throw new Exception($"RequestConsumerKeyFrameAsync() | Peer:{peerId} is not exists.");
                }
                return await peer.RequestConsumerKeyFrameAsync(consumerId);
            }
        }

        public async Task<TransportStat> GetTransportStatsAsync(string peerId, string transportId)
        {
            using (await _peersLocker.ReaderLockAsync())
            {
                if (!Peers.TryGetValue(peerId, out var peer))
                {
                    throw new Exception($"GetTransportStatsAsync() | Peer:{peerId} is not exists.");
                }
                return await peer.GetTransportStatsAsync(transportId);
            }
        }

        public async Task<ProducerStat> GetProducerStatsAsync(string peerId, string producerId)
        {
            using (await _peersLocker.ReaderLockAsync())
            {
                if (!Peers.TryGetValue(peerId, out var peer))
                {
                    throw new Exception($"GetProducerStatsAsync() | Peer:{peerId} is not exists.");
                }
                return await peer.GetProducerStatsAsync(producerId);
            }
        }

        public async Task<ConsumerStat> GetConsumerStatsAsync(string peerId, string consumerId)
        {
            using (await _peersLocker.ReaderLockAsync())
            {
                if (!Peers.TryGetValue(peerId, out var peer))
                {
                    throw new Exception($"GetConsumerStatsAsync() | Peer:{peerId} is not exists.");
                }
                return await peer.GetConsumerStatsAsync(consumerId);
            }
        }

        public async Task<IceParameters?> RestartIceAsync(string peerId, string transportId)
        {
            using (await _peersLocker.ReaderLockAsync())
            {
                if (!Peers.TryGetValue(peerId, out var peer))
                {
                    throw new Exception($"RestartIceAsync() | Peer:{peerId} is not exists.");
                }
                return await peer.RestartIceAsync(transportId);
            }
        }
    }
}
