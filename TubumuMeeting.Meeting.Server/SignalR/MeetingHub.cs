using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Tubumu.Core.Extensions;
using TubumuMeeting.Mediasoup;
using TubumuMeeting.Mediasoup.Extensions;

namespace TubumuMeeting.Meeting.Server
{
    [Authorize]
    public partial class MeetingHub : Hub<IPeer>
    {
        private readonly ILogger<MeetingHub> _logger;
        private readonly IHubContext<MeetingHub, IPeer> _hubContext;
        private readonly BadDisconnectSocketService _badDisconnectSocketService;
        private readonly Scheduler _scheduler;

        private string UserId => Context.User.Identity.Name;
        private string ConnectionId => Context.ConnectionId;

        public MeetingHub(ILogger<MeetingHub> logger, IHubContext<MeetingHub, IPeer> hubContext, BadDisconnectSocketService badDisconnectSocketService, Scheduler scheduler)
        {
            _logger = logger;
            _hubContext = hubContext;
            _scheduler = scheduler;
            _badDisconnectSocketService = badDisconnectSocketService;
        }

        public override async Task OnConnectedAsync()
        {
            await LeaveAsync();
            _badDisconnectSocketService.CacheContext(Context);
            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception exception)
        {
            await LeaveAsync();
            await base.OnDisconnectedAsync(exception);
        }

        #region Private

        private async Task LeaveAsync()
        {
            var leaveResult = await _scheduler.LeaveAsync(UserId);
            if (leaveResult != null)
            {
                // Message: peerLeave
                SendNotification(leaveResult.OtherPeerIds, "peerLeave", new { PeerId = leaveResult.SelfPeer.PeerId });
                _badDisconnectSocketService.DisconnectClient(leaveResult.SelfPeer.ConnectionId);
            }
        }

        #endregion Private
    }

    public partial class MeetingHub
    {
        /// <summary>
        /// Get RTP capabilities of router.
        /// </summary>
        /// <returns></returns>
        public MeetingMessage<RtpCapabilities> GetRouterRtpCapabilities()
        {
            var rtpCapabilities = _scheduler.DefaultRtpCapabilities;
            return new MeetingMessage<RtpCapabilities> { Code = 200, Message = "GetRouterRtpCapabilities 成功", Data = rtpCapabilities };
        }

        /// <summary>
        /// Join meeting.
        /// </summary>
        /// <param name="joinRequest"></param>
        /// <returns></returns>
        public async Task<MeetingMessage> Join(JoinRequest joinRequest)
        {
            if (!await _scheduler.JoinAsync(UserId, ConnectionId, joinRequest))
            {
                return new MeetingMessage { Code = 400, Message = "Join 失败" };
            }

            return new MeetingMessage { Code = 200, Message = "Join 成功" };
        }

        /// <summary>
        /// Set peer's appData.
        /// </summary>
        /// <param name="setPeerAppDataRequest"></param>
        /// <returns></returns>
        public async Task<MeetingMessage> SetPeerAppData(SetPeerAppDataRequest setPeerAppDataRequest)
        {
            var peerPeerAppDataResult = await _scheduler.SetPeerAppDataAsync(UserId, ConnectionId, setPeerAppDataRequest);

            // Message: peerPeerAppDataChanged
            SendNotification(peerPeerAppDataResult.OtherPeerIds, "peerPeerAppDataChanged", new
            {
                PeerId = UserId,
                AppData = peerPeerAppDataResult.AppData,
            });

            return new MeetingMessage { Code = 200, Message = "SetRoomAppData 成功" };
        }

        /// <summary>
        /// Unset peer'ss appData.
        /// </summary>
        /// <param name="unsetPeerAppDataRequest"></param>
        /// <returns></returns>
        public async Task<MeetingMessage> UnsetPeerAppData(UnsetPeerAppDataRequest unsetPeerAppDataRequest)
        {
            var peerPeerAppDataResult = await _scheduler.UnsetPeerAppDataAsync(UserId, ConnectionId, unsetPeerAppDataRequest);

            // Message: peerPeerAppDataChanged
            SendNotification(peerPeerAppDataResult.OtherPeerIds, "peerPeerAppDataChanged", new
            {
                PeerId = UserId,
                AppData = peerPeerAppDataResult.AppData,
            });

            return new MeetingMessage { Code = 200, Message = "UnsetPeerAppData 成功" };
        }

        /// <summary>
        /// Clear peer's appData.
        /// </summary>
        /// <returns></returns>
        public async Task<MeetingMessage> ClearPeerAppData()
        {
            var peerPeerAppDataResult = await _scheduler.ClearPeerAppDataAsync(UserId, ConnectionId);

            // Message: peerPeerAppDataChanged
            SendNotification(peerPeerAppDataResult.OtherPeerIds, "peerPeerAppDataChanged", new
            {
                PeerId = UserId,
                AppData = peerPeerAppDataResult.AppData,
            });

            return new MeetingMessage { Code = 200, Message = "ClearPeerAppData 成功" };
        }

        /// <summary>
        /// Create WebRTC transport.
        /// </summary>
        /// <param name="createWebRtcTransportRequest"></param>
        /// <returns></returns>
        public async Task<MeetingMessage<CreateWebRtcTransportResult>> CreateWebRtcTransport(CreateWebRtcTransportRequest createWebRtcTransportRequest)
        {
            var transport = await _scheduler.CreateWebRtcTransportAsync(UserId, ConnectionId, createWebRtcTransportRequest);
            transport.On("sctpstatechange", sctpState =>
            {
                _logger.LogDebug($"WebRtcTransport \"sctpstatechange\" event [sctpState:{sctpState}]");
                return Task.CompletedTask;
            });

            transport.On("dtlsstatechange", value =>
            {
                var dtlsState = (DtlsState)value!;
                if (dtlsState == DtlsState.Failed || dtlsState == DtlsState.Closed)
                {
                    _logger.LogWarning($"WebRtcTransport dtlsstatechange event [dtlsState:{value}]");
                }
                return Task.CompletedTask;
            });

            // NOTE: For testing.
            //await transport.EnableTraceEventAsync(new[] { TransportTraceEventType.Probation, TransportTraceEventType.BWE });
            //await transport.EnableTraceEventAsync(new[] { TransportTraceEventType.BWE });

            var peerId = UserId;
            transport.On("trace", trace =>
            {
                var traceData = (TransportTraceEventData)trace!;
                _logger.LogDebug($"transport \"trace\" event [transportId:{transport.TransportId}, trace:{traceData.Type.GetEnumStringValue()}]");

                if (traceData.Type == TransportTraceEventType.BWE && traceData.Direction == TraceEventDirection.Out)
                {
                    // Message: downlinkBwe
                    SendNotification(peerId, "downlinkBwe", new
                    {
                        DesiredBitrate = traceData.Info["desiredBitrate"],
                        EffectiveDesiredBitrate = traceData.Info["effectiveDesiredBitrate"],
                        AvailableBitrate = traceData.Info["availableBitrate"]
                    });
                }
                return Task.CompletedTask;
            });

            return new MeetingMessage<CreateWebRtcTransportResult>
            {
                Code = 200,
                Message = $"CreateWebRtcTransport 成功({(createWebRtcTransportRequest.Producing ? "Producing" : "Consuming")})",
                Data = new CreateWebRtcTransportResult
                {
                    TransportId = transport.TransportId,
                    IceParameters = transport.IceParameters,
                    IceCandidates = transport.IceCandidates,
                    DtlsParameters = transport.DtlsParameters,
                    SctpParameters = transport.SctpParameters,
                }
            };
        }

        /// <summary>
        /// Connect WebRTC transport.
        /// </summary>
        /// <param name="connectWebRtcTransportRequest"></param>
        /// <returns></returns>
        public async Task<MeetingMessage> ConnectWebRtcTransport(ConnectWebRtcTransportRequest connectWebRtcTransportRequest)
        {
            try
            {
                if (!await _scheduler.ConnectWebRtcTransportAsync(UserId, ConnectionId, connectWebRtcTransportRequest))
                {
                    return new MeetingMessage { Code = 400, Message = $"ConnectWebRtcTransport 失败: TransportId: {connectWebRtcTransportRequest.TransportId}" };
                }
            }
            catch (Exception ex)
            {
                return new MeetingMessage { Code = 400, Message = $"ConnectWebRtcTransport 失败: TransportId: {connectWebRtcTransportRequest.TransportId}, {ex.Message}" };
            }

            return new MeetingMessage { Code = 200, Message = "ConnectWebRtcTransport 成功" };
        }

        /// <summary>
        /// Join room.
        /// </summary>
        /// <param name="joinRoomRequest"></param>
        /// <returns></returns>
        public async Task<MeetingMessage<JoinRoomResponse>> JoinRoom(JoinRoomRequest joinRoomRequest)
        {
            var joinRoomResult = await _scheduler.JoinRoomAsync(UserId, ConnectionId, joinRoomRequest);

            // 将自身的信息告知给房间内的其他人
            var otherPeerIds = joinRoomResult.Peers.Select(m => m.Peer.PeerId).Where(m => m != joinRoomResult.SelfPeer.Peer.PeerId).ToArray();
            // Message: peerJoinRoom
            SendNotification(otherPeerIds, "peerJoinRoom", joinRoomResult.SelfPeer);

            var data = new JoinRoomResponse
            {
                RoomId = joinRoomRequest.RoomId,
                Peers = joinRoomResult.Peers,
            };
            return new MeetingMessage<JoinRoomResponse> { Code = 200, Message = "JoinRoom 成功", Data = data };
        }

        /// <summary>
        /// Leave room.
        /// </summary>
        /// <param name="roomId"></param>
        /// <returns></returns>
        public async Task<MeetingMessage> LeaveRoom(string roomId)
        {
            var leaveRoomResult = await _scheduler.LeaveRoomAsync(UserId, ConnectionId, roomId);

            // Message: peerLeaveRoom
            SendNotification(leaveRoomResult.OtherPeerIds, "peerLeaveRoom", new
            {
                RoomId = roomId,
                PeerId = UserId
            });

            return new MeetingMessage { Code = 200, Message = "LeaveRoom 成功" };
        }

        /// <summary>
        /// Set room's appData of peer.
        /// </summary>
        /// <param name="setRoomAppDataRequest"></param>
        /// <returns></returns>
        public async Task<MeetingMessage> SetRoomAppData(SetRoomAppDataRequest setRoomAppDataRequest)
        {
            var peerRoomAppDataResult = await _scheduler.SetRoomAppDataAsync(UserId, ConnectionId, setRoomAppDataRequest);

            // Message: peerRoomAppDataChanged
            SendNotification(peerRoomAppDataResult.OtherPeerIds, "peerRoomAppDataChanged", new
            {
                RoomId = setRoomAppDataRequest.RoomId,
                PeerId = UserId,
                AppData = peerRoomAppDataResult.AppData,
            });

            return new MeetingMessage { Code = 200, Message = "SetRoomAppData 成功" };
        }

        /// <summary>
        /// Unset room's appData of peer.
        /// </summary>
        /// <param name="unsetRoomAppDataRequest"></param>
        /// <returns></returns>
        public async Task<MeetingMessage> UnsetRoomAppData(UnsetRoomAppDataRequest unsetRoomAppDataRequest)
        {
            var peerRoomAppDataResult = await _scheduler.UnsetRoomAppDataAsync(UserId, ConnectionId, unsetRoomAppDataRequest);

            // Message: peerRoomAppDataChanged
            SendNotification(peerRoomAppDataResult.OtherPeerIds, "peerRoomAppDataChanged", new
            {
                RoomId = unsetRoomAppDataRequest.RoomId,
                PeerId = UserId,
                RoomAppData = peerRoomAppDataResult.AppData,
            });

            return new MeetingMessage { Code = 200, Message = "UnsetRoomAppData 成功" };
        }

        /// <summary>
        /// Clear room's appData of peer.
        /// </summary>
        /// <param name="roomId"></param>
        /// <returns></returns>
        public async Task<MeetingMessage> ClearRoomAppData(string roomId)
        {
            var peerRoomAppDataResult = await _scheduler.ClearRoomAppDataAsync(UserId, ConnectionId, roomId);

            // Message: peerRoomAppDataChanged
            SendNotification(peerRoomAppDataResult.OtherPeerIds, "peerRoomAppDataChanged", new
            {
                RoomId = roomId,
                PeerId = UserId,
                RoomAppData = peerRoomAppDataResult.AppData,
            });

            return new MeetingMessage { Code = 200, Message = "ClearRoomAppData 成功" };
        }

        /// <summary>
        /// Pull medias.
        /// </summary>
        /// <param name="consumeRequest"></param>
        /// <returns></returns>
        public async Task<MeetingMessage> Pull(PullRequest consumeRequest)
        {
            var consumeResult = await _scheduler.PullAsync(UserId, ConnectionId, consumeRequest);
            var consumerPeer = consumeResult.ConsumePeer;
            var producerPeer = consumeResult.ProducePeer;
            var roomId = consumeRequest.RoomId;

            foreach (var producer in consumeResult.ExistsProducers)
            {
                // 本 Peer 消费其他 Peer
                CreateConsumer(consumerPeer, producerPeer, producer, roomId).ContinueWithOnFaultedHandleLog(_logger);
            }

            if (!consumeResult.ProduceSources.IsNullOrEmpty())
            {
                // Message: produceSources
                SendNotification(consumeResult.ProducePeer.PeerId, "produceSources", new
                {
                    RoomId = consumeResult.RoomId,
                    ProduceSources = consumeResult.ProduceSources
                });
            }

            return new MeetingMessage { Code = 200, Message = "Pull 成功" };
        }

        /// <summary>
        /// Produce media.
        /// </summary>
        /// <param name="produceRequest"></param>
        /// <returns></returns>
        public async Task<MeetingMessage<ProduceRespose>> Produce(ProduceRequest produceRequest)
        {
            var peerId = UserId;
            ProduceResult produceResult;
            try
            {
                produceResult = await _scheduler.ProduceAsync(peerId, ConnectionId, produceRequest);
            }
            catch (Exception ex)
            {
                return new MeetingMessage<ProduceRespose>
                {
                    Code = 400,
                    Message = $"Produce 失败:{ex.Message}",
                };
            }

            var producerPeer = produceResult.ProducerPeer;
            var producer = produceResult.Producer;

            foreach (var item in produceResult.PullPaddingConsumerPeerWithRoomIds)
            {
                var consumerPeer = item.ConsumerPeer;
                var roomId = item.RoomId;

                // 其他 Peer 消费本 Peer
                CreateConsumer(consumerPeer, producerPeer, producer, roomId).ContinueWithOnFaultedHandleLog(_logger);
            }

            // NOTE: For Testing
            //CreateConsumer(producerPeer, producerPeer, producer, "1").ContinueWithOnFaultedHandleLog(_logger);

            // Set Producer events.
            producer.On("score", score =>
            {
                var data = (ProducerScore[])score!;
                // Message: producerScore
                SendNotification(peerId, "producerScore", new { ProducerId = producer.ProducerId, Score = data });
                return Task.CompletedTask;
            });
            producer.On("videoorientationchange", videoOrientation =>
            {
                var data = (ProducerVideoOrientation)videoOrientation!;
                _logger.LogDebug($"producer.On() | Producer \"videoorientationchange\" Event [producerId:\"{producer.ProducerId}\", VideoOrientation:\"{videoOrientation}\"]");
                return Task.CompletedTask;
            });
            producer.Observer.On("close", _ =>
            {
                SendNotification(peerId, "producerClosed", new { ProducerId = producer.ProducerId });
                return Task.CompletedTask;
            });

            return new MeetingMessage<ProduceRespose>
            {
                Code = 200,
                Message = "Produce 成功",
                Data = new ProduceRespose { Id = producer.ProducerId, Source = produceRequest.Source }
            };
        }

        /// <summary>
        /// Close producer.
        /// </summary>
        /// <param name="producerId"></param>
        /// <returns></returns>
        public async Task<MeetingMessage> CloseProducer(string producerId)
        {
            if (!await _scheduler.CloseProducerAsync(UserId, ConnectionId, producerId))
            {
                return new MeetingMessage { Code = 400, Message = "CloseProducer 失败" };
            }

            return new MeetingMessage { Code = 200, Message = "CloseProducer 成功" };
        }

        /// <summary>
        /// Pause producer.
        /// </summary>
        /// <param name="producerId"></param>
        /// <returns></returns>
        public async Task<MeetingMessage> PauseProducer(string producerId)
        {
            if (!await _scheduler.PauseProducerAsync(UserId, ConnectionId, producerId))
            {
                return new MeetingMessage { Code = 400, Message = "CloseProducer 失败" };
            }

            return new MeetingMessage { Code = 200, Message = "PauseProducer 成功" };
        }

        /// <summary>
        /// Resume producer.
        /// </summary>
        /// <param name="producerId"></param>
        /// <returns></returns>
        public async Task<MeetingMessage> ResumeProducer(string producerId)
        {
            if (!await _scheduler.ResumeProducerAsync(UserId, ConnectionId, producerId))
            {
                return new MeetingMessage { Code = 400, Message = "CloseProducer 失败" };
            }

            return new MeetingMessage { Code = 200, Message = "ResumeProducer 成功" };
        }

        /// <summary>
        /// Close consumer.
        /// </summary>
        /// <param name="consumerId"></param>
        /// <returns></returns>
        public async Task<MeetingMessage> CloseConsumer(string consumerId)
        {
            if (!await _scheduler.CloseConsumerAsync(UserId, ConnectionId, consumerId))
            {
                return new MeetingMessage { Code = 400, Message = "CloseConsumer 失败" };
            }

            return new MeetingMessage { Code = 200, Message = "CloseConsumer 成功" };
        }

        /// <summary>
        /// Pause consumer.
        /// </summary>
        /// <param name="consumerId"></param>
        /// <returns></returns>
        public async Task<MeetingMessage> PauseConsumer(string consumerId)
        {
            if (!await _scheduler.PauseConsumerAsync(UserId, ConnectionId, consumerId))
            {
                return new MeetingMessage { Code = 400, Message = "PauseConsumer 失败" };
            }

            return new MeetingMessage { Code = 200, Message = "PauseConsumer 成功" };
        }

        /// <summary>
        /// Resume consumer.
        /// </summary>
        /// <param name="consumerId"></param>
        /// <returns></returns>
        public async Task<MeetingMessage> ResumeConsumer(string consumerId)
        {
            try
            {
                var consumer = await _scheduler.ResumeConsumerAsync(UserId, ConnectionId, consumerId);
                if (consumer == null)
                {
                    return new MeetingMessage { Code = 400, Message = "ResumeConsumer 失败" };
                }

                // Message: consumerScore
                SendNotification(UserId, "consumerScore", new { ConsumerId = consumer.ConsumerId, Score = consumer.Score });
            }
            catch (Exception ex)
            {
                return new MeetingMessage
                {
                    Code = 400,
                    Message = $"ResumeConsumer 失败:{ex.Message}",
                };
            }

            return new MeetingMessage { Code = 200, Message = "ResumeConsumer 成功" };
        }

        /// <summary>
        /// Set consumer's preferedLayers.
        /// </summary>
        /// <param name="setConsumerPreferedLayersRequest"></param>
        /// <returns></returns>
        public async Task<MeetingMessage> SetConsumerPreferedLayers(SetConsumerPreferedLayersRequest setConsumerPreferedLayersRequest)
        {
            if (!await _scheduler.SetConsumerPreferedLayersAsync(UserId, ConnectionId, setConsumerPreferedLayersRequest))
            {
                return new MeetingMessage { Code = 400, Message = "SetConsumerPreferedLayers 失败" };
            }

            return new MeetingMessage { Code = 200, Message = "SetConsumerPreferedLayers 成功" };
        }

        /// <summary>
        /// Set consumer's priority.
        /// </summary>
        /// <param name="setConsumerPriorityRequest"></param>
        /// <returns></returns>
        public async Task<MeetingMessage> SetConsumerPriority(SetConsumerPriorityRequest setConsumerPriorityRequest)
        {
            if (!await _scheduler.SetConsumerPriorityAsync(UserId, ConnectionId, setConsumerPriorityRequest))
            {
                return new MeetingMessage { Code = 400, Message = "SetConsumerPreferedLayers 失败" };
            }

            return new MeetingMessage { Code = 200, Message = "SetConsumerPriority 成功" };
        }

        /// <summary>
        /// Request key-frame.
        /// </summary>
        /// <param name="consumerId"></param>
        /// <returns></returns>
        public async Task<MeetingMessage> RequestConsumerKeyFrame(string consumerId)
        {
            if (!await _scheduler.RequestConsumerKeyFrameAsync(UserId, ConnectionId, consumerId))
            {
                return new MeetingMessage { Code = 400, Message = "RequestConsumerKeyFrame 失败" };
            }

            return new MeetingMessage { Code = 200, Message = "RequestConsumerKeyFrame 成功" };
        }

        /// <summary>
        /// Get transport's state.
        /// </summary>
        /// <param name="transportId"></param>
        /// <returns></returns>
        public async Task<MeetingMessage<TransportStat>> GetTransportStats(string transportId)
        {
            var data = await _scheduler.GetTransportStatsAsync(UserId, ConnectionId, transportId);
            return new MeetingMessage<TransportStat> { Code = 200, Message = "GetTransportStats 成功", Data = data };
        }

        /// <summary>
        /// Get producer's state.
        /// </summary>
        /// <param name="producerId"></param>
        /// <returns></returns>
        public async Task<MeetingMessage<ProducerStat>> GetProducerStats(string producerId)
        {
            var data = await _scheduler.GetProducerStatsAsync(UserId, ConnectionId, producerId);
            return new MeetingMessage<ProducerStat> { Code = 200, Message = "GetProducerStats 成功", Data = data };
        }

        /// <summary>
        /// Get consumer's state.
        /// </summary>
        /// <param name="consumerId"></param>
        /// <returns></returns>
        public async Task<MeetingMessage<ConsumerStat>> GetConsumerStats(string consumerId)
        {
            var data = await _scheduler.GetConsumerStatsAsync(UserId, ConnectionId, consumerId);
            return new MeetingMessage<ConsumerStat> { Code = 200, Message = "GetConsumerStats 成功", Data = data };
        }

        /// <summary>
        /// Restart ICE.
        /// </summary>
        /// <param name="transportId"></param>
        /// <returns></returns>
        public async Task<MeetingMessage<IceParameters?>> RestartIce(string transportId)
        {
            var iceParameters = await _scheduler.RestartIceAsync(UserId, ConnectionId, transportId);
            return new MeetingMessage<IceParameters?> { Code = 200, Message = "RestartIce 成功", Data = iceParameters };
        }

        /// <summary>
        /// Send message to other peers in rooms.
        /// </summary>
        /// <param name="sendMessageRequest"></param>
        /// <returns></returns>
        public async Task<MeetingMessage> SendMessage(SendMessageRequest sendMessageRequest)
        {
            string[] otherPeerIds;
            if (sendMessageRequest.RoomId.IsNullOrWhiteSpace())
            {
                otherPeerIds = await _scheduler.GetOtherPeerIdsAsync(UserId, ConnectionId);
            }
            else
            {
                otherPeerIds = await _scheduler.GetOtherPeerIdsInRoomAsync(UserId, ConnectionId, sendMessageRequest.RoomId!);
            }

            // Message: newMessage
            SendNotification(otherPeerIds, "newMessage", new
            {
                RoomId = sendMessageRequest.RoomId,
                Message = sendMessageRequest.Message,
            });

            return new MeetingMessage { Code = 200, Message = "RestartIce 成功" };
        }

        #region Private Methods

        private async Task CreateConsumer(Peer consumerPeer, Peer producerPeer, Producer producer, string roomId)
        {
            _logger.LogDebug($"CreateConsumer() | [ConsumerPeer:\"{consumerPeer.PeerId}\", ProducerPeer:\"{producerPeer.PeerId}\", Producer:\"{producer.ProducerId}\"]");

            // Create the Consumer in paused mode.
            Consumer consumer;

            try
            {
                consumer = await _scheduler.ConsumeAsync(producerPeer.PeerId, consumerPeer.PeerId, producer.ProducerId, roomId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "CreateConsumer()");
                return;
            }

            consumer.On("score", (score) =>
            {
                var data = (ConsumerScore)score!;
                // Message: consumerScore
                SendNotification(consumerPeer.PeerId, "consumerScore", new { ConsumerId = consumer.ConsumerId, Score = data });
                return Task.CompletedTask;
            });

            // Set Consumer events.
            consumer.On("transportclose", _ =>
            {
                return Task.CompletedTask;
            });

            consumer.On("producerclose", _ =>
            {
                // Message: consumerClosed
                SendNotification(consumerPeer.PeerId, "consumerClosed", new { ConsumerId = consumer.ConsumerId });
                return Task.CompletedTask;
            });

            consumer.On("producerpause", _ =>
            {
                // Message: consumerPaused
                SendNotification(consumerPeer.PeerId, "consumerPaused", new { ConsumerId = consumer.ConsumerId });
                return Task.CompletedTask;
            });

            consumer.On("producerresume", _ =>
            {
                // Message: consumerResumed
                SendNotification(consumerPeer.PeerId, "consumerResumed", new { ConsumerId = consumer.ConsumerId });
                return Task.CompletedTask;
            });

            consumer.On("layerschange", layers =>
            {
                var data = (ConsumerLayers?)layers;

                // Message: consumerLayersChanged
                SendNotification(consumerPeer.PeerId, "consumerLayersChanged", new { ConsumerId = consumer.ConsumerId });
                return Task.CompletedTask;
            });

            // NOTE: For testing.
            // await consumer.enableTraceEvent([ 'rtp', 'keyframe', 'nack', 'pli', 'fir' ]);
            // await consumer.enableTraceEvent([ 'pli', 'fir' ]);
            // await consumer.enableTraceEvent([ 'keyframe' ]);

            consumer.On("trace", trace =>
            {
                _logger.LogDebug($"consumer \"trace\" event [producerId:{consumer.ConsumerId}, trace:{trace}]");
                return Task.CompletedTask;
            });

            // Send a request to the remote Peer with Consumer parameters.
            // Message: newConsumer

            SendNotification(consumerPeer.PeerId, "newConsumer", new ConsumeInfo
            {
                RoomId = roomId,
                ProducerPeerId = producerPeer.PeerId,
                Kind = consumer.Kind,
                ProducerId = producer.ProducerId,
                ConsumerId = consumer.ConsumerId,
                RtpParameters = consumer.RtpParameters,
                Type = consumer.Type,
                ProducerAppData = producer.AppData,
                ProducerPaused = consumer.ProducerPaused,
            });
        }

        private void SendNotification(string peerId, string type, object data)
        {
            // For Testing
            if (type == "consumerLayersChanged" || type == "consumerScore" || type == "producerScore") return;
            var client = _hubContext.Clients.User(peerId);
            client.Notify(new MeetingNotification
            {
                Type = type,
                Data = data
            }).ContinueWithOnFaultedHandleLog(_logger);
        }

        private void SendNotification(IReadOnlyList<string> peerIds, string type, object data)
        {
            var client = _hubContext.Clients.Users(peerIds);
            client.Notify(new MeetingNotification
            {
                Type = type,
                Data = data
            }).ContinueWithOnFaultedHandleLog(_logger);
        }

        #endregion Private Methods
    }
}
