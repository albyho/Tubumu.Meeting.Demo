using System.Collections.Generic;

namespace TubumuMeeting.Mediasoup
{
    public class SendMessageRequest
    {
        public string? RoomId { get; set; }

        public string Message { get; set; }
    }
}
