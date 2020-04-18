using System.Collections.Generic;

namespace TubumuMeeting.Mediasoup
{
    public class WebRtcTransportSettings
    {
        public TransportListenIp[] ListenIps { get; set; }

        public int InitialAvailableOutgoingBitrate { get; set; }

        public int MinimumAvailableOutgoingBitrate { get; set; } // TODO: (alby)貌似没有地方使用该参数

        public int? MaximumIncomingBitrate { get; set; }
    }
}
