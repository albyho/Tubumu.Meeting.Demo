using System;
using System.Collections.Generic;
using System.Text;

namespace TubumuMeeting.Mediasoup
{
    public class WebRtcTransportSettings
    {
        public IEnumerable<TransportListenIp> ListenIps { get; set; }

        public int InitialAvailableOutgoingBitrate { get; set; }

        public int MinimumAvailableOutgoingBitrate { get; set; }

        public int MaximumIncomingBitrate { get; set; }
    }
}
