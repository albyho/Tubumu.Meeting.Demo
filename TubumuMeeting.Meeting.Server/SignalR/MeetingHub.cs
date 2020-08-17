using System;
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
        private readonly Scheduler _scheduler;

        private string UserId => Context.User.Identity.Name;

        public MeetingHub(ILogger<MeetingHub> logger, IHubContext<MeetingHub, IPeer> hubContext, Scheduler scheduler)
        {
            _logger = logger;
            _hubContext = hubContext;
            _scheduler = scheduler;
        }

        public override async Task OnConnectedAsync()
        {
            await LeaveAsync();
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
                foreach (var otherPeerId in leaveResult.OtherPeerIds)
                {
                    // Message: peerLeave
                    SendMessage(otherPeerId, "peerLeave", new { PeerId = leaveResult.SelfPeer.PeerId });
                }
            }
        }

        #endregion
    }

    public partial class MeetingHub
    {
        public MeetingMessage GetRouterRtpCapabilities()
        {
            var rtpCapabilities = _scheduler.DefaultRtpCapabilities;
            return new MeetingMessage { Code = 200, Message = "GetRouterRtpCapabilities 成功", Data = rtpCapabilities };
        }

        public async Task<MeetingMessage> Join(JoinRequest joinRequest)
        {
            if (!await _scheduler.JoinAsync(UserId, joinRequest))
            {
                return new MeetingMessage { Code = 400, Message = "Join 失败" };
            }

            return new MeetingMessage { Code = 200, Message = "Join 成功" };
        }

        public async Task<MeetingMessage> SetPeerAppData(SetPeerAppDataRequest setPeerAppDataRequest)
        {
            var peerPeerAppDataResult = await _scheduler.SetPeerAppDataAsync(UserId, setPeerAppDataRequest);

            foreach (var otherPeerId in peerPeerAppDataResult.OtherPeerIds)
            {
                // Message: peerPeerAppDataChanged
                SendMessage(otherPeerId, "peerPeerAppDataChanged", new
                {
                    PeerId = UserId,
                    RoomAppData = peerPeerAppDataResult.SelfPeer.AppData,
                });
            }

            return new MeetingMessage { Code = 200, Message = "SetRoomAppData 成功" };
        }

        public async Task<MeetingMessage> UnsetPeerAppData(UnsetPeerAppDataRequest unsetPeerAppDataRequest)
        {
            var peerPeerAppDataResult = await _scheduler.UnsetPeerAppDataAsync(UserId, unsetPeerAppDataRequest);

            foreach (var otherPeerId in peerPeerAppDataResult.OtherPeerIds)
            {
                // Message: peerPeerAppDataChanged
                SendMessage(otherPeerId, "peerPeerAppDataChanged", new
                {
                    PeerId = UserId,
                    PeerAppData = peerPeerAppDataResult.SelfPeer.AppData,
                });
            }

            return new MeetingMessage { Code = 200, Message = "UnsetPeerAppData 成功" };
        }

        public async Task<MeetingMessage> ClearPeerAppData()
        {
            var peerPeerAppDataResult = await _scheduler.ClearPeerAppDataAsync(UserId);

            foreach (var otherPeerId in peerPeerAppDataResult.OtherPeerIds)
            {
                // Message: peerPeerAppDataChanged
                SendMessage(otherPeerId, "peerPeerAppDataChanged", new
                {
                    PeerId = UserId,
                    RoomAppData = peerPeerAppDataResult.SelfPeer.AppData,
                });
            }

            return new MeetingMessage { Code = 200, Message = "ClearPeerAppData 成功" };
        }

        public async Task<MeetingMessage> CreateWebRtcTransport(CreateWebRtcTransportRequest createWebRtcTransportRequest)
        {
            var transport = await _scheduler.CreateWebRtcTransportAsync(UserId, createWebRtcTransportRequest);
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
                    SendMessage(peerId, "downlinkBwe", new
                    {
                        DesiredBitrate = traceData.Info["desiredBitrate"],
                        EffectiveDesiredBitrate = traceData.Info["effectiveDesiredBitrate"],
                        AvailableBitrate = traceData.Info["availableBitrate"]
                    });
                }
                return Task.CompletedTask;
            });

            return new MeetingMessage
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

        public async Task<MeetingMessage> ConnectWebRtcTransport(ConnectWebRtcTransportRequest connectWebRtcTransportRequest)
        {
            try
            {
                if (!await _scheduler.ConnectWebRtcTransportAsync(UserId, connectWebRtcTransportRequest))
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

        public async Task<MeetingMessage> JoinRoom(JoinRoomRequest joinRoomRequest)
        {
            var joinRoomResult = await _scheduler.JoinRoomAsync(UserId, joinRoomRequest);

            foreach (var peer in joinRoomResult.Peers)
            {
                // 将自身的信息告知给房间内的其他人
                if (peer.Peer.PeerId != joinRoomResult.SelfPeer.Peer.PeerId)
                {
                    // Message: peerJoinRoom
                    SendMessage(peer.Peer.PeerId, "peerJoinRoom", joinRoomResult.SelfPeer);
                }
            }

            var data = new
            {
                RoomId = joinRoomRequest.RoomId,
                Peers = joinRoomResult.Peers,
            };
            return new MeetingMessage { Code = 200, Message = "JoinRoom 成功", Data = data };
        }

        public async Task<MeetingMessage> LeaveRoom(string roomId)
        {
            var leaveRoomResult = await _scheduler.LeaveRoomAsync(UserId, roomId);

            foreach (var otherPeerId in leaveRoomResult.OtherPeerIds)
            {
                // Message: peerLeaveRoom
                SendMessage(otherPeerId, "peerLeaveRoom", new
                {
                    RoomId = roomId,
                    PeerId = UserId
                });
            }

            return new MeetingMessage { Code = 200, Message = "LeaveRoom 成功" };
        }

        public async Task<MeetingMessage> SetRoomAppData(SetRoomAppDataRequest setRoomAppDataRequest)
        {
            var peerRoomAppDataResult = await _scheduler.SetRoomAppDataAsync(UserId, setRoomAppDataRequest);

            foreach (var otherPeerId in peerRoomAppDataResult.OtherPeerIds)
            {
                // Message: peerRoomAppDataChanged
                SendMessage(otherPeerId, "peerRoomAppDataChanged", new
                {
                    RoomId = setRoomAppDataRequest.RoomId,
                    PeerId = UserId,
                    RoomAppData = peerRoomAppDataResult.RoomAppData,
                });
            }

            return new MeetingMessage { Code = 200, Message = "SetRoomAppData 成功" };
        }

        public async Task<MeetingMessage> UnsetRoomAppData(UnsetRoomAppDataRequest unsetRoomAppDataRequest)
        {
            var peerRoomAppDataResult = await _scheduler.UnsetRoomAppDataAsync(UserId, unsetRoomAppDataRequest);

            foreach (var otherPeerId in peerRoomAppDataResult.OtherPeerIds)
            {
                // Message: peerRoomAppDataChanged
                SendMessage(otherPeerId, "peerRoomAppDataChanged", new
                {
                    RoomId = unsetRoomAppDataRequest.RoomId,
                    PeerId = UserId,
                    RoomAppData = peerRoomAppDataResult.RoomAppData,
                });
            }

            return new MeetingMessage { Code = 200, Message = "UnsetRoomAppData 成功" };
        }

        public async Task<MeetingMessage> ClearRoomAppData(string roomId)
        {
            var peerRoomAppDataResult = await _scheduler.ClearRoomAppDataAsync(UserId, roomId);

            foreach (var otherPeerId in peerRoomAppDataResult.OtherPeerIds)
            {
                // Message: peerRoomAppDataChanged
                SendMessage(otherPeerId, "peerRoomAppDataChanged", new
                {
                    RoomId = roomId,
                    PeerId = UserId,
                    RoomAppData = peerRoomAppDataResult.RoomAppData,
                });
            }

            return new MeetingMessage { Code = 200, Message = "ClearRoomAppData 成功" };
        }

        public async Task<MeetingMessage> Pull(PullRequest consumeRequest)
        {
            var consumeResult = await _scheduler.PullAsync(UserId, consumeRequest);
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
                SendMessage(consumeResult.ProducePeer.PeerId, "produceSources", new
                {
                    RoomId = consumeResult.RoomId,
                    ProduceSources = consumeResult.ProduceSources
                });
            }

            return new MeetingMessage { Code = 200, Message = "Pull 成功" };
        }

        public async Task<MeetingMessage> Produce(ProduceRequest produceRequest)
        {
            var peerId = UserId;
            ProduceResult produceResult;
            try
            {
                produceResult = await _scheduler.ProduceAsync(peerId, produceRequest);
            }
            catch (Exception ex)
            {
                return new MeetingMessage
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
                SendMessage(peerId, "producerScore", new { ProducerId = producer.ProducerId, Score = data });
                return Task.CompletedTask;
            });
            producer.On("videoorientationchange", videoOrientation =>
            {
                var data = (ProducerVideoOrientation)videoOrientation!;
                _logger.LogDebug($"producer.On() | producer \"videoorientationchange\" event [producerId:\"{producer.ProducerId}\", videoOrientation:\"{videoOrientation}\"]");
                return Task.CompletedTask;
            });
            producer.Observer.On("close", _ =>
            {
                SendMessage(peerId, "producerClosed", new { ProducerId = producer.ProducerId });
                return Task.CompletedTask;
            });

            string? source = null;
            if (produceRequest.AppData.TryGetValue("source", out var sourceObj))
            {
                source = sourceObj.ToString();
            }

            return new MeetingMessage
            {
                Code = 200,
                Message = "Produce 成功",
                Data = new { Id = producer.ProducerId, Source = source }
            };
        }

        public async Task<MeetingMessage> CloseProducer(string producerId)
        {
            if (!await _scheduler.CloseProducerAsync(UserId, producerId))
            {
                return new MeetingMessage { Code = 400, Message = "CloseProducer 失败" };
            }

            return new MeetingMessage { Code = 200, Message = "CloseProducer 成功" };
        }

        public async Task<MeetingMessage> PauseProducer(string producerId)
        {
            if (!await _scheduler.PauseProducerAsync(UserId, producerId))
            {
                return new MeetingMessage { Code = 400, Message = "CloseProducer 失败" };
            }

            return new MeetingMessage { Code = 200, Message = "PauseProducer 成功" };
        }

        public async Task<MeetingMessage> ResumeProducer(string producerId)
        {
            if (!await _scheduler.ResumeProducerAsync(UserId, producerId))
            {
                return new MeetingMessage { Code = 400, Message = "CloseProducer 失败" };
            }

            return new MeetingMessage { Code = 200, Message = "ResumeProducer 成功" };
        }

        public async Task<MeetingMessage> CloseConsumer(string consumerId)
        {
            if (!await _scheduler.CloseConsumerAsync(UserId, consumerId))
            {
                return new MeetingMessage { Code = 400, Message = "CloseConsumer 失败" };
            }

            return new MeetingMessage { Code = 200, Message = "CloseConsumer 成功" };
        }

        public async Task<MeetingMessage> PauseConsumer(string consumerId)
        {
            if (!await _scheduler.PauseConsumerAsync(UserId, consumerId))
            {
                return new MeetingMessage { Code = 400, Message = "PauseConsumer 失败" };
            }

            return new MeetingMessage { Code = 200, Message = "PauseConsumer 成功" };
        }

        public async Task<MeetingMessage> ResumeConsumer(string consumerId)
        {
            try
            {
                var consumer = await _scheduler.ResumeConsumerAsync(UserId, consumerId);
                if (consumer == null)
                {
                    return new MeetingMessage { Code = 400, Message = "ResumeConsumer 失败" };
                }

                // Message: consumerScore
                SendMessage(UserId, "consumerScore", new { ConsumerId = consumer.ConsumerId, Score = consumer.Score });
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

        public async Task<MeetingMessage> SetConsumerPreferedLayers(SetConsumerPreferedLayersRequest setConsumerPreferedLayersRequest)
        {
            if (!await _scheduler.SetConsumerPreferedLayersAsync(UserId, setConsumerPreferedLayersRequest))
            {
                return new MeetingMessage { Code = 400, Message = "SetConsumerPreferedLayers 失败" };
            }

            return new MeetingMessage { Code = 200, Message = "SetConsumerPreferedLayers 成功" };
        }

        public async Task<MeetingMessage> SetConsumerPriority(SetConsumerPriorityRequest setConsumerPriorityRequest)
        {
            if (!await _scheduler.SetConsumerPriorityAsync(UserId, setConsumerPriorityRequest))
            {
                return new MeetingMessage { Code = 400, Message = "SetConsumerPreferedLayers 失败" };
            }

            return new MeetingMessage { Code = 200, Message = "SetConsumerPriority 成功" };
        }

        public async Task<MeetingMessage> RequestConsumerKeyFrame(string consumerId)
        {
            if (!await _scheduler.RequestConsumerKeyFrameAsync(UserId, consumerId))
            {
                return new MeetingMessage { Code = 400, Message = "RequestConsumerKeyFrame 失败" };
            }

            return new MeetingMessage { Code = 200, Message = "RequestConsumerKeyFrame 成功" };
        }

        public async Task<MeetingMessage> GetTransportStats(string transportId)
        {
            var data = await _scheduler.GetTransportStatsAsync(UserId, transportId);
            return new MeetingMessage { Code = 200, Message = "GetTransportStats 成功", Data = data };
        }

        public async Task<MeetingMessage> GetProducerStats(string producerId)
        {
            var data = await _scheduler.GetProducerStatsAsync(UserId, producerId);
            return new MeetingMessage { Code = 200, Message = "GetProducerStats 成功", Data = data };
        }

        public async Task<MeetingMessage> GetConsumerStats(string consumerId)
        {
            var data = await _scheduler.GetConsumerStatsAsync(UserId, consumerId);
            return new MeetingMessage { Code = 200, Message = "GetConsumerStats 成功", Data = data };
        }

        public async Task<MeetingMessage> RestartIce(string transportId)
        {
            var iceParameters = await _scheduler.RestartIceAsync(UserId, transportId);
            return new MeetingMessage { Code = 200, Message = "RestartIce 成功", Data = iceParameters };
        }

        #region Private Methods

        private async Task CreateConsumer(Peer consumerPeer, Peer producerPeer, Producer producer, string roomId)
        {
            _logger.LogDebug($"CreateConsumer() | [consumerPeer:\"{consumerPeer.PeerId}\", producerPeer:\"{producerPeer.PeerId}\", producer:\"{producer.ProducerId}\"]");

            // Create the Consumer in paused mode.
            Consumer consumer;

            try
            {
                consumer = await _scheduler.ConsumeAsync(producerPeer.PeerId, consumerPeer.PeerId, producer.ProducerId, roomId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"CreateConsumer() | [error:\"{ex}\"]");
                return;
            }

            consumer.On("score", (score) =>
            {
                var data = (ConsumerScore)score!;
                // Message: consumerScore
                SendMessage(consumerPeer.PeerId, "consumerScore", new { ConsumerId = consumer.ConsumerId, Score = data });
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
                SendMessage(consumerPeer.PeerId, "consumerClosed", new { ConsumerId = consumer.ConsumerId });
                return Task.CompletedTask;
            });

            consumer.On("producerpause", _ =>
            {
                // Message: consumerPaused
                SendMessage(consumerPeer.PeerId, "consumerPaused", new { ConsumerId = consumer.ConsumerId });
                return Task.CompletedTask;
            });

            consumer.On("producerresume", _ =>
            {
                // Message: consumerResumed
                SendMessage(consumerPeer.PeerId, "consumerResumed", new { ConsumerId = consumer.ConsumerId });
                return Task.CompletedTask;
            });

            consumer.On("layerschange", layers =>
            {
                var data = (ConsumerLayers?)layers;

                // Message: consumerLayersChanged
                SendMessage(consumerPeer.PeerId, "consumerLayersChanged", new { ConsumerId = consumer.ConsumerId });
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

            SendMessage(consumerPeer.PeerId, "newConsumer", new ConsumeInfo
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

        private void SendMessage(string peerId, string type, object data)
        {
            if (type == "consumerLayersChanged" || type == "consumerScore" || type == "producerScore") return;
            var client = _hubContext.Clients.User(peerId);
            client.Notify(new MeetingNotification
            {
                Type = type,
                Data = data
            }).ContinueWithOnFaultedHandleLog(_logger);
        }

        #endregion
    }
}
