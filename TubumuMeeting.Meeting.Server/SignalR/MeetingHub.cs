using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using TubumuMeeting.Mediasoup;
using TubumuMeeting.Mediasoup.Extensions;

namespace TubumuMeeting.Meeting.Server
{
    /// <summary>
    /// MeetingMessage (错误码：200 普通消息 201 连接通知成功  202 进入房间成功 203 进入房间失败 400 连接通知失败等错误)
    /// </summary>
    public class MeetingMessage
    {
        public int Code { get; set; } = 200;

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
                return SendMessageToCaller(new MeetingMessage { Code = 201, Message = "连接成功" });
            }
            return Task.CompletedTask;
        }

        public override Task OnDisconnectedAsync(Exception exception)
        {
            return base.OnDisconnectedAsync(exception);
        }

        public int UserId => int.Parse(Context.User.Identity.Name);
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
        public Task GetRouterRtpCapabilities()
        {
            var room = _meetingManager.Peers[UserId]?.Room;
            if (room != null)
            {
                var rtpCapabilities = room.Router.RtpCapabilities;
                return SendMessageToCaller(new MeetingMessage { Code = 203, Message = "Success", Data = rtpCapabilities });
            }

            return SendMessageToCaller(new MeetingMessage { Code = 204, Message = "Failure" });
        }

        public Task Join(RtpCapabilities rtpCapabilities)
        {
            var peer = _meetingManager.Peers[UserId];
            if (!peer.Joined)
            {
                _meetingManager.Peers[UserId].RtpCapabilities = rtpCapabilities;
                _meetingManager.Peers[UserId].Joined = true;

                return SendMessageToCaller(new MeetingMessage { Code = 205, Message = "Success" });
            }

            return SendMessageToCaller(new MeetingMessage { Code = 206, Message = "Failure" });
        }

        public async Task EnterRoom(Guid roomId)
        {
            // TODO: (alby)代码清理
            var room = _meetingManager.GetOrCreateRoom(roomId, "Meeting");

            var peer = _meetingManager.Peers[UserId];
            if (!peer.Joined)
            {
                await SendMessageToCaller(new MeetingMessage { Code = 208, Message = "Failure" });
                return;
            }

            var joinRessult = await _meetingManager.PeerEnterRoomAsync(UserId, roomId);
            if (joinRessult)
            {
                await SendMessageToCaller(new MeetingMessage { Code = 207, Message = "Success" });
                return;
            }

            await SendMessageToCaller(new MeetingMessage { Code = 208, Message = "Failure" });

            // Notify the new Peer to all other Peers.
        }

        public async Task CreateWebRtcTransport(CreateWebRtcTransportParameters createWebRtcTransportParameters)
        {
            var peer = _meetingManager.Peers[UserId];
            if (!peer.Joined || peer.Room == null)
            {
                await SendMessageToCaller(new MeetingMessage { Code = 210, Message = "Failure" });
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
                await SendMessageToCaller(new MeetingMessage { Code = 210, Message = "Failure" });
                return;
            }

            await SendMessageToCaller(new MeetingMessage
            {
                Code = 209,
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

        public void ConnectWebRtcTransport()
        {

        }

        public void RestartIce()
        {

        }

        public void Produce()
        {

        }

        public void CloseProducer()
        {

        }

        public void PauseProducer()
        {

        }

        public void ResumeProducer()
        {

        }

        public void PauseConsumer()
        {

        }

        public void ResumeConsumer()
        {

        }

        public void SetConsumerPreferedLayers()
        {

        }

        public void SetConsumerPriority()
        {

        }

        public void RequestConsumerKeyFrame()
        {

        }

        public void GetTransportStats()
        {

        }

        public void GetProducerStats()
        {

        }

        public void GetConsumerStats()
        {

        }
    }
}
