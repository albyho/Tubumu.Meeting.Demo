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
        Task PeerHandled(MeetingMessage message);

        Task NewConsumer(MeetingMessage message);

        Task NewAskFor(MeetingMessage message);

        Task ReceiveMessage(MeetingMessage message);
    }

    [Authorize]
    public partial class MeetingHub : Hub<IPeer>
    {
        private readonly ILogger<MeetingHub> _logger;
        private readonly MediasoupOptions _mediasoupOptions;
        private readonly MeetingManager _meetingManager;
        private readonly IHubContext<MeetingHub, IPeer> _hubContext;

        public MeetingHub(ILogger<MeetingHub> logger, MeetingManager meetingManager, MediasoupOptions mediasoupOptions, IHubContext<MeetingHub, IPeer> hubContext)
        {
            _logger = logger;
            _meetingManager = meetingManager;
            _mediasoupOptions = mediasoupOptions;
            _hubContext = hubContext;
        }

        public override Task OnConnectedAsync()
        {
            ClosePeer();

            var handleResult = _meetingManager.PeerHandle(UserId, "Guest");
            if (handleResult)
            {
                return Clients.Caller.PeerHandled(new MeetingMessage { Code = 200, Message = "连接成功" });
            }

            return Clients.Caller.PeerHandled(new MeetingMessage { Code = 400, Message = "连接失败" });
        }

        public override Task OnDisconnectedAsync(Exception exception)
        {
            ClosePeer();

            return base.OnDisconnectedAsync(exception);
        }

        private void ClosePeer()
        {
            if (Group != null)
            {
                foreach (var otherPeer in Group.Peers.Values)
                {
                    if (otherPeer.PeerId == UserId)
                    {
                        continue;
                    }

                    var client = _hubContext.Clients.User(otherPeer.PeerId);
                    client.ReceiveMessage(new MeetingMessage
                    {
                        Code = 200,
                        InternalCode = "peerClosed",
                        Message = "peerClosed",
                        Data = new { PeerId = UserId }
                    }).ContinueWithOnFaultedHandleLog(_logger);
                }

                // Iterate and close all mediasoup Transport associated to this Peer, so all
                // its Producers and Consumers will also be closed.
                foreach (var transport in Peer!.Transports.Values)
                {
                    transport.Close();
                }
            }

            _meetingManager.PeerClose(UserId);
        }

        private string UserId => Context.User.Identity.Name;

        private Peer? Peer => _meetingManager.Peers.TryGetValue(UserId, out var peer) ? peer : null;

        private Group? Group => Peer?.Group;
    }

    public partial class MeetingHub
    {
        public MeetingMessage GetRouterRtpCapabilities()
        {
            var rtpCapabilities = _meetingManager.DefaultRtpCapabilities;
            return new MeetingMessage { Code = 200, Message = "GetRouterRtpCapabilities 成功", Data = rtpCapabilities };
        }

        public async Task<MeetingMessage> Join(JoinRequest joinRequest)
        {
            // TODO: (alby)校验 Peer 是否有权限进入该 Group

            if (!await _meetingManager.PeerJoinAsync(UserId, 
                joinRequest.RtpCapabilities, 
                joinRequest.SctpCapabilities, 
                joinRequest.Sources,
                joinRequest.GroupId,
                joinRequest.AppData))
            {
                return new MeetingMessage { Code = 400, Message = "Join 失败: PeerJoin 失败" };
            }

            foreach (var otherPeer in Group!.Peers.Values)
            {
                if (otherPeer.PeerId == UserId)
                {
                    continue;
                }

                // Notify the new Peer to all other Peers.
                // Message: newPeer
                var client = _hubContext.Clients.User(otherPeer.PeerId);
                client.ReceiveMessage(new MeetingMessage
                {
                    Code = 200,
                    InternalCode = "newPeer",
                    Message = "newPeer",
                    Data = new
                    {
                        Id = Peer!.PeerId,
                        Peer.DisplayName,
                    }
                }).ContinueWithOnFaultedHandleLog(_logger);
            }

            return new MeetingMessage { Code = 200, Message = "Join 成功" };
        }

        public async Task<MeetingMessage> CreateWebRtcTransport(CreateWebRtcTransportRequest createWebRtcTransportRequest)
        {
            if (Group == null)
            {
                return new MeetingMessage { Code = 200, Message = "GetRouterRtpCapabilities 失败" };
            }

            var webRtcTransportSettings = _mediasoupOptions.MediasoupSettings.WebRtcTransportSettings;
            var webRtcTransportOptions = new WebRtcTransportOptions
            {
                ListenIps = webRtcTransportSettings.ListenIps,
                InitialAvailableOutgoingBitrate = webRtcTransportSettings.InitialAvailableOutgoingBitrate,
                MaxSctpMessageSize = webRtcTransportSettings.MaxSctpMessageSize,
                EnableSctp = createWebRtcTransportRequest.SctpCapabilities != null,
                NumSctpStreams = createWebRtcTransportRequest.SctpCapabilities?.NumStreams,
                AppData = new Dictionary<string, object>
                {
                    { "Consuming", createWebRtcTransportRequest.Consuming },
                    { "Producing", createWebRtcTransportRequest.Producing },
                },
            };

            if (createWebRtcTransportRequest.ForceTcp)
            {
                webRtcTransportOptions.EnableUdp = false;
                webRtcTransportOptions.EnableTcp = true;
            }

            var transport = await Group.Router.CreateWebRtcTransportAsync(webRtcTransportOptions);
            if (transport == null)
            {
                return new MeetingMessage { Code = 400, Message = "CreateWebRtcTransport 失败" };
            }

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
                    var client = _hubContext.Clients.User(peerId.ToString());
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

            // Store the WebRtcTransport into the Peer data Object.
            Peer!.Transports[transport.TransportId] = transport;

            // If set, apply max incoming bitrate limit.
            if (webRtcTransportSettings.MaximumIncomingBitrate.HasValue && webRtcTransportSettings.MaximumIncomingBitrate.Value > 0)
            {
                // Fire and forget
                transport.SetMaxIncomingBitrateAsync(webRtcTransportSettings.MaximumIncomingBitrate.Value).ContinueWithOnFaultedHandleLog(_logger);
            }

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
            if (!Peer!.Transports.TryGetValue(connectWebRtcTransportRequest.TransportId, out var transport))
            {
                return new MeetingMessage { Code = 400, Message = "ConnectWebRtcTransport 失败" };
            }

            await transport.ConnectAsync(connectWebRtcTransportRequest.DtlsParameters);
            return new MeetingMessage { Code = 200, Message = "ConnectWebRtcTransport 成功" };
        }

        public async Task<MeetingMessage> Produce(ProduceRequest produceRequest)
        {
            if (Group == null || !Peer!.Transports.TryGetValue(produceRequest.TransportId, out var transport))
            {
                return new MeetingMessage { Code = 400, Message = "Produce 失败" };
            }

            // Add peerId into appData to later get the associated Peer during
            // the 'loudest' event of the audioLevelObserver.
            produceRequest.AppData["peerId"] = Peer.PeerId;

            var producer = await transport.ProduceAsync(new ProducerOptions
            {
                Kind = produceRequest.Kind,
                RtpParameters = produceRequest.RtpParameters,
                AppData = produceRequest.AppData,
            });

            // Store the Producer into the Peer data Object.
            Peer.Producers[producer.ProducerId] = producer;

            // Set Producer events.
            var peerId = Peer.PeerId;
            producer.On("score", score =>
            {
                var data = (ProducerScore[])score!;
                // Message: producerScore
                var client = _hubContext.Clients.User(peerId.ToString());
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

        public Task<MeetingMessage> CloseProducer(string producerId)
        {
            if (Group == null || !Peer!.Producers.TryGetValue(producerId, out var producer))
            {
                return Task.FromResult(new MeetingMessage { Code = 400, Message = "CloseProducer 失败" });
            }

            producer.Close();
            Peer.Producers.Remove(producerId);

            return Task.FromResult(new MeetingMessage { Code = 200, Message = "CloseProducer 成功" });
        }

        public async Task<MeetingMessage> PauseProducer(string producerId)
        {
            if (Group == null || !Peer!.Producers.TryGetValue(producerId, out var producer))
            {
                return new MeetingMessage { Code = 400, Message = "PauseProducer 失败" };
            }

            await producer.PauseAsync();

            return new MeetingMessage { Code = 200, Message = "PauseProducer 成功" };
        }

        public async Task<MeetingMessage> ResumeProducer(string producerId)
        {
            if (Group == null || !Peer!.Producers.TryGetValue(producerId, out var producer))
            {
                return new MeetingMessage { Code = 400, Message = "ResumeProducer 失败" };
            }

            await producer.ResumeAsync();

            return new MeetingMessage { Code = 200, Message = "ResumeProducer 成功" };
        }

        public async Task<MeetingMessage> AskForProduce(AskForProduceRequest askForProduceRequest)
        {
            if (Group == null || !Group.Peers.TryGetValue(askForProduceRequest.PeerId, out var otherPeer))
            {
                return new MeetingMessage { Code = 400, Message = "AskForProduce 失败" };
            }

            if (otherPeer!.Sources.IsNullOrEmpty())
            {
                return new MeetingMessage { Code = 400, Message = "AskForProduce 失败: None sources" };
            }

            var askForSources = otherPeer!.Sources.Intersect(askForProduceRequest.Sources).ToArray();
            if(askForSources.Length != askForProduceRequest.Sources.Length)
            {
                return new MeetingMessage { Code = 400, Message = "AskForProduce 失败: Some sources doesn't exist." };
            }

            // Message: newAskFor
            var client = _hubContext.Clients.User(otherPeer.PeerId);
            await client.NewAskFor(new MeetingMessage
            {
                Code = 200,
                InternalCode = "newAskFor",
                Message = "newAskFor",
                Data = new
                {
                    PeerId = Peer!.PeerId,
                    Sources = askForSources,
                }
            });

            return new MeetingMessage { Code = 200, Message = "AskForProduce 成功" };
        }

        public MeetingMessage Consume(ConsumeRequest consumeRequest)
        {
            // 只能消费本组
            if (Group == null || !Group.Peers.TryGetValue(consumeRequest.PeerId, out var otherPeer))
            {
                return new MeetingMessage { Code = 400, Message = "Consume 失败" };
            }

            if (!consumeRequest.ProducerIds.IsNullOrEmpty())
            {
                foreach (var producerId in consumeRequest.ProducerIds!)
                {
                    if (!otherPeer.Producers.TryGetValue(producerId, out var producer))
                    {
                        _logger.LogWarning($"Consume() | None producer: [peerId:\"{consumeRequest.PeerId}\", producerId:\"{producerId}\"]");
                        continue;
                    }

                    // 本 Peer 消费其他 Peer
                    CreateConsumer(Peer!, otherPeer, producer).ContinueWithOnFaultedHandleLog(_logger);
                }
            }
            else
            {
                foreach (var producer in otherPeer.Producers.Values)
                {
                    // 本 Peer 消费其他 Peer
                    CreateConsumer(Peer!, otherPeer, producer).ContinueWithOnFaultedHandleLog(_logger);
                }
            }

            return new MeetingMessage { Code = 200, Message = "Consume 成功" };
        }

        public Task<MeetingMessage> CloseConsumer(string consumerId)
        {
            if (Group == null || !Peer!.Consumers.TryGetValue(consumerId, out var consumer))
            {
                return Task.FromResult(new MeetingMessage { Code = 400, Message = "CloseConsumer 失败" });
            }

            consumer.Close();
            Peer.Consumers.Remove(consumerId);

            return Task.FromResult(new MeetingMessage { Code = 200, Message = "CloseConsumer 成功" });
        }

        public async Task<MeetingMessage> PauseConsumer(string consumerId)
        {
            if (Group == null || !Peer!.Consumers.TryGetValue(consumerId, out var consumer))
            {
                return new MeetingMessage { Code = 400, Message = "PauseConsumer 失败" };
            }

            await consumer.PauseAsync();

            return new MeetingMessage { Code = 200, Message = "PauseConsumer 成功" };
        }

        public async Task<MeetingMessage> ResumeConsumer(string consumerId)
        {
            if (Group == null || !Peer!.Consumers.TryGetValue(consumerId, out var consumer))
            {
                return new MeetingMessage { Code = 400, Message = "ResumeConsumer 失败" };
            }

            await consumer.ResumeAsync();

            return new MeetingMessage { Code = 200, Message = "ResumeConsumer 成功" };
        }

        public async Task<MeetingMessage> SetConsumerPreferedLayers(SetConsumerPreferedLayersRequest setConsumerPreferedLayersRequest)
        {
            if (Group == null || !Peer!.Consumers.TryGetValue(setConsumerPreferedLayersRequest.ConsumerId, out var consumer))
            {
                return new MeetingMessage { Code = 400, Message = "SetConsumerPreferedLayers 失败" };
            }

            await consumer.SetPreferredLayersAsync(setConsumerPreferedLayersRequest);

            return new MeetingMessage { Code = 200, Message = "SetConsumerPreferedLayers 成功" };
        }

        public async Task<MeetingMessage> SetConsumerPriority(SetConsumerPriorityRequest setConsumerPriorityRequest)
        {
            if (Group == null || !Peer!.Consumers.TryGetValue(setConsumerPriorityRequest.ConsumerId, out var consumer))
            {
                return new MeetingMessage { Code = 400, Message = "SetConsumerPriority 失败" };
            }

            await consumer.SetPriorityAsync(setConsumerPriorityRequest.Priority);

            return new MeetingMessage { Code = 200, Message = "SetConsumerPriority 成功" };
        }

        public async Task<MeetingMessage> RequestConsumerKeyFrame(string consumerId)
        {
            if (Group == null || !Peer!.Consumers.TryGetValue(consumerId, out var consumer))
            {
                return new MeetingMessage { Code = 400, Message = "RequestConsumerKeyFrame 失败" };
            }

            await consumer.RequestKeyFrameAsync();

            return new MeetingMessage { Code = 200, Message = "RequestConsumerKeyFrame 成功" };
        }

        public async Task<MeetingMessage> ProduceData(ProduceDataRequest produceDataRequest)
        {
            if (Group == null || !Peer!.Transports.TryGetValue(produceDataRequest.TransportId, out var transport))
            {
                return new MeetingMessage { Code = 400, Message = "ProduceData 失败" };
            }

            var dataProducer = await transport.ProduceDataAsync(new DataProducerOptions
            {
                Id = produceDataRequest.TransportId,
                SctpStreamParameters = produceDataRequest.SctpStreamParameters,
                Label = produceDataRequest.Label,
                Protocol = produceDataRequest.Protocol,
                AppData = produceDataRequest.AppData,
            });

            // Store the Producer into the protoo Peer data Object.
            Peer.DataProducers[dataProducer.DataProducerId] = dataProducer;

            // Create a server-side DataConsumer for each Peer.
            foreach (var otherPeer in Group.Peers.Values)
            {
                if (otherPeer.PeerId == UserId)
                {
                    continue;
                }

                // 其他 Peer 消费本 Peer
                CreateDataConsumer(otherPeer, Peer, dataProducer).ContinueWithOnFaultedHandleLog(_logger);
            }

            return new MeetingMessage { Code = 200, Message = "ProduceData 成功" };
        }

        public async Task<MeetingMessage> GetTransportStats(string transportId)
        {
            if (Group == null || !Peer!.Transports.TryGetValue(transportId, out var transport))
            {
                return new MeetingMessage { Code = 400, Message = "GetTransportStats 失败" };
            }

            var status = await transport.GetStatsAsync();
            // TODO: (alby)考虑不进行反序列化
            // TODO: (alby)实际上有 WebTransportStat、PlainTransportStat、PipeTransportStat 和 DirectTransportStat。这里反序列化后会丢失数据。
            var data = JsonConvert.DeserializeObject<TransportStat>(status!);

            return new MeetingMessage { Code = 200, Message = "GetTransportStats 成功", Data = data };
        }

        public async Task<MeetingMessage> GetProducerStats(string producerId)
        {
            if (Group == null || !Peer!.Producers.TryGetValue(producerId, out var producer))
            {
                return new MeetingMessage { Code = 400, Message = "GetProducerStats 失败" };
            }
            var status = await producer.GetStatsAsync();
            // TODO: (alby)考虑不进行反序列化
            var data = JsonConvert.DeserializeObject<ProducerStat>(status!);

            return new MeetingMessage { Code = 200, Message = "GetProducerStats 成功", Data = data };
        }

        public async Task<MeetingMessage> GetConsumerStats(string consumerId)
        {
            if (Group == null || !Peer!.Consumers.TryGetValue(consumerId, out var consumer))
            {
                return new MeetingMessage { Code = 400, Message = "GetConsumerStats 失败" };
            }

            var status = await consumer.GetStatsAsync();
            // TODO: (alby)考虑不进行反序列化
            var data = JsonConvert.DeserializeObject<ConsumerStat>(status!);

            return new MeetingMessage { Code = 200, Message = "GetConsumerStats 成功", Data = data };
        }

        public async Task<MeetingMessage> GetDataConsumerStats(string dataConsumerId)
        {
            if (Group == null || !Peer!.DataConsumers.TryGetValue(dataConsumerId, out var dataConsumer))
            {
                return new MeetingMessage { Code = 400, Message = "GetDataConsumerStats 失败" };
            }

            var status = await dataConsumer.GetStatsAsync();
            // TODO: (alby)考虑不进行反序列化
            var data = JsonConvert.DeserializeObject<DataConsumerStat>(status!);

            return new MeetingMessage { Code = 200, Message = "GetDataConsumerStats 成功", Data = data };
        }

        public async Task<MeetingMessage> GetDataProducerStats(string dataProducerId)
        {
            if (Group == null || !Peer!.DataProducers.TryGetValue(dataProducerId, out var dataProducer))
            {
                return new MeetingMessage { Code = 400, Message = "GetDataProducerStats 失败" };
            }
            var status = await dataProducer.GetStatsAsync();
            // TODO: (alby)考虑不进行反序列化
            var data = JsonConvert.DeserializeObject<DataProducerStat>(status!);

            return new MeetingMessage { Code = 200, Message = "GetDataProducerStats 成功", Data = data };
        }

        public async Task<MeetingMessage> RestartIce(string transportId)
        {
            if (Group == null || !Peer!.Transports.TryGetValue(transportId, out var transport))
            {
                return new MeetingMessage { Code = 400, Message = "RestartIce 失败" };
            }

            if (!(transport is WebRtcTransport webRtcTransport))
            {
                return new MeetingMessage { Code = 400, Message = "RestartIce 失败" };
            }

            var iceParameters = await webRtcTransport.RestartIceAsync();

            return new MeetingMessage { Code = 200, Message = "RestartIce 成功", Data = iceParameters };
        }

        #region CreateConsumer

        private async Task CreateConsumer(Peer consumerPeer, Peer producerPeer, Producer producer)
        {
            _logger.LogDebug($"CreateConsumer() | [consumerPeer:\"{consumerPeer.PeerId}\", producerPeer:\"{producerPeer.PeerId}\", producer:\"{producer.ProducerId}\"]");

            if (Group == null)
            {
                _logger.LogError($"CreateConsumer() | [Group is null]");
                return;
            }

            // Optimization:
            // - Create the server-side Consumer. If video, do it paused.
            // - Tell its Peer about it and wait for its response.
            // - Upon receipt of the response, resume the server-side Consumer.
            // - If video, this will mean a single key frame requested by the
            //   server-side Consumer (when resuming it).

            // NOTE: Don't create the Consumer if the remote Peer cannot consume it.
            if (consumerPeer.RtpCapabilities == null || !Group.Router.CanConsume(producer.ProducerId, consumerPeer.RtpCapabilities))
            {
                return;
            }

            // Must take the Transport the remote Peer is using for consuming.
            var transport = consumerPeer.GetConsumerTransport();
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

            if (!_meetingManager.Peers.TryGetValue(newConsumerReturnRequest.PeerId, out var consumerPeer) ||
                consumerPeer.Closed ||
                !consumerPeer.Consumers.TryGetValue(newConsumerReturnRequest.ConsumerId, out var consumer) ||
                consumer.Closed)
            {
                return new MeetingMessage { Code = 400, Message = "NewConsumerReturn 失败" };
            }

            // Now that we got the positive response from the remote Peer and, if
            // video, resume the Consumer to ask for an efficient key frame.
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

        #region CreateDataConsumer

        /// <summary>
        /// Creates a mediasoup DataConsumer for the given mediasoup DataProducer.
        /// </summary>
        /// <param name="dataConsumerPeer"></param>
        /// <param name="dataProducerPeer"></param>
        /// <param name="dataProducer"></param>
        /// <returns></returns>
        private async Task CreateDataConsumer(Peer dataConsumerPeer, Peer dataProducerPeer, DataProducer dataProducer)
        {
            // NOTE: Don't create the DataConsumer if the remote Peer cannot consume it.
            if (dataConsumerPeer.SctpCapabilities == null)
            {
                return;
            }

            // Must take the Transport the remote Peer is using for consuming.
            var transport = dataConsumerPeer.GetConsumerTransport();
            // This should not happen.
            if (transport == null)
            {
                _logger.LogWarning("CreateDataConsumer() | Transport for consuming not found");
                return;
            }

            // Create the DataConsumer.
            DataConsumer dataConsumer;

            try
            {
                dataConsumer = await transport.ConsumeDataAsync(new DataConsumerOptions
                {
                    DataProducerId = dataProducer.DataProducerId,
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"CreateDataConsumer() | [error:\"{ex}\"]");
                return;
            }

            // Store the DataConsumer into the protoo dataConsumerPeer data Object.
            dataConsumerPeer.DataConsumers[dataConsumer.DataConsumerId] = dataConsumer;

            // Set DataConsumer events.
            dataConsumer.On("transportclose", _ =>
            {
                // Remove from its map.
                dataConsumerPeer.DataConsumers.Remove(dataConsumer.DataConsumerId);
            });

            dataConsumer.On("dataproducerclose", _ =>
            {
                // Remove from its map.
                dataConsumerPeer.DataConsumers.Remove(dataConsumer.DataConsumerId);

                // Message: consumerClosed
                var client = _hubContext.Clients.User(dataConsumerPeer.PeerId);
                client.ReceiveMessage(new MeetingMessage
                {
                    Code = 200,
                    InternalCode = "dataConsumerClosed",
                    Message = "dataConsumerClosed",
                    Data = new { DataConsumerId = dataConsumer.DataConsumerId }
                }).ContinueWithOnFaultedHandleLog(_logger);
            });

            // Send a protoo request to the remote Peer with Consumer parameters.
            try
            {
                // Message: newConsumer
                var client = _hubContext.Clients.User(dataConsumerPeer.PeerId);
                await client.NewConsumer(new MeetingMessage
                {
                    Code = 200,
                    InternalCode = "newDataConsumer",
                    Message = "newDataConsumer",
                    Data = new
                    {
                        PeerId = dataProducerPeer?.PeerId,
                        DataProducerId = dataProducer.DataProducerId,
                        Id = dataConsumer.DataConsumerId,
                        SctpStreamParameters = dataConsumer.SctpStreamParameters,
                        Lablel = dataConsumer.Label,
                        Protocol = dataConsumer.Protocol,
                        AppData = dataProducer.AppData,
                        // CreateDataConsumer 和 CreateConsumer 不同，前者在请求客户端后不进行后续操作。所以，这里不用加一个 DataConsumerPeerId 属性。
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"CreateDataConsumer() | [error:\"{ex}\"]");
            }
        }

        #endregion
    }
}
