using System;
using System.Collections.Generic;
using System.Text;

namespace TubumuMeeting.Mediasoup
{
    public class JoinRequest
    {
        public RtpCapabilities RtpCapabilities { get; set; }

        public SctpCapabilities? SctpCapabilities { get; set; }
    }
}
