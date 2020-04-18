using System;
using System.Collections.Generic;

namespace TubumuMeeting.Mediasoup
{
    /// <summary>
    /// The RTP capabilities define what mediasoup or an endpoint can receive at
    /// media level.
    /// </summary>
    [Serializable]
    public class RtpCapabilities
    {
        /// <summary>
        /// Supported media and RTX codecs.
        /// </summary>
        public List<RtpCodecCapability>? Codecs { get; set; }

        /// <summary>
        /// Supported RTP header extensions.
        /// </summary>
        public RtpHeaderExtension[]? HeaderExtensions { get; set; }

        /// <summary>
        /// Supported FEC mechanisms.
        /// </summary>
        public string[]? FecMechanisms { get; set; }
    }
}
