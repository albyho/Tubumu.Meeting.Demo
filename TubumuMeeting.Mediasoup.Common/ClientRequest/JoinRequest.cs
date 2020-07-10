using System;
using System.Collections.Generic;

namespace TubumuMeeting.Mediasoup
{
    public class JoinRequest
    {
        public RtpCapabilities RtpCapabilities { get; set; }

        public SctpCapabilities? SctpCapabilities { get; set; }

        public string[]? Sources { get; set; }

        public Dictionary<string, object>? DeviceInfo { get; set; }

        public Guid GroupId { get; set; }
    }
}
