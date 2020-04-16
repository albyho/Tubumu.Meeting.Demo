using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using TubumuMeeting.Mediasoup.Extensions;

namespace TubumuMeeting.Meeting
{
    /// <summary>
    /// MeetingMessage (错误码：200 普通消息 201 连接通知成功 202 加入房间成功 203 加入房间失败 400 连接通知失败等错误)
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
            var userId = int.Parse(Context.User.Identity.Name);
            var handleResult = _meetingManager.HandlePeer(userId, "Guest");
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
        public async Task JoinRoom(Guid roomId)
        {
            var room = _meetingManager.GetOrCreateRoom(roomId, "Meeting");
            var relateRessult = await _meetingManager.RoomRelateRouter(room.RoomId);

            var peerId = int.Parse(Context.User.Identity.Name);
            var joinRessult = _meetingManager.PeerJoinRoom(peerId, roomId);
            if (joinRessult)
            {
                await SendMessageToCaller(new MeetingMessage { Code = 202, Message = "加入房间成功" });
                return;
            }

            await SendMessageToCaller(new MeetingMessage { Code = 203, Message = "加入房间失败" });
        }
    }
}
