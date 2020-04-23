using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
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

        public int InternalCode { get; set; }

        public string Message { get; set; } = "Success";

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public object? Data { get; set; }
    }

    public interface IPeer
    {
        Task PeerHandled(MeetingMessage message);

        Task ReceiveMessage(MeetingMessage message);

        Task ReceiveNotification(MeetingMessage message);
    }

    [Authorize]
    public partial class MeetingHub : Hub<IPeer>
    {
        private readonly ILogger<MeetingHub> _logger;
        private readonly MediasoupOptions _mediasoupOptions;
        private readonly MeetingManager _meetingManager;

        public MeetingHub(ILogger<MeetingHub> logger, MeetingManager meetingManager, MediasoupOptions mediasoupOptions)
        {
            _logger = logger;
            _meetingManager = meetingManager;
            _mediasoupOptions = mediasoupOptions;
        }

        public override Task OnConnectedAsync()
        {
            var handleResult = _meetingManager.HandlePeer(UserId, "Guest");
            if (handleResult)
            {
                return Clients.Caller.PeerHandled(new MeetingMessage { Code = 200, InternalCode = 10001, Message = "连接成功" });
            }
            return Clients.Caller.PeerHandled(new MeetingMessage { Code = 400, InternalCode = 10002, Message = "连接失败" });
        }

        public override Task OnDisconnectedAsync(Exception exception)
        {
            _meetingManager.ClosePeer(UserId);
            return base.OnDisconnectedAsync(exception);
        }

        private int UserId => int.Parse(Context.User.Identity.Name);

        private PeerRoom? PeerRoom => _meetingManager.GetPeerRoomWithPeerId(UserId);
    }

    public partial class MeetingHub
    {
        public Task SendMessageByUserIdAsync(int userId, MeetingMessage message)
        {
            var client = Clients.User(userId.ToString());
            return client.ReceiveMessage(message);
        }

        public Task SendMessageAsync(string connectionId, MeetingMessage message)
        {
            var client = Clients.Client(connectionId);
            return client.ReceiveMessage(message);
        }

        public Task SendMessageToCaller(MeetingMessage message)
        {
            return Clients.Caller.ReceiveMessage(message);
        }

        public Task BroadcastMessageAsync(MeetingMessage message)
        {
            return Clients.All.ReceiveMessage(message);
        }
    }

    public partial class MeetingHub
    {
        public async Task<MeetingMessage> EnterRoom(Guid roomId)
        {
            if (!await _meetingManager.PeerEnterRoomAsync(UserId, roomId))
            {
                return new MeetingMessage { Code = 400, InternalCode = 10004, Message = "Failure" };
            }

            foreach (var otherPeer in _meetingManager.GetPeersWithRoomId(PeerRoom!.Room.RoomId))
            {
                if (otherPeer.PeerId != PeerRoom.Peer.PeerId)
                {
                    foreach (var producer in otherPeer.Producers.Values)
                    {
                        CreateConsumer(PeerRoom.Peer, otherPeer, producer).ContinueWithOnFaultedHandleLog(_logger);
                    }

                    // Notify the new Peer to all other Peers.
                    // Message: newPeer
                    SendMessageByUserIdAsync(otherPeer.PeerId, new MeetingMessage
                    {
                        Code = 200,
                        InternalCode = 20001,
                        Message = "Success",
                        Data = new
                        {
                            Id = PeerRoom.Peer.PeerId,
                            PeerRoom.Peer.DisplayName,
                        }
                    }).ContinueWithOnFaultedHandleLog(_logger);
                }
            }

            return new MeetingMessage { Code = 200, InternalCode = 10003, Message = "Success" };
        }

        public Task<MeetingMessage> GetRouterRtpCapabilities()
        {
            if (PeerRoom != null)
            {
                var rtpCapabilities = PeerRoom.Room.Router.RtpCapabilities;
                return Task.FromResult(new MeetingMessage { Code = 200, InternalCode = 10005, Message = "Success", Data = rtpCapabilities });
            }

            return Task.FromResult(new MeetingMessage { Code = 400, InternalCode = 10006, Message = "Failure" });
        }

        public async Task<MeetingMessage> CreateWebRtcTransport(CreateWebRtcTransportParameters createWebRtcTransportParameters)
        {
            var peerRoom = _meetingManager.GetPeerRoomWithPeerId(UserId);
            if (peerRoom == null)
            {
                return new MeetingMessage { Code = 400, InternalCode = 10010, Message = "Failure" };
            }

            var webRtcTransportSettings = _mediasoupOptions.MediasoupSettings.WebRtcTransportSettings;
            var webRtcTransportOptions = new WebRtcTransportOptions
            {
                ListenIps = webRtcTransportSettings.ListenIps,
                InitialAvailableOutgoingBitrate = webRtcTransportSettings.InitialAvailableOutgoingBitrate,
                AppData = new Dictionary<string, object>
                {
                    { "Consuming", createWebRtcTransportParameters.Consuming },
                    { "Producing", createWebRtcTransportParameters.Producing },
                },
            };

            if (createWebRtcTransportParameters.ForceTcp)
            {
                webRtcTransportOptions.EnableUdp = false;
                webRtcTransportOptions.EnableTcp = true;
            }

            var transport = await peerRoom.Room.Router.CreateWebRtcTransportAsync(webRtcTransportOptions);
            if (transport == null)
            {
                return new MeetingMessage { Code = 400, InternalCode = 10010, Message = "Failure" };
            }

            transport.On("dtlsstatechange", value =>
            {
                var dtlsState = (DtlsState)value!;
                if (dtlsState == DtlsState.Failed || dtlsState == DtlsState.Closed)
                {
                    _logger.LogWarning($"WebRtcTransport dtlsstatechange event [dtlsState:{value}]");
                }
            });

            // Store the WebRtcTransport into the Peer data Object.
            peerRoom.Peer.Transports[transport.Internal.TransportId] = transport;

            // If set, apply max incoming bitrate limit.
            if (webRtcTransportSettings.MaximumIncomingBitrate.HasValue && webRtcTransportSettings.MaximumIncomingBitrate.Value > 0)
            {
                // Fire and forget
                transport.SetMaxIncomingBitrateAsync(webRtcTransportSettings.MaximumIncomingBitrate.Value).ContinueWithOnFaultedHandleLog(_logger);
            }

            return new MeetingMessage
            {
                Code = 200,
                InternalCode = 10009,
                Message = "Success",
                Data = new CreateWebRtcTransportResult
                {
                    Id = transport.Internal.TransportId,
                    IceParameters = transport.IceParameters,
                    IceCandidates = transport.IceCandidates,
                    DtlsParameters = transport.DtlsParameters,
                }
            };
        }

        public async Task<MeetingMessage> ConnectWebRtcTransport(ConnectWebRtcTransportRequest connectWebRtcTransportRequest)
        {
            if (PeerRoom == null)
            {
                return new MeetingMessage { Code = 400, InternalCode = 10012, Message = "Failure" };
            }

            if (!PeerRoom.Peer.Transports.TryGetValue(connectWebRtcTransportRequest.TransportId, out var transport))
            {
                return new MeetingMessage { Code = 400, InternalCode = 10012, Message = "Failure" };
            }

            await transport.ConnectAsync(connectWebRtcTransportRequest.DtlsParameters);
            return new MeetingMessage { Code = 400, InternalCode = 10011, Message = "Success" };
        }

        public async Task<MeetingMessage> Produce(ProduceRequest produceRequest)
        {
            if (PeerRoom == null || !PeerRoom.Peer.Transports.TryGetValue(produceRequest.TransportId, out var transport))
            {
                return new MeetingMessage { Code = 400, InternalCode = 10016, Message = "Failure" };
            }

            // Add peerId into appData to later get the associated Peer during
            // the 'loudest' event of the audioLevelObserver.
            produceRequest.AppData["peerId"] = PeerRoom.Peer.PeerId;

            var producer = await transport.ProduceAsync(new ProducerOptions
            {
                Kind = produceRequest.Kind,
                RtpParameters = produceRequest.RtpParameters,
                AppData = produceRequest.AppData,
            });

            // Store the Producer into the Peer data Object.
            PeerRoom.Peer.Producers[producer.Id] = producer;

            // Set Producer events.
            producer.On("score", score =>
            {
                // TODO: (alby)考虑不进行反序列化
                var data = JsonConvert.DeserializeObject<ProducerScore[]>(score!.ToString());
                // Message: producerScore
                SendMessageByUserIdAsync(PeerRoom.Peer.PeerId, new MeetingMessage
                {
                    Code = 200,
                    InternalCode = 20002,
                    Message = "Success",
                    Data = new { ProducerId = producer.Id, Score = data }
                }).ContinueWithOnFaultedHandleLog(_logger);
            });
            producer.On("videoorientationchange", videoOrientation =>
            {
                // TODO: (alby)考虑不进行反序列化
                var data = JsonConvert.DeserializeObject<ProducerVideoOrientation>(videoOrientation!.ToString());
                _logger.LogDebug($"producer.On() | producer \"videoorientationchange\" event [producerId:\"{producer.Id}\", videoOrientation:\"{videoOrientation}\"]");
            });

            // Optimization: Create a server-side Consumer for each Peer.
            foreach (var otherPeer in _meetingManager.GetPeersWithRoomId(PeerRoom.Room.RoomId))
            {
                if (otherPeer.PeerId == PeerRoom.Peer.PeerId) continue;
                CreateConsumer(otherPeer, PeerRoom.Peer, producer).ContinueWithOnFaultedHandleLog(_logger);
            }

            // Add into the audioLevelObserver.
            if (produceRequest.Kind == MediaKind.Audio)
            {

            }

            return new MeetingMessage
            {
                Code = 400,
                InternalCode = 10015,
                Message = "Failure",
                Data = new ProduceResult { Id = producer.Id }
            };
        }

        public Task<MeetingMessage> Join(RtpCapabilities rtpCapabilities)
        {
            if (!_meetingManager.JoinPeer(UserId, rtpCapabilities))
            {
                return Task.FromResult(new MeetingMessage { Code = 400, InternalCode = 10008, Message = "Failure" });
            }

            return Task.FromResult(new MeetingMessage { Code = 200, InternalCode = 10007, Message = "Success" });
        }

        private async Task CreateConsumer(Peer consumerPeer, Peer producerPeer, Producer producer)
        {
            _logger.LogDebug($"CreateConsumer() | [consumerPeer:\"{consumerPeer.PeerId}\", producerPeer:\"{producerPeer.PeerId}\", producer:\"{producer.Id}\"]");

            // Optimization:
            // - Create the server-side Consumer. If video, do it paused.
            // - Tell its Peer about it and wait for its response.
            // - Upon receipt of the response, resume the server-side Consumer.
            // - If video, this will mean a single key frame requested by the
            //   server-side Consumer (when resuming it).

            // NOTE: Don't create the Consumer if the remote Peer cannot consume it.
            if (consumerPeer.RtpCapabilities == null || !PeerRoom!.Room.Router.CanConsume(producer.Id, consumerPeer.RtpCapabilities))
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
                    ProducerId = producer.Id,
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
            consumerPeer.Consumers[consumer.Id] = consumer;

            consumer.On("score", (score) =>
            {
                // TODO: (alby)考虑不进行反序列化
                var data = JsonConvert.DeserializeObject<ProducerScore[]>(score!.ToString());
                // Message: consumerScore
                SendMessageByUserIdAsync(consumerPeer.PeerId, new MeetingMessage
                {
                    Code = 200,
                    InternalCode = 20003,
                    Message = "Success",
                    Data = new { ConsumerId = consumer.Id, Score = data }
                }).ContinueWithOnFaultedHandleLog(_logger);
            });

            // Set Consumer events.
            consumer.On("transportclose", _ =>
            {
                // Remove from its map.
                consumerPeer.Consumers.Remove(consumer.Id);
            });

            consumer.On("producerclose", _ =>
            {
                // Remove from its map.
                consumerPeer.Consumers.Remove(consumer.Id);

                // Message: consumerClosed
                SendMessageByUserIdAsync(consumerPeer.PeerId, new MeetingMessage
                {
                    Code = 200,
                    InternalCode = 20004,
                    Message = "Success",
                    Data = new { ConsumerId = consumer.Id }
                }).ContinueWithOnFaultedHandleLog(_logger);
            });

            consumer.On("producerpause", _ =>
            {
                // Message: consumerPaused
                SendMessageByUserIdAsync(consumerPeer.PeerId, new MeetingMessage
                {
                    Code = 200,
                    InternalCode = 20005,
                    Message = "Success",
                    Data = new { ConsumerId = consumer.Id }
                }).ContinueWithOnFaultedHandleLog(_logger);
            });

            consumer.On("producerresume", _ =>
            {
                // Message: consumerResumed
                SendMessageByUserIdAsync(consumerPeer.PeerId, new MeetingMessage
                {
                    Code = 200,
                    InternalCode = 20006,
                    Message = "Success",
                    Data = new { ConsumerId = consumer.Id }
                }).ContinueWithOnFaultedHandleLog(_logger);
            });

            consumer.On("layerschange", layers =>
            {

                var data = JsonConvert.DeserializeObject<ConsumerLayers>(layers!.ToString());
                // Message: consumerLayersChanged
                SendMessageByUserIdAsync(consumerPeer.PeerId, new MeetingMessage
                {
                    Code = 200,
                    InternalCode = 20007,
                    Message = "Success",
                    Data = new { ConsumerId = consumer.Id, SpatialLayer = data != null ? (int?)data.SpatialLayer : null, TemporalLayer = data != null ? (int?)data.TemporalLayer : null }
                }).ContinueWithOnFaultedHandleLog(_logger);
            });

            // Send a request to the remote Peer with Consumer parameters.
            try
            {
                // Message: newConsumer
                await SendMessageByUserIdAsync(producerPeer.PeerId, new MeetingMessage
                {
                    Code = 200,
                    InternalCode = 20008,
                    Message = "Success",
                    Data = new
                    {
                        PeerId = consumer.Id,
                        Kind = consumer.Kind,
                        ProducerId = producer.Id,
                        RtpParameters = consumer.RtpParameters,
                        Type = consumer.Type,
                        AppData = producer.AppData,
                        ProducerPaused = consumer.ProducerPaused,
                    }
                });

                // Now that we got the positive response from the remote Peer and, if
                // video, resume the Consumer to ask for an efficient key frame.
                await consumer.ResumeAsync();

                // Message: consumerScore
                SendMessageByUserIdAsync(consumerPeer.PeerId, new MeetingMessage
                {
                    Code = 200,
                    InternalCode = 20009,
                    Message = "Success",
                    Data = new
                    {
                        ConsumerId = consumer.Id,
                        Score = consumer.Score,
                    }
                }).ContinueWithOnFaultedHandleLog(_logger);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"CreateConsumer() | [error:\"{ex}\"]");
            }
        }

        public Task<MeetingMessage> CloseProducer(string producerId)
        {
            if (PeerRoom == null)
            {
                return Task.FromResult(new MeetingMessage { Code = 400, InternalCode = 10018, Message = "Failure" });
            }

            if (!PeerRoom.Peer.Producers.TryGetValue(producerId, out var producer))
            {
                return Task.FromResult(new MeetingMessage { Code = 400, InternalCode = 10018, Message = "Failure" });
            }

            producer.Close();
            PeerRoom.Peer.Producers.Remove(producerId);
            return Task.FromResult(new MeetingMessage { Code = 400, InternalCode = 10017, Message = "Success" });
        }

        public async Task<MeetingMessage> RestartIce(string transportId)
        {
            if (PeerRoom == null)
            {
                return new MeetingMessage { Code = 400, InternalCode = 10014, Message = "Failure" };
            }

            if (!PeerRoom.Peer.Transports.TryGetValue(transportId, out var transport))
            {
                return new MeetingMessage { Code = 400, InternalCode = 10014, Message = "Failure" };
            }

            var webRtcTransport = transport as WebRtcTransport;
            if (webRtcTransport == null)
            {
                return new MeetingMessage { Code = 400, InternalCode = 10014, Message = "Failure" };
            }

            var iceParameters = await webRtcTransport.RestartIceAsync();
            return new MeetingMessage { Code = 200, InternalCode = 10013, Message = "Success", Data = iceParameters };
        }

        public async Task<MeetingMessage> PauseProducer(string producerId)
        {
            if (PeerRoom == null || !PeerRoom.Peer.Producers.TryGetValue(producerId, out var producer))
            {
                return new MeetingMessage { Code = 400, InternalCode = 10020, Message = "Failure" };
            }

            await producer.PauseAsync();

            return new MeetingMessage { Code = 200, InternalCode = 10019, Message = "Success" };
        }

        public async Task<MeetingMessage> ResumeProducer(string producerId)
        {
            if (PeerRoom == null || !PeerRoom.Peer.Producers.TryGetValue(producerId, out var producer))
            {
                return new MeetingMessage { Code = 400, InternalCode = 10022, Message = "Failure" };
            }

            await producer.ResumeAsync();

            return new MeetingMessage { Code = 200, InternalCode = 10021, Message = "Success" };
        }

        public async Task<MeetingMessage> PauseConsumer(string consumerId)
        {
            if (PeerRoom == null || !PeerRoom.Peer.Consumers.TryGetValue(consumerId, out var consumer))
            {
                return new MeetingMessage { Code = 400, InternalCode = 10024, Message = "Failure" };
            }

            await consumer.PauseAsync();

            return new MeetingMessage { Code = 200, InternalCode = 10023, Message = "Success" };
        }

        public async Task<MeetingMessage> ResumeConsumer(string consumerId)
        {
            if (PeerRoom == null || !PeerRoom.Peer.Consumers.TryGetValue(consumerId, out var consumer))
            {
                return new MeetingMessage { Code = 400, InternalCode = 10026, Message = "Failure" };
            }

            await consumer.ResumeAsync();

            return new MeetingMessage { Code = 200, InternalCode = 10025, Message = "Success" };
        }

        public async Task<MeetingMessage> SetConsumerPreferedLayers(SetConsumerPreferedLayersRequest setConsumerPreferedLayersRequest)
        {
            if (PeerRoom == null || !PeerRoom.Peer.Consumers.TryGetValue(setConsumerPreferedLayersRequest.ConsumerId, out var consumer))
            {
                return new MeetingMessage { Code = 400, InternalCode = 10028, Message = "Failure" };
            }

            await consumer.SetPreferredLayersAsync(setConsumerPreferedLayersRequest);

            return new MeetingMessage { Code = 200, InternalCode = 10027, Message = "Success" };
        }

        public async Task<MeetingMessage> SetConsumerPriority(SetConsumerPriorityRequest setConsumerPriorityRequest)
        {
            if (PeerRoom == null || !PeerRoom.Peer.Consumers.TryGetValue(setConsumerPriorityRequest.ConsumerId, out var consumer))
            {
                return new MeetingMessage { Code = 400, InternalCode = 10030, Message = "Failure" };
            }

            await consumer.SetPriorityAsync(setConsumerPriorityRequest.Priority);

            return new MeetingMessage { Code = 200, InternalCode = 10029, Message = "Success" };
        }

        public async Task<MeetingMessage> RequestConsumerKeyFrame(string consumerId)
        {
            if (PeerRoom == null || !PeerRoom.Peer.Consumers.TryGetValue(consumerId, out var consumer))
            {
                return new MeetingMessage { Code = 400, InternalCode = 10032, Message = "Failure" };
            }

            await consumer.RequestKeyFrameAsync();

            return new MeetingMessage { Code = 200, InternalCode = 10031, Message = "Success" };
        }

        public async Task<MeetingMessage> GetTransportStats(string transportId)
        {
            if (PeerRoom == null || !PeerRoom.Peer.Transports.TryGetValue(transportId, out var transport))
            {
                return new MeetingMessage { Code = 400, InternalCode = 10034, Message = "Failure" };
            }

            var status = await transport.GetStatsAsync();
            // TODO: (alby)考虑不进行反序列化
            var data = JsonConvert.DeserializeObject<TransportStat>(status);

            return new MeetingMessage { Code = 200, InternalCode = 10033, Message = "Success", Data = data };
        }

        public async Task<MeetingMessage> GetProducerStats(string producerId)
        {
            if (PeerRoom == null || !PeerRoom.Peer.Producers.TryGetValue(producerId, out var producer))
            {
                return new MeetingMessage { Code = 400, InternalCode = 10036, Message = "Failure" };
            }
            var status = await producer.GetStatsAsync();
            // TODO: (alby)考虑不进行反序列化
            var data = JsonConvert.DeserializeObject<ProducerStat>(status);

            return new MeetingMessage { Code = 200, InternalCode = 10035, Message = "Success", Data = data };
        }

        public async Task<MeetingMessage> GetConsumerStats(string consumerId)
        {
            if (PeerRoom == null || !PeerRoom.Peer.Consumers.TryGetValue(consumerId, out var consumer))
            {
                return new MeetingMessage { Code = 400, InternalCode = 10038, Message = "Failure" };
            }

            var status = await consumer.GetStatsAsync();
            // TODO: (alby)考虑不进行反序列化
            var data = JsonConvert.DeserializeObject<ConsumerStat>(status);

            return new MeetingMessage { Code = 200, InternalCode = 10037, Message = "Success", Data = data };
        }
    }
}
