using System;
using System.Collections.Generic;
using Tubumu.Core.Extensions;

namespace TubumuMeeting.Mediasoup
{
    /// <summary>
    /// Provides information on the capabilities of a codec within the RTP
    /// capabilities. The list of media codecs supported by mediasoup and their
    /// settings is defined in the supportedRtpCapabilities.ts file.
    ///
    /// Exactly one RtpCodecCapability will be present for each supported combination
    /// of parameters that requires a distinct value of preferredPayloadType. For
    /// example:
    ///
    /// - Multiple H264 codecs, each with their own distinct 'packetization-mode' and
    ///   'profile-level-id' values.
    /// - Multiple VP9 codecs, each with their own distinct 'profile-id' value.
    ///
    /// RtpCodecCapability entries in the mediaCodecs array of RouterOptions do not
    /// require preferredPayloadType field (if unset, mediasoup will choose a random
    /// one). If given, make sure it's in the 96-127 range.
    /// </summary>
    public class RtpCodecCapability
    {
        /// <summary>
        /// Media kind.
        /// </summary>
        public MediaKind Kind { get; set; }

        /// <summary>
        /// The codec MIME media type/subtype (e.g. 'audio/opus', 'video/VP8').
        /// </summary>
        public string MimeType { get; set; }

        /// <summary>
        /// The preferred RTP payload type.
        /// </summary>
        public int? PreferredPayloadType { get; set; }

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
        /// Codec specific parameters. Some parameters (such as 'packetization-mode'
        /// and 'profile-level-id' in H264 or 'profile-id' in VP9) are critical for
        /// codec matching.
        /// </summary>
        public IDictionary<string, object> Parameters { get; set; }

        /// <summary>
        /// Transport layer and codec-specific feedback messages for this codec.
        /// </summary>
        public RtcpFeedback[]? RtcpFeedback { get; set; }
    }
}
