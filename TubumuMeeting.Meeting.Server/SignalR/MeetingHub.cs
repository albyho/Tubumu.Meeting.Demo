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
        Task ReceiveMessage(MeetingMessage message);
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
                return SendMessageToCaller(new MeetingMessage { Code = 200, InternalCode = 10001, Message = "连接成功" });
            }
            return Task.CompletedTask;
        }

        public override Task OnDisconnectedAsync(Exception exception)
        {
            _meetingManager.ClosePeer(UserId);
            return base.OnDisconnectedAsync(exception);
        }

        private int UserId => int.Parse(Context.User.Identity.Name);

        private Peer Peer => _meetingManager.Peers[UserId];
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
        public Task Join(RtpCapabilities rtpCapabilities)
        {
            var peer = Peer;
            if (!peer.Joined)
            {
                peer.RtpCapabilities = rtpCapabilities;
                peer.Joined = true;

                return SendMessageToCaller(new MeetingMessage { Code = 200, InternalCode = 10003, Message = "Success" });
            }

            return SendMessageToCaller(new MeetingMessage { Code = 400, InternalCode = 10004, Message = "Failure" });
        }

        public async Task EnterRoom(Guid roomId)
        {
            // TODO: (alby)代码清理
            var room = _meetingManager.GetOrCreateRoom(roomId, "Meeting");

            var peer = Peer;
            if (!peer.Joined)
            {
                await SendMessageToCaller(new MeetingMessage { Code = 400, InternalCode = 10006, Message = "Failure" });
                return;
            }

            var joinRessult = await _meetingManager.PeerEnterRoomAsync(UserId, roomId);
            if (joinRessult)
            {
                await SendMessageToCaller(new MeetingMessage { Code = 200, InternalCode = 10005, Message = "Success" });
                return;
            }

            await SendMessageToCaller(new MeetingMessage { Code = 400, InternalCode = 10006, Message = "Failure" });

            // Notify the new Peer to all other Peers.
        }

        public Task GetRouterRtpCapabilities()
        {
            var room = Peer.Room;
            if (room != null)
            {
                var rtpCapabilities = room.Router.RtpCapabilities;
                return SendMessageToCaller(new MeetingMessage { Code = 200, InternalCode = 10007, Message = "Success", Data = rtpCapabilities });
            }

            return SendMessageToCaller(new MeetingMessage { Code = 400, InternalCode = 10008, Message = "Failure" });
        }

        public async Task CreateWebRtcTransport(CreateWebRtcTransportParameters createWebRtcTransportParameters)
        {
            var peer = Peer;
            if (!peer.Joined || peer.Room == null)
            {
                await SendMessageToCaller(new MeetingMessage { Code = 400, InternalCode = 10010, Message = "Failure" });
                return;
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

            var transport = await peer.Room.Router.CreateWebRtcTransportAsync(webRtcTransportOptions);
            if (transport == null)
            {
                await SendMessageToCaller(new MeetingMessage { Code = 400, InternalCode = 10010, Message = "Failure" });
                return;
            }

            await SendMessageToCaller(new MeetingMessage
            {
                Code = 200,
                InternalCode = 10009,
                Message = "Success",
                Data = new CreateWebRtcTransportResult
                {
                    Id = transport.Id,
                    IceParameters = transport.IceParameters,
                    IceCandidates = transport.IceCandidates,
                    DtlsParameters = transport.DtlsParameters,
                }
            });

            transport.On("dtlsstatechange", value =>
            {
                var dtlsState = (DtlsState)value!;
                if (dtlsState == DtlsState.Failed || dtlsState == DtlsState.Closed)
                {
                    _logger.LogWarning($"WebRtcTransport dtlsstatechange event [dtlsState:{value}]");
                }
            });

            // Store the WebRtcTransport into the Peer data Object.
            peer.Transports[transport.Id] = transport;

            // If set, apply max incoming bitrate limit.
            if (webRtcTransportSettings.MaximumIncomingBitrate.HasValue && webRtcTransportSettings.MaximumIncomingBitrate.Value > 0)
            {
                // Fire and forget
                transport.SetMaxIncomingBitrateAsync(webRtcTransportSettings.MaximumIncomingBitrate.Value).ContinueWithOnFaultedHandleLog(_logger);
            }
        }

        public async Task ConnectWebRtcTransport(ConnectWebRtcTransportRequest connectWebRtcTransportRequest)
        {
            var peer = Peer;
            if (!peer.Joined || peer.Room == null)
            {
                await SendMessageToCaller(new MeetingMessage { Code = 400, InternalCode = 10012, Message = "Failure" });
                return;
            }

            if (!peer.Transports.TryGetValue(connectWebRtcTransportRequest.TransportId, out var transport))
            {
                await SendMessageToCaller(new MeetingMessage { Code = 400, InternalCode = 10012, Message = "Failure" });
                return;
            }

            await transport.ConnectAsync(connectWebRtcTransportRequest.DtlsParameters);
            await SendMessageToCaller(new MeetingMessage { Code = 200, InternalCode = 10011, Message = "Success" });
        }

        public async Task RestartIce(string transportId)
        {
            var peer = Peer;
            if (!peer.Joined || peer.Room == null)
            {
                await SendMessageToCaller(new MeetingMessage { Code = 400, InternalCode = 10014, Message = "Failure" });
                return;
            }

            if (!peer.Transports.TryGetValue(transportId, out var transport))
            {
                await SendMessageToCaller(new MeetingMessage { Code = 400, InternalCode = 10014, Message = "Failure" });
                return;
            }

            var webRtcTransport = transport as WebRtcTransport;
            if (webRtcTransport == null)
            {
                await SendMessageToCaller(new MeetingMessage { Code = 400, InternalCode = 10014, Message = "Failure" });
                return;
            }

            var iceParameters = await webRtcTransport.RestartIceAsync();
            await SendMessageToCaller(new MeetingMessage { Code = 200, InternalCode = 10013, Message = "Success", Data = iceParameters });
        }

        public async Task Produce(ProduceRequest produceRequest)
        {
            var peer = Peer;
            if (!peer.Joined || peer.Room == null)
            {
                await SendMessageToCaller(new MeetingMessage { Code = 400, InternalCode = 10016, Message = "Failure" });
                return;
            }

            if (!peer.Transports.TryGetValue(produceRequest.TransportId, out var transport))
            {
                await SendMessageToCaller(new MeetingMessage { Code = 400, InternalCode = 10016, Message = "Failure" });
                return;
            }

            // Add peerId into appData to later get the associated Peer during
            // the 'loudest' event of the audioLevelObserver.
            produceRequest.AppData["peerId"] = peer.PeerId;

            var producer = await transport.ProduceAsync(new ProducerOptions
            {
                Kind = produceRequest.Kind,
                RtpParameters = produceRequest.RtpParameters,
                AppData = produceRequest.AppData,
            });

            // Store the Producer into the Peer data Object.
            peer.Producers[producer.Id] = producer;

            // Set Producer events.
            producer.On("score", score =>
            {
                // TODO: (alby)考虑不进行反序列化
                var data = JsonConvert.DeserializeObject<ProducerScore[]>(score!.ToString());
                // Message: producerScore
                SendMessageToCaller(new MeetingMessage { Code = 200, InternalCode = 20001, Message = "Success", Data = new { ProducerId = producer.Id, Score = data } }).ContinueWithOnFaultedHandleLog(_logger);
            });
            producer.On("videoorientationchange", videoOrientation =>
            {
                // TODO: (alby)考虑不进行反序列化
                var data = JsonConvert.DeserializeObject<ProducerVideoOrientation>(videoOrientation!.ToString());
                _logger.LogDebug($"producer videoorientationchange event [producerId:\"{producer.Id}\", videoOrientation:\"{videoOrientation}\"]");
            });

            await SendMessageToCaller(new MeetingMessage { Code = 400, InternalCode = 10015, Message = "Failure", Data = new ProduceResult { Id = producer.Id } });

            // Optimization: Create a server-side Consumer for each Peer.
            foreach (var otherPeer in _meetingManager.Peers.Values)
            {
                if (otherPeer.Joined || otherPeer.Room == null || otherPeer.PeerId == peer.PeerId) continue;
                CreateConsumer(otherPeer, peer, producer).ContinueWithOnFaultedHandleLog(_logger);
            }

            // Add into the audioLevelObserver.
            if (produceRequest.Kind == MediaKind.Audio)
            {

            }
        }

        private async Task CreateConsumer(Peer consumerPeer, Peer producerPeer, Producer producer)
        {
            _logger.LogDebug($"CreateConsumer() [consumerPeer:\"{consumerPeer.PeerId}\", producerPeer:\"{producerPeer.PeerId}\", producer:\"{producer.Id}\"]");

            // Optimization:
            // - Create the server-side Consumer. If video, do it paused.
            // - Tell its Peer about it and wait for its response.
            // - Upon receipt of the response, resume the server-side Consumer.
            // - If video, this will mean a single key frame requested by the
            //   server-side Consumer (when resuming it).

            // NOTE: Don't create the Consumer if the remote Peer cannot consume it.
            if (consumerPeer.RtpCapabilities == null || !Peer.Room.Router.CanConsume(producer.Id, consumerPeer.RtpCapabilities))
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
                SendMessageToCaller(new MeetingMessage { Code = 200, InternalCode = 20002, Message = "Success", Data = new { ConsumerId = consumer.Id, Score = data } }).ContinueWithOnFaultedHandleLog(_logger);
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
                SendMessageToCaller(new MeetingMessage
                {
                    Code = 200,
                    InternalCode = 20003,
                    Message = "Success",
                    Data = new { ConsumerId = consumer.Id }
                })
                .ContinueWithOnFaultedHandleLog(_logger);
            });

            consumer.On("producerpause", _ =>
            {
                // Message: consumerPaused
                SendMessageToCaller(new MeetingMessage
                {
                    Code = 200,
                    InternalCode = 20004,
                    Message = "Success",
                    Data = new { ConsumerId = consumer.Id }
                })
                .ContinueWithOnFaultedHandleLog(_logger);
            });

            consumer.On("producerresume", _ =>
            {
                // Message: consumerResumed
                SendMessageToCaller(new MeetingMessage
                {
                    Code = 200,
                    InternalCode = 20005,
                    Message = "Success",
                    Data = new { ConsumerId = consumer.Id }
                })
                .ContinueWithOnFaultedHandleLog(_logger);
            });

            consumer.On("layerschange", layers =>
            {

                var data = JsonConvert.DeserializeObject<ConsumerLayers>(layers!.ToString());
                // Message: consumerLayersChanged
                SendMessageToCaller(new MeetingMessage
                {
                    Code = 200,
                    InternalCode = 20006,
                    Message = "Success",
                    Data = new { ConsumerId = consumer.Id, SpatialLayer = data != null ? (int?)data.SpatialLayer : null, TemporalLayer = data != null ? (int?)data.TemporalLayer : null }
                })
                .ContinueWithOnFaultedHandleLog(_logger);
            });

            // Send a request to the remote Peer with Consumer parameters.
            try
            {
                // Message: newConsumer
                await SendMessageByUserIdAsync(consumerPeer.PeerId, new MeetingMessage
                {
                    Code = 200,
                    InternalCode = 20007,
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
                    InternalCode = 20008,
                    Message = "Success",
                    Data = new
                    {
                        ConsumerId = consumer.Id,
                        Score = consumer.Score,
                    }
                })
                .ContinueWithOnFaultedHandleLog(_logger);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"CreateConsumer() | [error:\"{ex}\"]");
            }
        }

        public async Task CloseProducer(string producerId)
        {
            var peer = Peer;
            if (!peer.Joined || peer.Room == null)
            {
                await SendMessageToCaller(new MeetingMessage { Code = 400, InternalCode = 10018, Message = "Failure" });
                return;
            }

            if (!peer.Producers.TryGetValue(producerId, out var producer))
            {
                await SendMessageToCaller(new MeetingMessage { Code = 400, InternalCode = 10018, Message = "Failure" });
                return;
            }

            producer.Close();
            peer.Producers.Remove(producerId);
            await SendMessageToCaller(new MeetingMessage { Code = 200, InternalCode = 10017, Message = "Success" });
        }

        public async Task PauseProducer(string producerId)
        {
            var peer = Peer;
            if (!peer.Joined || peer.Room == null)
            {
                await SendMessageToCaller(new MeetingMessage { Code = 400, InternalCode = 10020, Message = "Failure" });
                return;
            }

            if (!peer.Producers.TryGetValue(producerId, out var producer))
            {
                await SendMessageToCaller(new MeetingMessage { Code = 400, InternalCode = 10020, Message = "Failure" });
                return;
            }

            await producer.PauseAsync();

            await SendMessageToCaller(new MeetingMessage { Code = 200, InternalCode = 10019, Message = "Success" });
        }

        public async Task ResumeProducer(string producerId)
        {
            var peer = Peer;
            if (!peer.Joined || peer.Room == null)
            {
                await SendMessageToCaller(new MeetingMessage { Code = 400, InternalCode = 10022, Message = "Failure" });
                return;
            }

            if (!peer.Producers.TryGetValue(producerId, out var producer))
            {
                await SendMessageToCaller(new MeetingMessage { Code = 400, InternalCode = 10022, Message = "Failure" });
                return;
            }

            await producer.ResumeAsync();

            await SendMessageToCaller(new MeetingMessage { Code = 200, InternalCode = 10021, Message = "Success" });
        }

        public async Task PauseConsumer(string consumerId)
        {
            var peer = Peer;
            if (!peer.Joined || peer.Room == null)
            {
                await SendMessageToCaller(new MeetingMessage { Code = 400, InternalCode = 10024, Message = "Failure" });
                return;
            }

            if (!peer.Consumers.TryGetValue(consumerId, out var consumer))
            {
                await SendMessageToCaller(new MeetingMessage { Code = 400, InternalCode = 10024, Message = "Failure" });
                return;
            }

            await consumer.PauseAsync();

            await SendMessageToCaller(new MeetingMessage { Code = 200, InternalCode = 10023, Message = "Success" });
        }

        public async Task ResumeConsumer(string consumerId)
        {
            var peer = Peer;
            if (!peer.Joined || peer.Room == null)
            {
                await SendMessageToCaller(new MeetingMessage { Code = 400, InternalCode = 10026, Message = "Failure" });
                return;
            }

            if (!peer.Consumers.TryGetValue(consumerId, out var consumer))
            {
                await SendMessageToCaller(new MeetingMessage { Code = 400, InternalCode = 10026, Message = "Failure" });
                return;
            }

            await consumer.ResumeAsync();

            await SendMessageToCaller(new MeetingMessage { Code = 200, InternalCode = 10025, Message = "Success" });
        }

        public async Task SetConsumerPreferedLayers(SetConsumerPreferedLayersRequest setConsumerPreferedLayersRequest)
        {
            var peer = Peer;
            if (!peer.Joined || peer.Room == null)
            {
                await SendMessageToCaller(new MeetingMessage { Code = 400, InternalCode = 10028, Message = "Failure" });
                return;
            }

            if (!peer.Consumers.TryGetValue(setConsumerPreferedLayersRequest.ConsumerId, out var consumer))
            {
                await SendMessageToCaller(new MeetingMessage { Code = 400, InternalCode = 10028, Message = "Failure" });
                return;
            }

            await consumer.SetPreferredLayersAsync(setConsumerPreferedLayersRequest);

            await SendMessageToCaller(new MeetingMessage { Code = 200, InternalCode = 10027, Message = "Success" });
        }

        public async Task SetConsumerPriority(SetConsumerPriorityRequest setConsumerPriorityRequest)
        {
            var peer = Peer;
            if (!peer.Joined || peer.Room == null)
            {
                await SendMessageToCaller(new MeetingMessage { Code = 400, InternalCode = 10030, Message = "Failure" });
                return;
            }

            if (!peer.Consumers.TryGetValue(setConsumerPriorityRequest.ConsumerId, out var consumer))
            {
                await SendMessageToCaller(new MeetingMessage { Code = 400, InternalCode = 10030, Message = "Failure" });
                return;
            }

            await consumer.SetPriorityAsync(setConsumerPriorityRequest.Priority);

            await SendMessageToCaller(new MeetingMessage { Code = 200, InternalCode = 10029, Message = "Success" });
        }

        public async Task RequestConsumerKeyFrame(string consumerId)
        {
            var peer = Peer;
            if (!peer.Joined || peer.Room == null)
            {
                await SendMessageToCaller(new MeetingMessage { Code = 400, InternalCode = 10032, Message = "Failure" });
                return;
            }

            if (!peer.Consumers.TryGetValue(consumerId, out var consumer))
            {
                await SendMessageToCaller(new MeetingMessage { Code = 400, InternalCode = 10032, Message = "Failure" });
                return;
            }

            await consumer.RequestKeyFrameAsync();

            await SendMessageToCaller(new MeetingMessage { Code = 200, InternalCode = 10031, Message = "Success" });
        }

        public async Task GetTransportStats(string transportId)
        {
            var peer = Peer;
            if (!peer.Joined || peer.Room == null)
            {
                await SendMessageToCaller(new MeetingMessage { Code = 400, InternalCode = 10034, Message = "Failure" });
                return;
            }

            if (!peer.Transports.TryGetValue(transportId, out var transport))
            {
                await SendMessageToCaller(new MeetingMessage { Code = 400, InternalCode = 10034, Message = "Failure" });
                return;
            }

            var status = await transport.GetStatsAsync();
            // TODO: (alby)考虑不进行反序列化
            var data = JsonConvert.DeserializeObject<TransportStat>(status);

            await SendMessageToCaller(new MeetingMessage { Code = 200, InternalCode = 10033, Message = "Success", Data = data });
        }

        public async Task GetProducerStats(string producerId)
        {
            var peer = Peer;
            if (!peer.Joined || peer.Room == null)
            {
                await SendMessageToCaller(new MeetingMessage { Code = 400, InternalCode = 10036, Message = "Failure" });
                return;
            }

            if (!peer.Producers.TryGetValue(producerId, out var producer))
            {
                await SendMessageToCaller(new MeetingMessage { Code = 400, InternalCode = 10036, Message = "Failure" });
                return;
            }

            var status = await producer.GetStatsAsync();
            // TODO: (alby)考虑不进行反序列化
            var data = JsonConvert.DeserializeObject<ProducerStat>(status);

            await SendMessageToCaller(new MeetingMessage { Code = 200, InternalCode = 10035, Message = "Success", Data = data });
        }

        public async Task GetConsumerStats(string consumerId)
        {
            var peer = Peer;
            if (!peer.Joined || peer.Room == null)
            {
                await SendMessageToCaller(new MeetingMessage { Code = 400, InternalCode = 10038, Message = "Failure" });
                return;
            }

            if (!peer.Consumers.TryGetValue(consumerId, out var consumer))
            {
                await SendMessageToCaller(new MeetingMessage { Code = 400, InternalCode = 10038, Message = "Failure" });
                return;
            }

            var status = await consumer.GetStatsAsync();
            // TODO: (alby)考虑不进行反序列化
            var data = JsonConvert.DeserializeObject<ConsumerStat>(status);

            await SendMessageToCaller(new MeetingMessage { Code = 200, InternalCode = 10037, Message = "Success", Data = data });
        }
    }
}
