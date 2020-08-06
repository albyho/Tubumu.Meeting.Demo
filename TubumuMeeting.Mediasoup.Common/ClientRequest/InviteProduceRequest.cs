using System;
using System.Collections.Generic;

namespace TubumuMeeting.Mediasoup
{
    public class InviteProduceRequest
    {
        public string RoomId { get; set; }

        public string PeerId { get; set; }

        public string[] Sources { get; set; }

        public Dictionary<string, object>? AppData { get; set; }
    }
}
