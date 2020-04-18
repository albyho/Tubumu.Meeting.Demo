using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using TubumuMeeting.Mediasoup;

namespace TubumuMeeting.Meeting
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
        private readonly MeetingManager _meetingManager;

        public MeetingHub(ILogger<MeetingHub> logger, MeetingManager meetingManager)
        {
            _logger = logger;
            _meetingManager = meetingManager;
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
            var joinRessult = await _meetingManager.PeerEnterRoomAsync(UserId, roomId);
            if (joinRessult)
            {
                await SendMessageToCaller(new MeetingMessage { Code = 207, Message = "Success" });
                return;
            }

            await SendMessageToCaller(new MeetingMessage { Code = 208, Message = "Failure" });

            // Notify the new Peer to all other Peers.
        }

        public void CreateWebRtcTransport()
        {

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
