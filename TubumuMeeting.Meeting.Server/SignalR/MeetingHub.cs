using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Tubumu.Core.Extensions;
using TubumuMeeting.Mediasoup;
using TubumuMeeting.Mediasoup.Extensions;

namespace TubumuMeeting.Meeting.Server
{
    /// <summary>
    /// MeetingMessage
    /// </summary>
    public class MeetingMessage
    {
        public int Code { get; set; } = 200;

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string? InternalCode { get; set; }

        public string Message { get; set; } = "Success";

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public object? Data { get; set; }

        public static string Stringify(int code, string message, string? data = null)
        {
            if (data == null)
            {
                return $"{{\"code\":{code},\"message\":\"{message}\"}}";
            }
            return $"{{\"code\":{code},\"message\":\"{message}\",\"data\":{data}}}";
        }
    }

    public interface IPeer
    {
        Task NewConsumer(MeetingMessage message);

        Task ReceiveMessage(MeetingMessage message);
    }

    [Authorize]
    public partial class MeetingHub : Hub<IPeer>
    {
        private readonly ILogger<MeetingHub> _logger;
        private readonly IHubContext<MeetingHub, IPeer> _hubContext;
        private readonly Scheduler _scheduler;

        public MeetingHub(ILogger<MeetingHub> logger, IHubContext<MeetingHub, IPeer> hubContext, Scheduler scheduler)
        {
            _logger = logger;
            _hubContext = hubContext;
            _scheduler = scheduler;
        }

        public override Task OnConnectedAsync()
        {
            Leave();

            return base.OnConnectedAsync();
        }

        public override Task OnDisconnectedAsync(Exception exception)
        {
            Leave();

            return base.OnDisconnectedAsync(exception);
        }

        #region Private

        private void Leave()
        {
            var peerLeaveResult = _scheduler.PeerLeave(UserId);
            foreach (var otherPeer in peerLeaveResult.OtherPeers)
            {
                // Message: peerLeaveRoom
                var client = _hubContext.Clients.User(otherPeer.PeerId.PeerId);
                client.ReceiveMessage(new MeetingMessage
                {
                    Code = 200,
                    InternalCode = "peerLeaveRoom",
                    Message = "peerLeaveRoom",
                    Data = new
                    {
                        RoomId = otherPeer.RoomId.RoomId,
                        PeerId = peerLeaveResult.Peer.PeerId
                    }
                }).ContinueWithOnFaultedHandleLog(_logger);
            }
        }

        private string UserId => Context.User.Identity.Name;

        #endregion
    }

    public partial class MeetingHub
    {
        public MeetingMessage GetRouterRtpCapabilities()
        {
            var rtpCapabilities = _scheduler.DefaultRtpCapabilities;
            return new MeetingMessage { Code = 200, Message = "GetRouterRtpCapabilities 成功", Data = rtpCapabilities };
        }

        public MeetingMessage Join(JoinRequest joinRequest)
        {
            if (!_scheduler.PeerJoin(UserId, joinRequest))
            {
                return new MeetingMessage { Code = 400, Message = "Join 失败" };
            }

            return new MeetingMessage { Code = 200, Message = "Join 成功" };
        }

        public async Task<MeetingMessage> CreateWebRtcTransport(CreateWebRtcTransportRequest createWebRtcTransportRequest)
        {
            var transport = await _scheduler.PeerCreateWebRtcTransportAsync(UserId, createWebRtcTransportRequest);
            transport.On("sctpstatechange", sctpState =>
            {
                _logger.LogDebug($"WebRtcTransport \"sctpstatechange\" event [sctpState:{sctpState}]");
            });

            transport.On("dtlsstatechange", value =>
            {
                var dtlsState = (DtlsState)value!;
                if (dtlsState == DtlsState.Failed || dtlsState == DtlsState.Closed)
                {
                    _logger.LogWarning($"WebRtcTransport dtlsstatechange event [dtlsState:{value}]");
                }
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
                    var client = _hubContext.Clients.User(peerId);
                    client.ReceiveMessage(new MeetingMessage
                    {
                        Code = 200,
                        InternalCode = "downlinkBwe",
                        Message = "downlinkBwe",
                        Data = new
                        {
                            DesiredBitrate = traceData.Info["desiredBitrate"],
                            EffectiveDesiredBitrate = traceData.Info["effectiveDesiredBitrate"],
                            AvailableBitrate = traceData.Info["availableBitrate"]
                        }
                    }).ContinueWithOnFaultedHandleLog(_logger);
                }
            });

            return new MeetingMessage
            {
                Code = 200,
                Message = "CreateWebRtcTransport 成功",
                Data = new CreateWebRtcTransportResult
                {
                    Id = transport.TransportId,
                    IceParameters = transport.IceParameters,
                    IceCandidates = transport.IceCandidates,
                    DtlsParameters = transport.DtlsParameters,
                    SctpParameters = transport.SctpParameters,
                }
            };
        }

        public async Task<MeetingMessage> ConnectWebRtcTransport(ConnectWebRtcTransportRequest connectWebRtcTransportRequest)
        {
            if (!await _scheduler.PeerConnectWebRtcTransportAsync(UserId, connectWebRtcTransportRequest))
            {
                return new MeetingMessage { Code = 400, Message = "ConnectWebRtcTransport 失败" };
            }

            return new MeetingMessage { Code = 200, Message = "ConnectWebRtcTransport 成功" };
        }

        public async Task<MeetingMessage> JoinRoom(JoinRoomRequest joinRoomRequest)
        {
            var joinRoomResult = await _scheduler.PeerJoinRoomAsync(UserId, joinRoomRequest);

            foreach (var otherPeer in joinRoomResult.OtherPeers)
            {
                // Notify the new Peer to all other Peers.
                // Message: peerJoinRoom
                var client = _hubContext.Clients.User(otherPeer.PeerId);
                client.ReceiveMessage(new MeetingMessage
                {
                    Code = 200,
                    InternalCode = "peerJoinRoom",
                    Message = "peerJoinRoom",
                    Data = new
                    {
                        RoomId = joinRoomRequest.RoomId,
                        PeerId = joinRoomResult.Peer.PeerId,
                        DisplayName = joinRoomResult.Peer.DisplayName,
                        Sources = joinRoomResult.Peer.Sources,
                    }
                }).ContinueWithOnFaultedHandleLog(_logger);
            }

            var otherPeers = joinRoomResult.OtherPeers.Select(m => new
            {
                RoomId = joinRoomRequest.RoomId,
                PeerId = m.PeerId,
                DisplayName = m.DisplayName,
                Sources = m.Sources,
            });
            var data = new
            {
                RoomId = joinRoomRequest.RoomId,
                OtherPeers = otherPeers,
            };
            return new MeetingMessage { Code = 200, Message = "JoinRoom 成功", Data = data };
        }

        public MeetingMessage LeaveRoom(LeaveRoomRequest leaveRoomRequest)
        {
            var leaveRoomResult = _scheduler.PeerLeaveRoom(UserId, leaveRoomRequest.RoomId);

            foreach (var otherPeer in leaveRoomResult.OtherPeers)
            {
                // Message: peerLeaveRoom
                var client = _hubContext.Clients.User(otherPeer.PeerId);
                client.ReceiveMessage(new MeetingMessage
                {
                    Code = 200,
                    InternalCode = "peerLeaveRoom",
                    Message = "peerLeaveRoom",
                    Data = new
                    {
                        RoomId = leaveRoomRequest.RoomId,
                        PeerId = UserId
                    }
                }).ContinueWithOnFaultedHandleLog(_logger);
            }

            return new MeetingMessage { Code = 200, Message = "LeaveRoom 成功" };
        }

        public MeetingMessage Consume(ConsumeRequest consumeRequest)
        {
            var consumeResult = _scheduler.PeerConsume(UserId, consumeRequest);

            foreach (var existsProducer in consumeResult.ExistsProducers)
            {
                // 本 Peer 消费其他 Peer
                CreateConsumer(existsProducer.Peer, consumeResult.Peer, existsProducer.Producer, consumeRequest.RoomId).ContinueWithOnFaultedHandleLog(_logger);
            }

            foreach(var produceSource in consumeResult.ProduceSources)
            {
                // Message: produceSource
                var client = _hubContext.Clients.User(consumeResult.Peer.PeerId);
                client.ReceiveMessage(new MeetingMessage
                {
                    Code = 200,
                    InternalCode = "produceSource",
                    Message = "produceSource",
                    Data = new
                    {
                        RoomId = consumeResult.RoomId,
                        ProduceSources = consumeResult.ProduceSources
                    }
                }).ContinueWithOnFaultedHandleLog(_logger);
            }

            return new MeetingMessage { Code = 200, Message = "Consume 成功" };
        }

        public async Task<MeetingMessage> Produce(ProduceRequest produceRequest)
        {
            var producer = await _scheduler.PeerProduceAsync(UserId, produceRequest);

            // 如果在本房间的其他 Peer 的 InterestedSources 匹配该 Producer 则消费
            //foreach (var otherPeer in room.Peers.Values.Where(m => m.PeerId != UserId && m.Rooms[roomId].InterestedSources.Any(m => m == producer.Source)))
            //{
            //    // 其他 Peer 消费本 Peer
            //    CreateConsumer(otherPeer, Peer!, producer, roomId).ContinueWithOnFaultedHandleLog(_logger);
            //}

            // Set Producer events.
            var peerId = UserId;
            producer.On("score", score =>
            {
                var data = (ProducerScore[])score!;
                // Message: producerScore
                var client = _hubContext.Clients.User(peerId);
                client.ReceiveMessage(new MeetingMessage
                {
                    Code = 200,
                    InternalCode = "producerScore",
                    Message = "producerScore",
                    Data = new { ProducerId = producer.ProducerId, Score = data }
                }).ContinueWithOnFaultedHandleLog(_logger);
            });
            producer.On("videoorientationchange", videoOrientation =>
            {
                var data = (ProducerVideoOrientation)videoOrientation!;
                _logger.LogDebug($"producer.On() | producer \"videoorientationchange\" event [producerId:\"{producer.ProducerId}\", videoOrientation:\"{videoOrientation}\"]");
            });

            return new MeetingMessage
            {
                Code = 200,
                Message = "Produce 成功",
                Data = new ProduceResult { Id = producer.ProducerId }
            };
        }

        public async Task<MeetingMessage> CloseProducer(string producerId)
        {
            if (!await _scheduler.PeerCloseProducerAsync(UserId, producerId))
            {
                return new MeetingMessage { Code = 400, Message = "CloseProducer 失败" };
            }

            return new MeetingMessage { Code = 200, Message = "CloseProducer 成功" };
        }

        public async Task<MeetingMessage> PauseProducer(string producerId)
        {
            if (!await _scheduler.PeerPauseProducerAsync(UserId, producerId))
            {
                return new MeetingMessage { Code = 400, Message = "CloseProducer 失败" };
            }

            return new MeetingMessage { Code = 200, Message = "PauseProducer 成功" };
        }

        public async Task<MeetingMessage> ResumeProducer(string producerId)
        {
            if (!await _scheduler.PeerResumeProducerAsync(UserId, producerId))
            {
                return new MeetingMessage { Code = 400, Message = "CloseProducer 失败" };
            }

            return new MeetingMessage { Code = 200, Message = "ResumeProducer 成功" };
        }

        public async Task<MeetingMessage> CloseConsumer(string consumerId)
        {
            if (!await _scheduler.PeerCloseConsumerAsync(UserId, consumerId))
            {
                return new MeetingMessage { Code = 400, Message = "CloseConsumer 失败" };
            }

            return new MeetingMessage { Code = 200, Message = "CloseConsumer 成功" };
        }

        public async Task<MeetingMessage> PauseConsumer(string consumerId)
        {
            if (!await _scheduler.PeerPauseConsumerAsync(UserId, consumerId))
            {
                return new MeetingMessage { Code = 400, Message = "PauseConsumer 失败" };
            }

            return new MeetingMessage { Code = 200, Message = "PauseConsumer 成功" };
        }

        public async Task<MeetingMessage> ResumeConsumer(string consumerId)
        {
            if (!await _scheduler.PeerResumeConsumerAsync(UserId, consumerId))
            {
                return new MeetingMessage { Code = 400, Message = "PauseConsumer 失败" };
            }

            return new MeetingMessage { Code = 200, Message = "PauseConsumer 成功" };
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

        #region CreateConsumer

        private async Task CreateConsumer(Peer consumerPeer, Peer producerPeer, Producer producer, string roomId)
        {
            _logger.LogDebug($"CreateConsumer() | [consumerPeer:\"{consumerPeer.PeerId}\", producerPeer:\"{producerPeer.PeerId}\", producer:\"{producer.ProducerId}\"]");

            // Optimization:
            // - Create the server-side Consumer. If video, do it paused.
            // - Tell its Peer about it and wait for its response.
            // - Upon receipt of the response, resume the server-side Consumer.
            // - If video, this will mean a single key frame requested by the
            //   server-side Consumer (when resuming it).

            // NOTE: Don't create the Consumer if the remote Peer cannot consume it.
            //if (consumerPeer.RtpCapabilities == null || !Peer!.Group.Router.CanConsume(producer.ProducerId, consumerPeer.RtpCapabilities))
            //{
            //    return;
            //}

            // Must take the Transport the remote Peer is using for consuming.
            var transport = consumerPeer.GetConsumingTransport();
            // This should not happen.
            if (transport == null)
            {
                _logger.LogWarning("CreateConsumer() | Transport for consuming not found");
                return;
            }

            // Create the Consumer in paused mode.
            Consumer consumer;

            try
            {
                consumer = await transport.ConsumeAsync(new ConsumerOptions
                {
                    ProducerId = producer.ProducerId,
                    RtpCapabilities = consumerPeer.RtpCapabilities,
                    Paused = producer.Kind == MediaKind.Video
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"CreateConsumer() | [error:\"{ex}\"]");
                return;
            }

            // Store RoomId
            consumer.RoomId = roomId;

            // Store producer source
            consumer.Source = producer.Source;

            // Store the Consumer into the consumerPeer data Object.
            consumerPeer.Consumers[consumer.ConsumerId] = consumer;

            consumer.On("score", (score) =>
            {
                var data = (ConsumerScore)score!;
                // Message: consumerScore
                var client = _hubContext.Clients.User(consumerPeer.PeerId);
                client.ReceiveMessage(new MeetingMessage
                {
                    Code = 200,
                    InternalCode = "consumerScore",
                    Message = "consumerScore",
                    Data = new { ConsumerId = consumer.ConsumerId, Score = data }
                }).ContinueWithOnFaultedHandleLog(_logger);
            });

            // Set Consumer events.
            consumer.On("transportclose", _ =>
            {
                // Remove from its map.
                consumerPeer.Consumers.Remove(consumer.ConsumerId);
            });

            consumer.On("producerclose", _ =>
            {
                // Remove from its map.
                consumerPeer.Consumers.Remove(consumer.ConsumerId);

                // Message: consumerClosed
                var client = _hubContext.Clients.User(consumerPeer.PeerId);
                client.ReceiveMessage(new MeetingMessage
                {
                    Code = 200,
                    InternalCode = "consumerClosed",
                    Message = "consumerClosed",
                    Data = new { ConsumerId = consumer.ConsumerId }
                }).ContinueWithOnFaultedHandleLog(_logger);
            });

            consumer.On("producerpause", _ =>
            {
                // Message: consumerPaused
                var client = _hubContext.Clients.User(consumerPeer.PeerId);
                client.ReceiveMessage(new MeetingMessage
                {
                    Code = 200,
                    InternalCode = "consumerPaused",
                    Message = "consumerPaused",
                    Data = new { ConsumerId = consumer.ConsumerId }
                }).ContinueWithOnFaultedHandleLog(_logger);
            });

            consumer.On("producerresume", _ =>
            {
                // Message: consumerResumed
                var client = _hubContext.Clients.User(consumerPeer.PeerId);
                client.ReceiveMessage(new MeetingMessage
                {
                    Code = 200,
                    InternalCode = "consumerResumed",
                    Message = "consumerResumed",
                    Data = new { ConsumerId = consumer.ConsumerId }
                }).ContinueWithOnFaultedHandleLog(_logger);
            });

            consumer.On("layerschange", layers =>
            {
                var data = (ConsumerLayers?)layers;

                // Message: consumerLayersChanged
                var client = _hubContext.Clients.User(consumerPeer.PeerId);
                client.ReceiveMessage(new MeetingMessage
                {
                    Code = 200,
                    InternalCode = "consumerLayersChanged",
                    Message = "consumerLayersChanged",
                    Data = new { ConsumerId = consumer.ConsumerId, SpatialLayer = data != null ? (int?)data.SpatialLayer : null, TemporalLayer = data != null ? (int?)data.TemporalLayer : null }
                }).ContinueWithOnFaultedHandleLog(_logger);
            });

            // Send a request to the remote Peer with Consumer parameters.
            try
            {
                // Message: newConsumer
                var client = _hubContext.Clients.User(consumerPeer.PeerId);
                await client.NewConsumer(new MeetingMessage
                {
                    Code = 200,
                    InternalCode = "newConsumer",
                    Message = "newConsumer",
                    Data = new
                    {
                        PeerId = producerPeer.PeerId,
                        Kind = consumer.Kind,
                        ProducerId = producer.ProducerId,
                        Id = consumer.ConsumerId,
                        RtpParameters = consumer.RtpParameters,
                        Type = consumer.Type,
                        AppData = producer.AppData,
                        ProducerPaused = consumer.ProducerPaused,
                        ConsumerPeerId = consumerPeer.PeerId, // 为了方便 NewConsumerReturn 查找 Consumer
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"CreateConsumer() | [error:\"{ex}\"]");
            }
        }

        public async Task<MeetingMessage> NewConsumerReturn(NewConsumerReturnRequest newConsumerReturnRequest)
        {
            _logger.LogDebug($"NewConsumerReturn() | [peerId:\"{newConsumerReturnRequest.PeerId}\", consumerId:\"{newConsumerReturnRequest.ConsumerId}\"]");

            if (!_scheduler.Peers.TryGetValue(newConsumerReturnRequest.PeerId, out var consumerPeer) ||
                consumerPeer.Closed ||
                !consumerPeer.Consumers.TryGetValue(newConsumerReturnRequest.ConsumerId, out var consumer) ||
                consumer.Closed)
            {
                return new MeetingMessage { Code = 400, Message = "NewConsumerReturn 失败" };
            }

            // Now that we got the positive response from the remote endpoint, resume
            // the Consumer so the remote endpoint will receive the a first RTP packet
            // of this new stream once its PeerConnection is already ready to process 
            // and associate it.
            await consumer.ResumeAsync();

            // Message: consumerScore
            var client = _hubContext.Clients.User(consumerPeer.PeerId);
            client.ReceiveMessage(new MeetingMessage
            {
                Code = 200,
                InternalCode = "consumerScore",
                Message = "consumerScore",
                Data = new
                {
                    ConsumerId = consumer.ConsumerId,
                    Score = consumer.Score,
                }
            }).ContinueWithOnFaultedHandleLog(_logger);

            return new MeetingMessage { Code = 200, Message = "NewConsumerReturn 成功" };
        }

        #endregion
    }
}
