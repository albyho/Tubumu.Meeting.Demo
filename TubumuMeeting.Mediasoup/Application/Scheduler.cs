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

        public RtpCapabilities DefaultRtpCapabilities { get; private set; }

        public Dictionary<string, Room> Rooms { get; } = new Dictionary<string, Room>();

        public Dictionary<string, Peer> Peers { get; } = new Dictionary<string, Peer>();

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

        public async Task<bool> Join(string peerId, JoinRequest joinRequest)
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

        public LeaveResult? Leave(string peerId)
        {
            using (_peersLocker.WriterLock())
            {
                if (!Peers.TryGetValue(peerId, out var peer))
                {
                    // _logger.LogWarning($"PeerLeave() | Peer:{peerId} is not exists.");
                    return null;
                }

                using (_peerRoomLocker.ReaderLock())
                {
                    var otherPeers = new HashSet<Peer>();
                    foreach (var room in peer.Rooms.Values)
                    {
                        var otherPeersInRoom = room.Peers.Values.Where(m => m.PeerId != peerId);
                        foreach (var otherPeer in otherPeersInRoom)
                        {
                            otherPeers.Add(otherPeer);
                        }

                        room.Peers.Remove(peerId);
                    }

                    peer.Rooms.Clear();
                    peer.Leave();
                    Peers.Remove(peerId);

                    return new LeaveResult
                    {
                        SelfPeer = peer,
                        OtherPeers = otherPeers.ToArray(),
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
                return await  peer.ConnectWebRtcTransportAsync(connectWebRtcTransportRequest);
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
                        room.Peers[peerId] = peer;
                        peer.Rooms[joinRoomRequest.RoomId] = room;

                        var peersInRoom = room.Peers.Values.ToArray();
                        return new JoinRoomResult
                        {
                            SelfPeer = peer,
                            PeersInRoom = peersInRoom,
                        };
                    }
                }
            }
        }

        public LeaveRoomResult LeaveRoom(string peerId, string roomId)
        {
            using (_peersLocker.ReaderLock())
            {
                if (!Peers.TryGetValue(peerId, out var peer))
                {
                    throw new Exception($"LeaveRoom() | Peer:{peerId} is not exists.");
                }

                using (_peerRoomLocker.WriterLock())
                {
                    if (!peer.Rooms.TryGetValue(roomId, out var room))
                    {
                        throw new Exception($"LeaveRoom() | Peer:{peerId} is not exists in Room:{roomId}.");
                    }

                    // 离开房间
                    peer.LeaveRoom(roomId);

                    // Peer 和 Room 的关系
                    room.Peers.Remove(peerId);
                    peer.Rooms.Remove(roomId);

                    var otherPeers = room.Peers.Values.ToArray();

                    return new LeaveRoomResult
                    {
                        SelfPeer = peer,
                        OtherPeers = otherPeers,
                    };
                }
            }
        }

        public PullResult Pull(string peerId, PullRequest pullRequest)
        {
            using (_peersLocker.ReaderLock())
            {
                if (!Peers.TryGetValue(peerId, out var peer))
                {
                    throw new Exception($"Pull() | Peer:{peerId} is not exists.");
                }

                if (!Peers.TryGetValue(pullRequest.ProducerPeerId, out var producePeer))
                {
                    throw new Exception($"Pull() | Peer:{pullRequest.ProducerPeerId} is not exists.");
                }

                using (_peerRoomLocker.ReaderLock())
                {
                    if (!peer.Rooms.TryGetValue(pullRequest.RoomId, out var room))
                    {
                        throw new Exception($"Pull() | Peer:{peerId} is not exists in Room:{pullRequest.RoomId}.");
                    }

                    if (!room.Peers.ContainsKey(pullRequest.ProducerPeerId))
                    {
                        throw new Exception($"Pull() | Peer:{pullRequest.ProducerPeerId} is not exists in Room:{pullRequest.RoomId}.");
                    }

                    var pullResult = peer.Pull(producePeer, pullRequest.RoomId, pullRequest.Sources);

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

        public async Task<Consumer> ConsumeAsync(string producerPeerId, string cosumerPeerId, Producer producer, string roomId)
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
                var consumer = await cosumerPeer.ConsumeAsync(producerPeer, producer, roomId);
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
