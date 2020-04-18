using System;
using System.Collections.Generic;
using System.Text;

namespace TubumuMeeting.Mediasoup
{
    public class CreateWebRtcTransportResult
    {
        public string Id { get; set; }

        public IceParameters IceParameters { get; set; }

        public IceCandidate[] IceCandidates { get; set; }

        public DtlsParameters DtlsParameters { get; set; }
    }
}
