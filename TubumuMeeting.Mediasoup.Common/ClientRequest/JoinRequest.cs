using System;

namespace TubumuMeeting.Mediasoup
{
    public class JoinRequest
    {
        public RtpCapabilities RtpCapabilities { get; set; }

        public SctpCapabilities? SctpCapabilities { get; set; }

        public Guid GroupId { get; set; }
    }
}
