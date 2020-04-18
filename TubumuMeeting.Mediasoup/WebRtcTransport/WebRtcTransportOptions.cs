using System.Collections.Generic;

namespace TubumuMeeting.Mediasoup
{
    public class WebRtcTransportOptions
    {
        /// <summary>
        /// Listening IP address or addresses in order of preference (first one is the
        /// preferred one).
        /// </summary>
        public TransportListenIp[] ListenIps { get; set; }

        /// <summary>
        /// Listen in UDP. Default true.
        /// </summary>
        public bool? EnableUdp { get; set; } = true;

        /// <summary>
        /// Listen in TCP. Default false.
        /// </summary>
        public bool? EnableTcp { get; set; } = false;

        /// <summary>
        /// Prefer UDP. Default false.
        /// </summary>
        public bool? PreferUdp { get; set; } = false;

        /// <summary>
        /// Prefer TCP. Default false.
        /// </summary>
        public bool? PreferTcp { get; set; } = false;

        /// <summary>
        /// Initial available outgoing bitrate (in bps). Default 600000.
        /// </summary>
        public int? InitialAvailableOutgoingBitrate { get; set; } = 6000000;

        /// <summary>
        /// Create a SCTP association. Default false.
        /// </summary>
        public bool? EnableSctp { get; set; } = false;

        /// <summary>
        /// SCTP streams number.
        /// </summary>
        public NumSctpStreams? NumSctpStreams { get; set; } = new NumSctpStreams { OS = 1024, MIS = 1024 };

        /// <summary>
        /// Maximum size of data that can be passed to DataProducer's send() method.
        /// Default 262144.
        /// </summary>
        public int? MaxSctpMessageSize = 262144;

        /// <summary>
        /// Custom application data.
        /// </summary>
        public Dictionary<string, object>? AppData { get; set; }

    }
}
