using System.Collections.Generic;
using TubumuMeeting.Mediasoup;

namespace TubumuMeeting.Meeting.Server
{
    public class ConsumeInfo
    {
        public string RoomId { get; set; }

        public string ProducerPeerId { get; set; }

        public MediaKind Kind { get; set; }

        public string ProducerId { get; set; }

        public string ConsumerId { get; set; }

        public RtpParameters RtpParameters { get; set; }

        public ConsumerType Type { get; set; }

        public Dictionary<string, object>? ProducerAppData { get; set; }

        public bool ProducerPaused { get; set; }
    }
}
