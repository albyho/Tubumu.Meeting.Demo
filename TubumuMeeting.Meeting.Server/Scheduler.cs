using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Nito.AsyncEx;
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

        private readonly AsyncLock _peerLocker = new AsyncLock();

        private readonly AsyncLock _roomLocker = new AsyncLock();

        private readonly AsyncLock _peerRoomLocker = new AsyncLock();  // 直接或间接访问 peer.Rooms 或 room.Peers 需上锁。

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

            var rtpCodecCapabilities = mediasoupOptions.MediasoupSettings.RouterSettings.RtpCodecCapabilities;
            // This may throw.
            DefaultRtpCapabilities = ORTC.GenerateRouterRtpCapabilities(rtpCodecCapabilities);
        }

        public async Task<bool> Join(string peerId, JoinRequest joinRequest)
        {
            using (_peerLocker.Lock())
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
                    peerId,
                    joinRequest.DisplayName,
                    joinRequest.Sources,
                    joinRequest.AppData
                    )
                {
                    RtpCapabilities = joinRequest.RtpCapabilities,
                    SctpCapabilities = joinRequest.SctpCapabilities,
                };

                Peers[peerId] = peer;

                return true;
            }
        }

        public LeaveResult Leave(string peerId)
        {
            using (_peerLocker.Lock())
            {
                if (!Peers.TryGetValue(peerId, out var peer))
                {
                    _logger.LogError($"PeerLeave() | Peer:{peerId} is not exists.");
                    throw new Exception($"Peer:{peerId} is not exists.");
                }

                var otherPeers = new List<PeerRoom>();
                using (_peerRoomLocker.Lock())
                {
                    // 从所有房间退出
                    foreach (var room in peer.Rooms.Values)
                    {
                        peer.CloseProducersNoConsumers(room.RoomId);
                        var otherPeersInRoom = room.Peers.Values.Where(m => m.PeerId != peerId);
                        var otherPeerRoomsInRoom = otherPeersInRoom.Select(m => new PeerRoom
                        {
                            Peer = m,
                            Room = room
                        });
                        otherPeers.AddRange(otherPeerRoomsInRoom);
                        foreach (var otherPeer in otherPeersInRoom)
                        {
                            otherPeer.CloseProducersNoConsumers(room.RoomId, peer.PeerId);
                        }

                        room.Peers.Remove(peerId);
                    }

                    peer.Rooms.Clear();
                }

                peer.Close();
                Peers.Remove(peerId);

                return new LeaveResult
                {
                    Peer = peer,
                    OtherPeerRooms = otherPeers.ToArray(),
                };
            }
        }

        public Task<WebRtcTransport> CreateWebRtcTransportAsync(string peerId, CreateWebRtcTransportRequest createWebRtcTransportRequest)
        {
            using (_peerLocker.Lock())
            {
                if (!Peers.TryGetValue(peerId, out var peer))
                {
                    _logger.LogError($"CreateWebRtcTransport() | Peer:{peerId} is not exists.");
                    throw new Exception($"Peer:{peerId} is not exists");
                }

                return peer.CreateWebRtcTransportAsync(createWebRtcTransportRequest);
            }
        }

        public Task<bool> ConnectWebRtcTransportAsync(string peerId, ConnectWebRtcTransportRequest connectWebRtcTransportRequest)
        {
            using (_peerLocker.Lock())
            {
                if (!Peers.TryGetValue(peerId, out var peer))
                {
                    _logger.LogError($"CreateWebRtcTransport() | Peer:{peerId} is not exists.");
                    throw new Exception($"Peer:{peerId} is not exists");
                }

                return peer.ConnectWebRtcTransportAsync(connectWebRtcTransportRequest);
            }
        }

        public async Task<JoinRoomResult> JoinRoomAsync(string peerId, JoinRoomRequest joinRoomRequest)
        {
            using (await _peerLocker.LockAsync())
            {
                if (!Peers.TryGetValue(peerId, out var peer))
                {
                    _logger.LogError($"JoinRoomAsync() | Peer:{peerId} is not exists.");
                    throw new Exception($"Peer:{peerId} is not exists.");
                }
                using (await _roomLocker.LockAsync())
                {
                    if (!Rooms.TryGetValue(joinRoomRequest.RoomId, out var room))
                    {
                        room = await CreateRoom(joinRoomRequest.RoomId, "Default");
                        Rooms[room.RoomId] = room;
                    }

                    using (_peerRoomLocker.Lock())
                    {
                        room.Peers[peerId] = peer;
                        peer.Rooms[joinRoomRequest.RoomId] = room;

                        var peersInRoom = room.Peers.Values.ToArray();
                        return new JoinRoomResult
                        {
                            Peer = peer,
                            PeersInRoom = peersInRoom,
                        };
                    }
                }
            }
        }

        public LeaveRoomResult LeaveRoom(string peerId, string roomId)
        {
            using (_peerLocker.Lock())
            {
                if (!Peers.TryGetValue(peerId, out var peer))
                {
                    _logger.LogError($"LeaveRoom() | Peer:{peerId} is not exists.");
                    throw new Exception($"Peer:{peerId} is not exists.");
                }

                using (_peerRoomLocker.Lock())
                {
                    if (!peer.Rooms.TryGetValue(roomId, out var room))
                    {
                        _logger.LogError($"LeaveRoom() | Peer:{peerId} is not exists in Room:{roomId}.");
                        throw new Exception($"Peer:{peerId} is not exists in Room:{roomId}.");
                    }

                    // 离开 Room 之前，先关闭将无人消费的 Producer
                    peer.CloseProducersNoConsumers(roomId);

                    var otherPeers = room.Peers.Values.Where(m => m.PeerId != peerId).ToArray();
                    foreach (var otherPeer in otherPeers)
                    {
                        otherPeer.CloseProducersNoConsumers(roomId, peer.PeerId);
                    }

                    room.Peers.Remove(peerId);
                    peer.Rooms.Remove(roomId);

                    return new LeaveRoomResult
                    {
                        Peer = peer,
                        OtherPeers = otherPeers,
                    };
                }
            }
        }

        public ConsumeResult Consume(string peerId, ConsumeRequest consumeRequest)
        {
            using (_peerLocker.Lock())
            {
                if (!Peers.TryGetValue(peerId, out var peer))
                {
                    _logger.LogError($"Consume() | Peer:{peerId} is not exists.");
                    throw new Exception($"Peer:{peerId} is not exists.");
                }

                if (!Peers.TryGetValue(consumeRequest.PeerId, out var otherPeer))
                {
                    _logger.LogError($"Consume() | Peer:{consumeRequest.PeerId} is not exists.");
                    throw new Exception($"Peer:{consumeRequest.PeerId} is not exists.");
                }

                using (_peerRoomLocker.Lock())
                {
                    if (!peer.Rooms.TryGetValue(consumeRequest.RoomId, out var room))
                    {
                        _logger.LogError($"Consume() | Peer:{peerId} is not exists in Room:{consumeRequest.RoomId}.");
                        throw new Exception($"Peer:{peerId} is not exists in Room:{consumeRequest.RoomId}.");
                    }

                    if (!room.Peers.ContainsKey(consumeRequest.PeerId))
                    {
                        _logger.LogError($"Consume() | Peer:{consumeRequest.PeerId} is not exists in Room:{consumeRequest.RoomId}.");
                        throw new Exception($"Peer:{consumeRequest.PeerId} is not exists in Room:{consumeRequest.RoomId}.");
                    }

                    var existsProducers = new List<PeerProducer>();
                    var produceSources = new List<string>();
                    foreach (var source in consumeRequest.Sources)
                    {
                        var producer = otherPeer.Producers.Values.Where(m => m.Source == source).FirstOrDefault();
                        // 如果 Source 有对应的 Producer，直接消费。
                        if (source != null)
                        {
                            var consumer = peer.Consumers.Values.Where(m => m.Internal.ProducerId == producer.ProducerId).FirstOrDefault();
                            if (consumer != null)
                            {
                                // 如果本 Peer 已经消费了对应 Producer，忽略以避免重复消费。
                                continue;
                            }
                            existsProducers.Add(new PeerProducer
                            {
                                Peer = otherPeer,
                                Producer = producer,
                            });
                            continue;
                        }
                        // 如果 Source 没有对应的 Producer，通知 otherPeer 生产；生产成功后又要通知本 Peer 去对应的 Room 消费。
                        produceSources.Add(source!);
                        otherPeer.ConsumePaddings.Add(new ConsumePadding
                        {
                            RoomId = room.RoomId,
                            PeerId = peer.PeerId,
                            Source = source!,
                        });
                    }

                    return new ConsumeResult
                    {
                        Peer = peer,
                        ExistsProducers = existsProducers.ToArray(),
                        ProduceSources = produceSources.ToArray(),
                    };
                }
            }
        }

        public async Task<ProduceResult> ProduceAsync(string peerId, ProduceRequest produceRequest)
        {
            using (await _peerLocker.LockAsync())
            {
                if (!Peers.TryGetValue(peerId, out var peer))
                {
                    _logger.LogError($"ProduceAsync() | Peer:{peerId} is not exists.");
                    throw new Exception($"Peer:{peerId} is not exists.");
                }

                using (await _peerRoomLocker.LockAsync())
                {
                    var producer = await peer.ProduceAsync(produceRequest);
                    if (producer == null)
                    {
                        _logger.LogError($"ProduceAsync() | Peer:{peerId} is not exists.");
                        throw new Exception($"Peer:{peerId} produce faild.");
                    }

                    var comsumePaddingsToRemove = new List<ConsumePadding>();
                    var peerRoomIds = new List<PeerRoomId>();
                    foreach (var comsumePadding in peer.ConsumePaddings.Where(m => m.Source == producer.Source))
                    {
                        comsumePaddingsToRemove.Add(comsumePadding);

                        // 其他 Peer 消费本 Peer
                        if (Peers.TryGetValue(comsumePadding.PeerId, out var otherPeer))
                        {
                            peerRoomIds.Add(new PeerRoomId
                            {
                                Peer = otherPeer,
                                RoomId = comsumePadding.RoomId,
                            });
                        }
                    }

                    foreach (var consumePaddingToRemove in comsumePaddingsToRemove)
                    {
                        peer.ConsumePaddings.Remove(consumePaddingToRemove);
                    }

                    var produceResult = new ProduceResult
                    {
                        Peer = peer,
                        Producer = producer,
                        PeerRoomIds = peerRoomIds.ToArray(),
                    };

                    return produceResult;
                }
            }
        }

        public async Task<Consumer> ConsumeAsync(string peerId, Producer producer, string roomId)
        {
            using (await _peerLocker.LockAsync())
            {
                if (!Peers.TryGetValue(peerId, out var peer))
                {
                    _logger.LogError($"ConsumeAsync() | Peer:{peerId} is not exists.");
                    throw new Exception($"Peer:{peerId} is not exists.");
                }
                using (await _roomLocker.LockAsync())
                {
                    if (!Rooms.ContainsKey(roomId))
                    {
                        _logger.LogError($"ConsumeAsync() | Room:{roomId} is not exists.");
                        throw new Exception($"Room:{roomId} is not exists.");
                    }

                    var consumer = await peer.ConsumeAsync(producer, roomId);
                    if (producer == null)
                    {
                        _logger.LogError($"ConsumeAsync() | Peer:{peerId} is not exists.");
                        throw new Exception($"Peer:{peerId} produce faild.");
                    }

                    return consumer;
                }
            }
        }

        public async Task<bool> CloseProducerAsync(string peerId, string producerId)
        {
            using (await _peerLocker.LockAsync())
            {
                if (!Peers.TryGetValue(peerId, out var peer))
                {
                    _logger.LogError($"CloseProducerAsync() | Peer:{peerId} is not exists.");
                    throw new Exception($"Peer:{peerId} is not exists.");
                }
                return await peer.CloseProducerAsync(producerId);
            }
        }

        public async Task<bool> PauseProducerAsync(string peerId, string producerId)
        {
            using (await _peerLocker.LockAsync())
            {
                if (!Peers.TryGetValue(peerId, out var peer))
                {
                    _logger.LogError($"PauseProducerAsync() | Peer:{peerId} is not exists.");
                    throw new Exception($"Peer:{peerId} is not exists.");
                }
                return await peer.PauseProducerAsync(producerId);
            }
        }

        public async Task<bool> ResumeProducerAsync(string peerId, string producerId)
        {
            using (await _peerLocker.LockAsync())
            {
                if (!Peers.TryGetValue(peerId, out var peer))
                {
                    _logger.LogError($"ResumeProducerAsync() | Peer:{peerId} is not exists.");
                    throw new Exception($"Peer:{peerId} is not exists.");
                }
                return await peer.ResumeProducerAsync(producerId);
            }
        }

        public async Task<bool> CloseConsumerAsync(string peerId, string consumerId)
        {
            using (await _peerLocker.LockAsync())
            {
                if (!Peers.TryGetValue(peerId, out var peer))
                {
                    _logger.LogError($"CloseConsumerAsync() | Peer:{peerId} is not exists.");
                    throw new Exception($"Peer:{peerId} is not exists.");
                }
                return await peer.CloseConsumerAsync(consumerId);
            }
        }

        public async Task<bool> PauseConsumerAsync(string peerId, string consumerId)
        {
            using (await _peerLocker.LockAsync())
            {
                if (!Peers.TryGetValue(peerId, out var peer))
                {
                    _logger.LogError($"PauseConsumerAsync() | Peer:{peerId} is not exists.");
                    throw new Exception($"Peer:{peerId} is not exists.");
                }
                return await peer.PauseConsumerAsync(consumerId);
            }
        }

        public async Task<bool> ResumeConsumerAsync(string peerId, string consumerId)
        {
            using (await _peerLocker.LockAsync())
            {
                if (!Peers.TryGetValue(peerId, out var peer))
                {
                    _logger.LogError($"ResumeConsumerAsync() | Peer:{peerId} is not exists.");
                    throw new Exception($"Peer:{peerId} is not exists.");
                }
                return await peer.ResumeConsumerAsync(consumerId);
            }
        }

        public async Task<bool> SetConsumerPreferedLayersAsync(string peerId, SetConsumerPreferedLayersRequest setConsumerPreferedLayersRequest)
        {
            using (await _peerLocker.LockAsync())
            {
                if (!Peers.TryGetValue(peerId, out var peer))
                {
                    _logger.LogError($"SetConsumerPreferedLayersAsync() | Peer:{peerId} is not exists.");
                    throw new Exception($"Peer:{peerId} is not exists.");
                }
                return await peer.SetConsumerPreferedLayersAsync(setConsumerPreferedLayersRequest);
            }
        }

        public async Task<bool> SetConsumerPriorityAsync(string peerId, SetConsumerPriorityRequest setConsumerPriorityRequest)
        {
            using (await _peerLocker.LockAsync())
            {
                if (!Peers.TryGetValue(peerId, out var peer))
                {
                    _logger.LogError($"SetConsumerPriorityAsync() | Peer:{peerId} is not exists.");
                    throw new Exception($"Peer:{peerId} is not exists.");
                }
                return await peer.SetConsumerPriorityAsync(setConsumerPriorityRequest);
            }
        }

        public async Task<bool> RequestConsumerKeyFrameAsync(string peerId, string consumerId)
        {
            using (await _peerLocker.LockAsync())
            {
                if (!Peers.TryGetValue(peerId, out var peer))
                {
                    _logger.LogError($"RequestConsumerKeyFrameAsync() | Peer:{peerId} is not exists.");
                    throw new Exception($"Peer:{peerId} is not exists.");
                }
                return await peer.RequestConsumerKeyFrameAsync(consumerId);
            }
        }

        public async Task<TransportStat> GetTransportStatsAsync(string peerId, string transportId)
        {
            using (await _peerLocker.LockAsync())
            {
                if (!Peers.TryGetValue(peerId, out var peer))
                {
                    _logger.LogError($"GetTransportStatsAsync() | Peer:{peerId} is not exists.");
                    throw new Exception($"Peer:{peerId} is not exists.");
                }
                return await peer.GetTransportStatsAsync(transportId);
            }
        }

        public async Task<ProducerStat> GetProducerStatsAsync(string peerId, string producerId)
        {
            using (await _peerLocker.LockAsync())
            {
                if (!Peers.TryGetValue(peerId, out var peer))
                {
                    _logger.LogError($"GetProducerStatsAsync() | Peer:{peerId} is not exists.");
                    throw new Exception($"Peer:{peerId} is not exists.");
                }
                return await peer.GetProducerStatsAsync(producerId);
            }
        }

        public async Task<ConsumerStat> GetConsumerStatsAsync(string peerId, string consumerId)
        {
            using (await _peerLocker.LockAsync())
            {
                if (!Peers.TryGetValue(peerId, out var peer))
                {
                    _logger.LogError($"GetConsumerStatsAsync() | Peer:{peerId} is not exists.");
                    throw new Exception($"Peer:{peerId} is not exists.");
                }
                return await peer.GetConsumerStatsAsync(consumerId);
            }
        }

        public async Task<IceParameters?> RestartIceAsync(string peerId, string transportId)
        {
            using (await _peerLocker.LockAsync())
            {
                if (!Peers.TryGetValue(peerId, out var peer))
                {
                    _logger.LogError($"RestartIceAsync() | Peer:{peerId} is not exists.");
                    throw new Exception($"Peer:{peerId} is not exists.");
                }
                return await peer.RestartIceAsync(transportId);
            }
        }

        #region Private Methods

        private async Task<Room> CreateRoom(string roomId, string name)
        {
            var room = new Room(_loggerFactory, _router, roomId, name);
            Rooms[roomId] = room;
            return room;
        }

        #endregion
    }
}
