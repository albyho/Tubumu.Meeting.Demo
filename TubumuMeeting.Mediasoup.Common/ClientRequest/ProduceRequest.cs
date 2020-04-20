using System;
using System.Collections.Generic;
using System.Text;

namespace TubumuMeeting.Mediasoup
{
    public class ProduceRequest
    {
        public string TransportId { get; set; }

        public MediaKind Kind { get; set; }

        public RtpParameters RtpParameters { get; set; }

        public Dictionary<string, object>? AppData { get; set; }
    }
}
