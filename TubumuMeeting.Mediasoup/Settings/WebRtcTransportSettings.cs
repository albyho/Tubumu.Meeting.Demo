using System.Collections.Generic;

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
