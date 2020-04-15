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
    /// MeetingMessage (错误码：200 连接通知成功 400 连接通知失败等错误)
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

        public MeetingHub(ILogger<MeetingHub> logger)
        {
            _logger = logger;
        }

        public override Task OnConnectedAsync()
        {
            // return SendMessageToCaller(new ApiResultNotification { Code = 200, Message = "连接通知成功" });
            var userId = int.Parse(Context.User.Identity.Name);
            return SendMessageByUserIdAsync(userId, new MeetingMessage
            {
                Code = 200,
                Message = "welcome",
            });
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
        public Task SendMessage(string roomId)
        {
            return BroadcastMessageAsync(new MeetingMessage
            {
                Code = 200,
                Message = "new user joined.",
            });
        }
    }
}
