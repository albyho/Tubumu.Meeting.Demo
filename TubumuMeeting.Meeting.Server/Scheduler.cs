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

        private readonly Dictionary<string, Peer> _peers = new Dictionary<string, Peer>();

        private readonly AsyncReaderWriterLock _peersLock = new AsyncReaderWriterLock();

        private readonly Dictionary<string, Room> _rooms = new Dictionary<string, Room>();

        private readonly AsyncAutoResetEvent _roomsLock = new AsyncAutoResetEvent();

        //private readonly Dictionary<string, List<RoomWithRoomAppData>> _peerRooms = new Dictionary<string, List<RoomWithRoomAppData>>();

        /// <summary>
        /// _peerRooms 锁。增改 List<RoomWithRoomAppData> 也应该用写锁。
        /// </summary>
        //private readonly AsyncReaderWriterLock _peerRoomsLock = new AsyncReaderWriterLock();

        // private readonly Dictionary<string, List<PeerWithRoomAppData>> _roomPeers = new Dictionary<string, List<PeerWithRoomAppData>>();

        /// <summary>
        /// _roomPeers 锁。增改 List<PeerWithRoomAppData> 也应该用写锁。
        /// </summary>
        //private readonly AsyncReaderWriterLock _roomPeersLock = new AsyncReaderWriterLock();

        //private readonly AsyncAutoResetEvent _peerAppDataLock = new AsyncAutoResetEvent();

        //private readonly AsyncAutoResetEvent _roomAppDataLock = new AsyncAutoResetEvent();

        #endregion Private Fields

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

            _roomsLock.Set();
            //_peerAppDataLock.Set();
            //_roomAppDataLock.Set();
        }

        public async Task<bool> JoinAsync(string peerId, string connectionId, JoinRequest joinRequest)
        {
            using (await _peersLock.WriteLockAsync())
            {
                if (_peers.TryGetValue(peerId, out var peer))
                {
                    if (peer.ConnectionId == connectionId)
                    {
                        _logger.LogError($"PeerJoinAsync() | Peer:{peerId} was joined.");
                        return false;
                    }
                }

                peer = new Peer(_loggerFactory,
                    _mediasoupOptions.MediasoupSettings.WebRtcTransportSettings,
                    joinRequest.RtpCapabilities,
                    joinRequest.SctpCapabilities,
                    peerId,
                    connectionId,
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
            using (await _peersLock.WriteLockAsync())
            {
                if (!_peers.TryGetValue(peerId, out var peer))
                {
                    // _logger.LogWarning($"PeerLeave() | Peer:{peerId} is not exists.");
                    return null;
                }

                _peers.Remove(peerId);

                return await peer.LeaveAsync();
            }
        }

        public async Task<PeerAppDataResult> SetPeerAppDataAsync(string peerId, string connectionId, SetPeerAppDataRequest setPeerAppDataRequest)
        {
            using (await _peersLock.ReadLockAsync())
            {
                if (!_peers.TryGetValue(peerId, out var peer))
                {
                    throw new Exception($"SetPeerAppDataAsync() | Peer:{peerId} is not exists.");
                }

                CheckConnection(peer, connectionId);

                return await peer.SetPeerAppDataAsync(setPeerAppDataRequest);
            }
        }

        public async Task<PeerAppDataResult> UnsetPeerAppDataAsync(string peerId, string connectionId, UnsetPeerAppDataRequest unsetPeerAppDataRequest)
        {
            using (await _peersLock.ReadLockAsync())
            {
                if (!_peers.TryGetValue(peerId, out var peer))
                {
                    throw new Exception($"UnsetPeerAppDataAsync() | Peer:{peerId} is not exists.");
                }

                CheckConnection(peer, connectionId);

                return await peer.UnsetPeerAppDataAsync(unsetPeerAppDataRequest);
            }
        }

        public async Task<PeerAppDataResult> ClearPeerAppDataAsync(string peerId, string connectionId)
        {
            using (await _peersLock.ReadLockAsync())
            {
                if (!_peers.TryGetValue(peerId, out var peer))
                {
                    throw new Exception($"ClearPeerAppDataAsync() | Peer:{peerId} is not exists.");
                }

                CheckConnection(peer, connectionId);

                return await peer.ClearPeerAppDataAsync();
            }
        }

        public async Task<WebRtcTransport> CreateWebRtcTransportAsync(string peerId, string connectionId, CreateWebRtcTransportRequest createWebRtcTransportRequest)
        {
            using (await _peersLock.ReadLockAsync())
            {
                if (!_peers.TryGetValue(peerId, out var peer))
                {
                    throw new Exception($"CreateWebRtcTransport() | Peer:{peerId} is not exists");
                }

                CheckConnection(peer, connectionId);

                return await peer.CreateWebRtcTransportAsync(createWebRtcTransportRequest);
            }
        }

        public async Task<bool> ConnectWebRtcTransportAsync(string peerId, string connectionId, ConnectWebRtcTransportRequest connectWebRtcTransportRequest)
        {
            using (await _peersLock.ReadLockAsync())
            {
                if (!_peers.TryGetValue(peerId, out var peer))
                {
                    throw new Exception($"ConnectWebRtcTransportAsync() | Peer:{peerId} is not exists");
                }

                CheckConnection(peer, connectionId);

                return await peer.ConnectWebRtcTransportAsync(connectWebRtcTransportRequest);
            }
        }

        public async Task<JoinRoomResult> JoinRoomAsync(string peerId, string connectionId, JoinRoomRequest joinRoomRequest)
        {
            using (await _peersLock.ReadLockAsync())
            {
                if (!_peers.TryGetValue(peerId, out var peer))
                {
                    throw new Exception($"JoinRoomAsync() | Peer:{peerId} is not exists.");
                }

                CheckConnection(peer, connectionId);

                if (joinRoomRequest.RoomSources.Except(peer.Sources).Any())
                {
                    throw new Exception($"JoinRoomAsync() | Peer:{peerId} don't has some sources which is in Room:{joinRoomRequest.RoomId}.");
                }

                await _roomsLock.WaitAsync();
                try
                {
                    // Room 如果不存在则创建
                    if (!_rooms.TryGetValue(joinRoomRequest.RoomId, out var room))
                    {
                        // Router media codecs.
                        var mediaCodecs = _mediasoupOptions.MediasoupSettings.RouterSettings.RtpCodecCapabilities;

                        // Create a mediasoup Router.
                        var worker = _mediasoupServer.GetWorker();
                        var router = await worker.CreateRouterAsync(new RouterOptions
                        {
                            MediaCodecs = mediaCodecs
                        });
                        if (router == null)
                        {
                            throw new Exception($"PeerJoinAsync() | Worker maybe closed.");
                        }

                        room = new Room(_loggerFactory, router, joinRoomRequest.RoomId, "Default");
                        _rooms[room.RoomId] = room;
                    }

                    return await peer.JoinRoomAsync(room, joinRoomRequest.RoomSources, joinRoomRequest.RoomAppData);
                }
                finally
                {
                    _roomsLock.Set();
                }
            }
        }

        public async Task<LeaveRoomResult> LeaveRoomAsync(string peerId, string connectionId, string roomId)
        {
            using (await _peersLock.ReadLockAsync())
            {
                if (!_peers.TryGetValue(peerId, out var peer))
                {
                    throw new Exception($"LeaveRoom() | Peer:{peerId} is not exists.");
                }

                CheckConnection(peer, connectionId);

                return await peer.LeaveRoomAsync();
            }
        }

        //public async Task<PeerRoomAppDataResult> SetRoomAppDataAsync(string peerId, string connectionId, SetRoomAppDataRequest setRoomAppDataRequest)
        //{
        //    using (await _peersLock.ReadLockAsync())
        //    {
        //        if (!_peers.TryGetValue(peerId, out var peer))
        //        {
        //            throw new Exception($"SetRoomAppDataAsync() | Peer:{peerId} is not exists.");
        //        }

        //        CheckConnection(peer, connectionId);

        //        using (await _peerRoomsLock.ReadLockAsync())
        //        {
        //            if (!_peerRooms.TryGetValue(peerId, out var peerRooms))
        //            {
        //                throw new Exception($"SetRoomAppDataAsync() | Peer:{peerId} is not in any room.");
        //            }

        //            var peerRoom = peerRooms.FirstOrDefault(m => m.Room.RoomId == setRoomAppDataRequest.RoomId);
        //            if (peerRoom == null)
        //            {
        //                throw new Exception($"SetRoomAppDataAsync() | Peer:{peerId} is not exists in Room:{setRoomAppDataRequest.RoomId}.");
        //            }

        //            using (await _roomPeersLock.ReadLockAsync())
        //            {
        //                if (!_roomPeers.TryGetValue(setRoomAppDataRequest.RoomId, out var roomPeers))
        //                {
        //                    throw new Exception($"SetRoomAppDataAsync() | Peer:{peerId} is not in any room.");
        //                }

        //                var roomPeer = roomPeers.FirstOrDefault(m => m.Peer.PeerId == peerId);
        //                if (roomPeer == null)
        //                {
        //                    throw new Exception($"SetRoomAppDataAsync() | Peer:{peerId} is not exists in Room:{setRoomAppDataRequest.RoomId}.");
        //                }

        //                await _roomAppDataLock.WaitAsync();
        //                foreach (var item in setRoomAppDataRequest.RoomAppData)
        //                {
        //                    peerRoom.RoomAppData[item.Key] = item.Value;
        //                    roomPeer.RoomAppData[item.Key] = item.Value;
        //                }
        //                _roomAppDataLock.Set();

        //                // 只通知本房间
        //                return new PeerRoomAppDataResult
        //                {
        //                    SelfPeerId = peerId,
        //                    AppData = peerRoom.RoomAppData,
        //                    OtherPeerIds = roomPeers.Where(m => m.Peer.PeerId != peerId).Select(m => m.Peer.PeerId).ToArray(),
        //                };
        //            }
        //        }
        //    }
        //}

        //public async Task<PeerRoomAppDataResult> UnsetRoomAppDataAsync(string peerId, string connectionId, UnsetRoomAppDataRequest unsetRoomAppDataRequest)
        //{
        //    using (await _peersLock.ReadLockAsync())
        //    {
        //        if (!_peers.TryGetValue(peerId, out var peer))
        //        {
        //            throw new Exception($"UnsetRoomAppDataAsync() | Peer:{peerId} is not exists.");
        //        }

        //        CheckConnection(peer, connectionId);

        //        using (await _peerRoomsLock.ReadLockAsync())
        //        {
        //            if (!_peerRooms.TryGetValue(peerId, out var peerRooms))
        //            {
        //                throw new Exception($"SetRoomAppDataAsync() | Peer:{peerId} is not in any room.");
        //            }

        //            var peerRoom = peerRooms.FirstOrDefault(m => m.Room.RoomId == unsetRoomAppDataRequest.RoomId);
        //            if (peerRoom == null)
        //            {
        //                throw new Exception($"SetRoomAppDataAsync() | Peer:{peerId} is not exists in Room:{unsetRoomAppDataRequest.RoomId}.");
        //            }

        //            using (await _roomPeersLock.ReadLockAsync())
        //            {
        //                if (!_roomPeers.TryGetValue(unsetRoomAppDataRequest.RoomId, out var roomPeers))
        //                {
        //                    throw new Exception($"SetRoomAppDataAsync() | Peer:{peerId} is not in any room.");
        //                }

        //                var roomPeer = roomPeers.FirstOrDefault(m => m.Peer.PeerId == peerId);
        //                if (roomPeer == null)
        //                {
        //                    throw new Exception($"SetRoomAppDataAsync() | Peer:{peerId} is not exists in Room:{unsetRoomAppDataRequest.RoomId}.");
        //                }

        //                await _roomAppDataLock.WaitAsync();
        //                foreach (var item in unsetRoomAppDataRequest.Keys)
        //                {
        //                    peerRoom.RoomAppData.Remove(item);
        //                    roomPeer.RoomAppData.Remove(item);
        //                }
        //                _roomAppDataLock.Set();

        //                // 只通知本房间
        //                return new PeerRoomAppDataResult
        //                {
        //                    SelfPeerId = peerId,
        //                    AppData = peerRoom.RoomAppData,
        //                    OtherPeerIds = roomPeers.Where(m => m.Peer.PeerId != peerId).Select(m => m.Peer.PeerId).ToArray(),
        //                };
        //            }
        //        }
        //    }
        //}

        //public async Task<PeerRoomAppDataResult> ClearRoomAppDataAsync(string peerId, string connectionId, string roomId)
        //{
        //    using (await _peersLock.ReadLockAsync())
        //    {
        //        if (!_peers.TryGetValue(peerId, out var peer))
        //        {
        //            throw new Exception($"ClearRoomAppDataAsync() | Peer:{peerId} is not exists.");
        //        }

        //        CheckConnection(peer, connectionId);

        //        using (await _peerRoomsLock.ReadLockAsync())
        //        {
        //            if (!_peerRooms.TryGetValue(peerId, out var peerRooms))
        //            {
        //                throw new Exception($"SetRoomAppDataAsync() | Peer:{peerId} is not in any room.");
        //            }

        //            var peerRoom = peerRooms.FirstOrDefault(m => m.Room.RoomId == roomId);
        //            if (peerRoom == null)
        //            {
        //                throw new Exception($"SetRoomAppDataAsync() | Peer:{peerId} is not exists in Room:{roomId}.");
        //            }

        //            using (await _roomPeersLock.ReadLockAsync())
        //            {
        //                if (!_roomPeers.TryGetValue(roomId, out var roomPeers))
        //                {
        //                    throw new Exception($"SetRoomAppDataAsync() | Peer:{peerId} is not in any room.");
        //                }

        //                var roomPeer = roomPeers.FirstOrDefault(m => m.Peer.PeerId == peerId);
        //                if (roomPeer == null)
        //                {
        //                    throw new Exception($"SetRoomAppDataAsync() | Peer:{peerId} is not exists in Room:{roomId}.");
        //                }

        //                await _roomAppDataLock.WaitAsync();
        //                peerRoom.RoomAppData.Clear();
        //                roomPeer.RoomAppData.Clear();
        //                _roomAppDataLock.Set();

        //                // 只通知本房间
        //                return new PeerRoomAppDataResult
        //                {
        //                    SelfPeerId = peerId,
        //                    AppData = peerRoom.RoomAppData,
        //                    OtherPeerIds = roomPeers.Where(m => m.Peer.PeerId != peerId).Select(m => m.Peer.PeerId).ToArray(),
        //                };
        //            }
        //        }
        //    }
        //}

        public async Task<PullResult> PullAsync(string peerId, string connectionId, PullRequest pullRequest)
        {
            using (await _peersLock.ReadLockAsync())
            {
                if (!_peers.TryGetValue(peerId, out var peer))
                {
                    throw new Exception($"Pull() | Peer:{peerId} is not exists.");
                }

                CheckConnection(peer, connectionId);

                if (!_peers.TryGetValue(pullRequest.ProducerPeerId, out var producePeer))
                {
                    throw new Exception($"Pull() | Peer:{pullRequest.ProducerPeerId} is not exists.");
                }

                var pullResult = await peer.PullAsync(producePeer, pullRequest.RoomSources);

                return new PullResult
                {
                    ConsumePeer = peer,
                    ProducePeer = producePeer,
                    ExistsProducers = pullResult.ExistsProducers,
                    ProduceSources = pullResult.ProduceSources,
                };
            }
        }

        public async Task<ProduceResult> ProduceAsync(string peerId, string connectionId, ProduceRequest produceRequest)
        {
            using (await _peersLock.ReadLockAsync())
            {
                if (!_peers.TryGetValue(peerId, out var peer))
                {
                    throw new Exception($"ProduceAsync() | Peer:{peerId} is not exists.");
                }

                CheckConnection(peer, connectionId);

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
            using (await _peersLock.ReadLockAsync())
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

        public async Task<bool> CloseProducerAsync(string peerId, string connectionId, string producerId)
        {
            using (await _peersLock.ReadLockAsync())
            {
                if (!_peers.TryGetValue(peerId, out var peer))
                {
                    throw new Exception($"CloseProducerAsync() | Peer:{peerId} is not exists.");
                }

                CheckConnection(peer, connectionId);

                return await peer.CloseProducerAsync(producerId);
            }
        }

        public async Task<bool> PauseProducerAsync(string peerId, string connectionId, string producerId)
        {
            using (await _peersLock.ReadLockAsync())
            {
                if (!_peers.TryGetValue(peerId, out var peer))
                {
                    throw new Exception($"PauseProducerAsync() | Peer:{peerId} is not exists.");
                }

                CheckConnection(peer, connectionId);

                return await peer.PauseProducerAsync(producerId);
            }
        }

        public async Task<bool> ResumeProducerAsync(string peerId, string connectionId, string producerId)
        {
            using (await _peersLock.ReadLockAsync())
            {
                if (!_peers.TryGetValue(peerId, out var peer))
                {
                    throw new Exception($"ResumeProducerAsync() | Peer:{peerId} is not exists.");
                }

                CheckConnection(peer, connectionId);

                return await peer.ResumeProducerAsync(producerId);
            }
        }

        public async Task<bool> CloseConsumerAsync(string peerId, string connectionId, string consumerId)
        {
            using (await _peersLock.ReadLockAsync())
            {
                if (!_peers.TryGetValue(peerId, out var peer))
                {
                    throw new Exception($"CloseConsumerAsync() | Peer:{ peerId} is not exists.");
                }

                CheckConnection(peer, connectionId);

                return await peer.CloseConsumerAsync(consumerId);
            }
        }

        public async Task<bool> PauseConsumerAsync(string peerId, string connectionId, string consumerId)
        {
            using (await _peersLock.ReadLockAsync())
            {
                if (!_peers.TryGetValue(peerId, out var peer))
                {
                    throw new Exception($"PauseConsumerAsync() | Peer:{peerId} is not exists.");
                }

                CheckConnection(peer, connectionId);

                return await peer.PauseConsumerAsync(consumerId);
            }
        }

        public async Task<Consumer> ResumeConsumerAsync(string peerId, string connectionId, string consumerId)
        {
            using (await _peersLock.ReadLockAsync())
            {
                if (!_peers.TryGetValue(peerId, out var peer))
                {
                    throw new Exception($"ResumeConsumerAsync() | Peer:{peerId} is not exists.");
                }

                CheckConnection(peer, connectionId);

                return await peer.ResumeConsumerAsync(consumerId);
            }
        }

        public async Task<bool> SetConsumerPreferedLayersAsync(string peerId, string connectionId, SetConsumerPreferedLayersRequest setConsumerPreferedLayersRequest)
        {
            using (await _peersLock.ReadLockAsync())
            {
                if (!_peers.TryGetValue(peerId, out var peer))
                {
                    throw new Exception($"SetConsumerPreferedLayersAsync() | Peer:{peerId} is not exists.");
                }

                CheckConnection(peer, connectionId);

                return await peer.SetConsumerPreferedLayersAsync(setConsumerPreferedLayersRequest);
            }
        }

        public async Task<bool> SetConsumerPriorityAsync(string peerId, string connectionId, SetConsumerPriorityRequest setConsumerPriorityRequest)
        {
            using (await _peersLock.ReadLockAsync())
            {
                if (!_peers.TryGetValue(peerId, out var peer))
                {
                    throw new Exception($"SetConsumerPriorityAsync() | Peer:{peerId} is not exists.");
                }

                CheckConnection(peer, connectionId);

                return await peer.SetConsumerPriorityAsync(setConsumerPriorityRequest);
            }
        }

        public async Task<bool> RequestConsumerKeyFrameAsync(string peerId, string connectionId, string consumerId)
        {
            using (await _peersLock.ReadLockAsync())
            {
                if (!_peers.TryGetValue(peerId, out var peer))
                {
                    throw new Exception($"RequestConsumerKeyFrameAsync() | Peer:{peerId} is not exists.");
                }

                CheckConnection(peer, connectionId);

                return await peer.RequestConsumerKeyFrameAsync(consumerId);
            }
        }

        public async Task<TransportStat> GetTransportStatsAsync(string peerId, string connectionId, string transportId)
        {
            using (await _peersLock.ReadLockAsync())
            {
                if (!_peers.TryGetValue(peerId, out var peer))
                {
                    throw new Exception($"GetTransportStatsAsync() | Peer:{peerId} is not exists.");
                }

                CheckConnection(peer, connectionId);

                return await peer.GetTransportStatsAsync(transportId);
            }
        }

        public async Task<ProducerStat> GetProducerStatsAsync(string peerId, string connectionId, string producerId)
        {
            using (await _peersLock.ReadLockAsync())
            {
                if (!_peers.TryGetValue(peerId, out var peer))
                {
                    throw new Exception($"GetProducerStatsAsync() | Peer:{peerId} is not exists.");
                }

                CheckConnection(peer, connectionId);

                return await peer.GetProducerStatsAsync(producerId);
            }
        }

        public async Task<ConsumerStat> GetConsumerStatsAsync(string peerId, string connectionId, string consumerId)
        {
            using (await _peersLock.ReadLockAsync())
            {
                if (!_peers.TryGetValue(peerId, out var peer))
                {
                    throw new Exception($"GetConsumerStatsAsync() | Peer:{peerId} is not exists.");
                }

                CheckConnection(peer, connectionId);

                return await peer.GetConsumerStatsAsync(consumerId);
            }
        }

        public async Task<IceParameters?> RestartIceAsync(string peerId, string connectionId, string transportId)
        {
            using (await _peersLock.ReadLockAsync())
            {
                if (!_peers.TryGetValue(peerId, out var peer))
                {
                    throw new Exception($"RestartIceAsync() | Peer:{peerId} is not exists.");
                }

                CheckConnection(peer, connectionId);

                return await peer.RestartIceAsync(transportId);
            }
        }

        public async Task<string[]> GetOtherPeerIdsAsync(string peerId, string connectionId)
        {
            using (await _peersLock.ReadLockAsync())
            {
                if (!_peers.TryGetValue(peerId, out var peer))
                {
                    throw new Exception($"GetOtherPeerIdsAsync() | Peer:{peerId} is not exists.");
                }

                CheckConnection(peer, connectionId);

                return await peer.GetOtherPeerIdsAsync();
            }
        }

        private void CheckConnection(Peer peer, string connectionId)
        {
            if (peer.ConnectionId != connectionId)
            {
                throw new DisconnectedException($"New: {connectionId} Old:{peer.ConnectionId}");
            }
        }
    }
}
