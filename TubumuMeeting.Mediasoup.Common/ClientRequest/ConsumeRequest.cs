using System;
using System.Collections.Generic;

namespace TubumuMeeting.Mediasoup
{
    public class ConsumeRequest
    {
        public Guid RoomId { get; set; }

        public string PeerId { get; set; }

        public string[] Sources { get; set; }
    }
}
