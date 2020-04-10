using System;
using System.Collections.Generic;
using Tubumu.Core.Extensions;

namespace TubumuMeeting.Mediasoup
{
    /// <summary>
    /// Provides information on codec settings within the RTP parameters. The list
    /// of media codecs supported by mediasoup and their settings is defined in the
    /// supportedRtpCapabilities.ts file.
    /// </summary>
    public class RtpCodecParameters : IEquatable<RtpCodecParameters>
    {
        /// <summary>
        /// The codec MIME media type/subtype (e.g. 'audio/opus', 'video/VP8').
        /// </summary>
        public string MimeType { get; set; }

        /// <summary>
        /// The value that goes in the RTP Payload Type Field. Must be unique.
        /// </summary>
        public int PayloadType { get; set; }

        /// <summary>
        /// Codec clock rate expressed in Hertz.
        /// </summary>
        public int ClockRate { get; set; }

        /// <summary>
        /// The number of channels supported (e.g. two for stereo). Just for audio.
        /// Default 1.
        /// </summary>
        public int? Channels { get; set; } = 1;

        /// <summary>
        /// Codec-specific parameters available for signaling. Some parameters (such
        /// as 'packetization-mode' and 'profile-level-id' in H264 or 'profile-id' in
        /// VP9) are critical for codec matching.
        /// </summary>
        public IDictionary<string, object> Parameters { get; set; }

        /// <summary>
        /// Transport layer and codec-specific feedback messages for this codec.
        /// </summary>
        public RtcpFeedback[]? RtcpFeedback { get; set; }

        public bool Equals(RtpCodecParameters other)
        {
            if (other == null) return false;

            return (MimeType == other.MimeType)
                && (PayloadType == other.PayloadType)
                && (ClockRate == other.ClockRate);
        }

        public override bool Equals(object other)
        {
            if (other is RtpCodecParameters)
                return Equals((RtpCodecParameters)other);
            else
                return false;
        }

        public override int GetHashCode()
        {
            return MimeType.GetHashCode() ^ PayloadType.GetHashCode() ^ ClockRate.GetHashCode();
        }
    }
}
