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
            var peerLeaveResult = _scheduler.Leave(UserId);
            foreach (var otherPeer in peerLeaveResult.OtherPeerRooms)
            {
                // Message: peerLeaveRoom
                var client = _hubContext.Clients.User(otherPeer.Peer.PeerId);
                client.ReceiveMessage(new MeetingMessage
                {
                    Code = 200,
                    InternalCode = "peerLeaveRoom",
                    Message = "peerLeaveRoom",
                    Data = new
                    {
                        RoomId = otherPeer.Room.RoomId,
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
            if (!_scheduler.Join(UserId, joinRequest))
            {
                return new MeetingMessage { Code = 400, Message = "Join 失败" };
            }

            return new MeetingMessage { Code = 200, Message = "Join 成功" };
        }

        public async Task<MeetingMessage> CreateWebRtcTransport(CreateWebRtcTransportRequest createWebRtcTransportRequest)
        {
            var transport = await _scheduler.CreateWebRtcTransportAsync(UserId, createWebRtcTransportRequest);
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
            if (!await _scheduler.ConnectWebRtcTransportAsync(UserId, connectWebRtcTransportRequest))
            {
                return new MeetingMessage { Code = 400, Message = "ConnectWebRtcTransport 失败" };
            }

            return new MeetingMessage { Code = 200, Message = "ConnectWebRtcTransport 成功" };
        }

        public async Task<MeetingMessage> JoinRoom(JoinRoomRequest joinRoomRequest)
        {
            var joinRoomResult = await _scheduler.JoinRoomAsync(UserId, joinRoomRequest);

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
            var leaveRoomResult = _scheduler.LeaveRoom(UserId, leaveRoomRequest.RoomId);

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
            var consumeResult = _scheduler.Consume(UserId, consumeRequest);

            foreach (var existsProducer in consumeResult.ExistsProducers)
            {
                // 本 Peer 消费其他 Peer
                CreateConsumer(existsProducer.Peer, consumeResult.Peer, existsProducer.Producer, consumeRequest.RoomId).ContinueWithOnFaultedHandleLog(_logger);
            }

            // Message: produceSources
            var client = _hubContext.Clients.User(consumeResult.Peer.PeerId);
            client.ReceiveMessage(new MeetingMessage
            {
                Code = 200,
                InternalCode = "produceSources",
                Message = "produceSources",
                Data = new
                {
                    RoomId = consumeResult.RoomId,
                    ProduceSources = consumeResult.ProduceSources
                }
            }).ContinueWithOnFaultedHandleLog(_logger);

            return new MeetingMessage { Code = 200, Message = "Consume 成功" };
        }

        public async Task<MeetingMessage> Produce(ProduceRequest produceRequest)
        {
            var produceResult = await _scheduler.ProduceAsync(UserId, produceRequest);
            var producer = produceResult.Producer;

            foreach (var item in produceResult.PeerRoomIds)
            {
                // 其他 Peer 消费本 Peer
                CreateConsumer(item.Peer, produceResult.Peer, produceResult.Producer, item.RoomId).ContinueWithOnFaultedHandleLog(_logger);
            }

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
                Data = new { Id = producer.ProducerId }
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
            if (!await _scheduler.ResumeConsumerAsync(UserId, consumerId))
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

            // Create the Consumer in paused mode.
            Consumer consumer;

            try
            {
                consumer = await _scheduler.ConsumeAsync(consumerPeer.PeerId, producer, roomId);
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

            // Now that we got the positive response from the remote endpoint, resume
            // the Consumer so the remote endpoint will receive the a first RTP packet
            // of this new stream once its PeerConnection is already ready to process 
            // and associate it.
            await _scheduler.ResumeConsumerAsync(UserId, newConsumerReturnRequest.ConsumerId);

            return new MeetingMessage { Code = 200, Message = "NewConsumerReturn 成功" };
        }

        #endregion
    }
}
