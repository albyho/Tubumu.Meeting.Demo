using System;
using System.Collections.Generic;
using System.Text;

namespace TubumuMeeting.Mediasoup
{
    public class CreateWebRtcTransportParameters
    {
        public bool ForceTcp { get; set; }

        public bool Producing { get; set; }

        public bool Consuming { get; set; }

        public SctpCapabilities? SctpCapabilities { get; set; }
    }
}
