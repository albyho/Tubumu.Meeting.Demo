using System.Collections.Generic;

namespace TubumuMeeting.Mediasoup
{
    public class ProduceRequest
    {
        public string TransportId { get; set; }

        public MediaKind Kind { get; set; }

        public RtpParameters RtpParameters { get; set; }

        public string Source { get; set; }

        public Dictionary<string, object> AppData { get; set; }
    }
}
