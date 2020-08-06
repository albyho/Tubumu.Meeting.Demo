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

        private readonly object _peerRoomLocker = new object();

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

        public bool PeerJoin(string peerId, JoinRequest joinRequest)
        {
            using (_peerLocker.Lock())
            {
                if (Peers.TryGetValue(peerId, out var peer))
                {
                    _logger.LogError($"PeerJoinAsync() | Peer:{peerId} was joined.");
                    return false;
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

        public LeaveResult PeerLeave(string peerId)
        {
            using (_peerLocker.Lock())
            {
                if (!Peers.TryGetValue(peerId, out var peer))
                {
                    _logger.LogError($"PeerLeave() | Peer:{peerId} is not exists.");
                    throw new Exception($"Peer:{peerId} is not exists.");
                }

                var otherPeers = new List<PeerIdRoomId>();
                lock (_peerRoomLocker)
                {
                    // 从所有房间退出
                    foreach (var room in peer.Rooms.Values)
                    {
                        peer.CloseProducersNoConsumers(room.RoomId);
                        var otherPeersInRoom = room.Peers.Values.Where(m => m.PeerId != peerId);
                        var otherPeerRoomsInRoom = otherPeersInRoom.Select(m => new PeerIdRoomId
                        {
                            PeerId = m,
                            RoomId = room
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
                    OtherPeers = otherPeers.ToArray(),
                };
            }
        }

        public Task<WebRtcTransport> PeerCreateWebRtcTransportAsync(string peerId, CreateWebRtcTransportRequest createWebRtcTransportRequest)
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

        public Task<bool> PeerConnectWebRtcTransportAsync(string peerId, ConnectWebRtcTransportRequest connectWebRtcTransportRequest)
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

        public async Task<JoinRoomResult> PeerJoinRoomAsync(string peerId, JoinRoomRequest joinRoomRequest)
        {
            using (await _peerLocker.LockAsync())
            {
                if (!Peers.TryGetValue(peerId, out var peer))
                {
                    _logger.LogError($"PeerJoinRoomAsync() | Peer:{peerId} is not exists.");
                    throw new Exception($"Peer:{peerId} is not exists.");
                }
                using (await _roomLocker.LockAsync())
                {
                    if (!Rooms.TryGetValue(joinRoomRequest.RoomId, out var room))
                    {
                        room = await CreateRoom(joinRoomRequest.RoomId, "Default");
                        Rooms[room.RoomId] = room;
                    }

                    lock (_peerRoomLocker)
                    {
                        room.Peers[peerId] = peer;
                        peer.Rooms[joinRoomRequest.RoomId] = room;

                        var otherPeers = room.Peers.Values.Where(m => m.PeerId != peerId).ToArray();
                        return new JoinRoomResult
                        {
                            Peer = peer,
                            OtherPeers = otherPeers,
                        };
                    }
                }
            }
        }

        public LeaveRoomResult PeerLeaveRoom(string peerId, string roomId)
        {
            using (_peerLocker.Lock())
            {
                if (!Peers.TryGetValue(peerId, out var peer))
                {
                    _logger.LogError($"PeerLeaveRoom() | Peer:{peerId} is not exists.");
                    throw new Exception($"Peer:{peerId} is not exists.");
                }

                lock (_peerRoomLocker)
                {
                    if (!peer.Rooms.TryGetValue(roomId, out var room))
                    {
                        _logger.LogError($"PeerLeaveRoom() | Peer:{peerId} is not exists in Room:{roomId}.");
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

        public ConsumeResult PeerConsume(string peerId, ConsumeRequest consumeRequest)
        {
            using (_peerLocker.Lock())
            {
                if (!Peers.TryGetValue(peerId, out var peer))
                {
                    _logger.LogError($"PeerInviteProduce() | Peer:{peerId} is not exists.");
                    throw new Exception($"Peer:{peerId} is not exists.");
                }

                if (!Peers.TryGetValue(consumeRequest.PeerId, out var otherPeer))
                {
                    _logger.LogError($"PeerInviteProduce() | Peer:{consumeRequest.PeerId} is not exists.");
                    throw new Exception($"Peer:{consumeRequest.PeerId} is not exists.");
                }

                lock (_peerRoomLocker)
                {
                    if (!peer.Rooms.TryGetValue(consumeRequest.RoomId, out var room))
                    {
                        _logger.LogError($"PeerInviteProduce() | Peer:{peerId} is not exists in Room:{consumeRequest.RoomId}.");
                        throw new Exception($"Peer:{peerId} is not exists in Room:{consumeRequest.RoomId}.");
                    }

                    if (!room.Peers.ContainsKey(consumeRequest.PeerId))
                    {
                        _logger.LogError($"PeerInviteProduce() | Peer:{consumeRequest.PeerId} is not exists in Room:{consumeRequest.RoomId}.");
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
                        otherPeer.ConsumerPaddings[source!] = new PeerIdRoomId
                        {
                            RoomId = room.RoomId,
                            PeerId = peer.PeerId,
                        };
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

        public async Task<Producer> PeerProduceAsync(string peerId, ProduceRequest produceRequest)
        {
            using (await _peerLocker.LockAsync())
            {
                if (!Peers.TryGetValue(peerId, out var peer))
                {
                    _logger.LogError($"PeerProduceAsync() | Peer:{peerId} is not exists.");
                    throw new Exception($"Peer:{peerId} is not exists.");
                }
                return await peer.ProduceAsync(produceRequest);
            }
        }

        public async Task<bool> PeerCloseProducerAsync(string peerId, string producerId)
        {
            using (await _peerLocker.LockAsync())
            {
                if (!Peers.TryGetValue(peerId, out var peer))
                {
                    _logger.LogError($"PeerCloseProducer() | Peer:{peerId} is not exists.");
                    throw new Exception($"Peer:{peerId} is not exists.");
                }
                return await peer.CloseProducerAsync(producerId);
            }
        }

        public async Task<bool> PeerPauseProducerAsync(string peerId, string producerId)
        {
            using (await _peerLocker.LockAsync())
            {
                if (!Peers.TryGetValue(peerId, out var peer))
                {
                    _logger.LogError($"PeerPauseProducer() | Peer:{peerId} is not exists.");
                    throw new Exception($"Peer:{peerId} is not exists.");
                }
                return await peer.PauseProducerAsync(producerId);
            }
        }

        public async Task<bool> PeerResumeProducerAsync(string peerId, string producerId)
        {
            using (await _peerLocker.LockAsync())
            {
                if (!Peers.TryGetValue(peerId, out var peer))
                {
                    _logger.LogError($"PeerResumeProducer() | Peer:{peerId} is not exists.");
                    throw new Exception($"Peer:{peerId} is not exists.");
                }
                return await peer.ResumeProducerAsync(producerId);
            }
        }

        public async Task<bool> PeerCloseConsumerAsync(string peerId, string consumerId)
        {
            using (await _peerLocker.LockAsync())
            {
                if (!Peers.TryGetValue(peerId, out var peer))
                {
                    _logger.LogError($"PeerCloseConsumer() | Peer:{peerId} is not exists.");
                    throw new Exception($"Peer:{peerId} is not exists.");
                }
                return await peer.CloseConsumerAsync(consumerId);
            }
        }

        public async Task<bool> PeerPauseConsumerAsync(string peerId, string consumerId)
        {
            using (await _peerLocker.LockAsync())
            {
                if (!Peers.TryGetValue(peerId, out var peer))
                {
                    _logger.LogError($"PeerPauseConsumer() | Peer:{peerId} is not exists.");
                    throw new Exception($"Peer:{peerId} is not exists.");
                }
                return await peer.PauseConsumerAsync(consumerId);
            }
        }

        public async Task<bool> PeerResumeConsumerAsync(string peerId, string consumerId)
        {
            using (await _peerLocker.LockAsync())
            {
                if (!Peers.TryGetValue(peerId, out var peer))
                {
                    _logger.LogError($"PeerResumeConsumer() | Peer:{peerId} is not exists.");
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
                    _logger.LogError($"SetConsumerPreferedLayers() | Peer:{peerId} is not exists.");
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
                    _logger.LogError($"SetConsumerPriority() | Peer:{peerId} is not exists.");
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
                    _logger.LogError($"RequestConsumerKeyFrame() | Peer:{peerId} is not exists.");
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
                    _logger.LogError($"GetTransportStats() | Peer:{peerId} is not exists.");
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
                    _logger.LogError($"GetProducerStats() | Peer:{peerId} is not exists.");
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
                    _logger.LogError($"GetConsumerStats() | Peer:{peerId} is not exists.");
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
                    _logger.LogError($"RestartIce() | Peer:{peerId} is not exists.");
                    throw new Exception($"Peer:{peerId} is not exists.");
                }
                return await peer.RestartIceAsync(transportId);
            }
        }

        #region Private Methods

        private async Task<Room> CreateRoom(string roomId, string name)
        {
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

            var room = new Room(_loggerFactory, _router, roomId, name);
            Rooms[roomId] = room;
            return room;
        }

        #endregion
    }
}
