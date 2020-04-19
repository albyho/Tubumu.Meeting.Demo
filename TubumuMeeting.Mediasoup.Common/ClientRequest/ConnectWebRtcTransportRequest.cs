using System;
using System.Collections.Generic;
using System.Text;

namespace TubumuMeeting.Mediasoup
{
    public class ConnectWebRtcTransportRequest
    {
        public string TransportId { get; set; }

        public DtlsParameters DtlsParameters { get; set; }
    }
}
