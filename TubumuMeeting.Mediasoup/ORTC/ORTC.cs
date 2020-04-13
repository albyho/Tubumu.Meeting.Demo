using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Tubumu.Core.Extensions;
using Tubumu.Core.Extensions.Object;
using TubumuMeeting.Mediasoup;

namespace TubumuMeeting.Mediasoup
{
    public static class ORTC
    {
        private static readonly Regex MimeTypeRegex = new Regex(@"^(audio|video)/(.+)");
        private static readonly Regex RtxMimeTypeRegex = new Regex(@"^.+/rtx$");

        public static readonly int[] DynamicPayloadTypes = new[] {
            100, 101, 102, 103, 104, 105, 106, 107, 108, 109, 110,
            111, 112, 113, 114, 115, 116, 117, 118, 119, 120, 121,
            122, 123, 124, 125, 126, 127, 96, 97, 98, 99 };

        /// <summary>
        /// Validates RtpCapabilities. It may modify given data by adding missing
        /// fields with default values.
        /// It throws if invalid.
        /// </summary>
        public static void ValidateRtpCapabilities(RtpCapabilities caps)
        {
            foreach (var codec in caps.Codecs!)
            {
                ValidateRtpCodecCapability(codec);
            }

            // headerExtensions is optional. If unset, fill with an empty array.
            if (caps.HeaderExtensions == null)
            {
                caps.HeaderExtensions = new RtpHeaderExtension[0];
            }

            foreach (var ext in caps.HeaderExtensions)
            {
                ValidateRtpHeaderExtension(ext);
            }
        }

        /// <summary>
        /// Validates RtpCodecCapability. It may modify given data by adding missing
        /// fields with default values.
        /// It throws if invalid.
        /// </summary>
        public static void ValidateRtpCodecCapability(RtpCodecCapability codec)
        {
            // mimeType is mandatory.
            if (codec.MimeType.IsNullOrWhiteSpace())
            {
                throw new ArgumentException(nameof(codec.MimeType));
            }

            var mimeType = codec.MimeType.ToLower();
            if (!MimeTypeRegex.IsMatch(mimeType))
            {
                throw new ArgumentException(nameof(codec.MimeType));
            }

            // Just override kind with media component in mimeType.
            codec.Kind = mimeType.ToLower().StartsWith("video") ? MediaKind.Video : MediaKind.Audio;

            // preferredPayloadType is optional.

            // clockRate is mandatory.

            // channels is optional. If unset, set it to 1 (just if audio).
            if (codec.Kind == MediaKind.Audio && (!codec.Channels.HasValue || codec.Channels < 1))
            {
                codec.Channels = 1;
            }

            // parameters is optional. If unset, set it to an empty object.
            // TODO: (alby)强类型无法转空对象
            if (codec.Parameters == null)
            {
                codec.Parameters = new Dictionary<string, object>();
            }

            foreach (var item in codec.Parameters)
            {
                var key = item.Key;
                var value = item.Value;
                if (value == null)
                {
                    codec.Parameters[item.Key] = "";
                    value = "";
                }

                if (!value.IsStringType() && !value.IsNumericType())
                {
                    throw new ArgumentOutOfRangeException($"invalid codec parameter[key:${ key }, value:${ value}]");
                }
                if (key == "apt" && !value.IsNumericType())
                {
                    throw new ArgumentOutOfRangeException("invalid codec apt parameter");
                }
            }

            // rtcpFeedback is optional. If unset, set it to an empty array.
            if (codec.RtcpFeedback == null)
            {
                codec.RtcpFeedback = new RtcpFeedback[0];
            }

            foreach (var fb in codec.RtcpFeedback)
            {
                ValidateRtcpFeedback(fb);
            }
        }

        /// <summary>
        /// Validates RtcpFeedback. It may modify given data by adding missing
        /// fields with default values.
        /// It throws if invalid.
        /// </summary>
        public static void ValidateRtcpFeedback(RtcpFeedback fb)
        {
            // type is mandatory.
            if (fb.Type.IsNullOrWhiteSpace())
                throw new ArgumentException(nameof(fb.Type));

            // parameter is optional. If unset set it to an empty string.
            if (fb.Parameter.IsNullOrWhiteSpace())
                fb.Parameter = "";
        }

        /// <summary>
        /// Validates RtpHeaderExtension. It may modify given data by adding missing
        /// fields with default values.
        /// It throws if invalid.
        /// </summary>
        public static void ValidateRtpHeaderExtension(RtpHeaderExtension ext)
        {
            // kind is optional. If unset set it to an empty string.
            // TODO: (alby)枚举null但非空字符串

            // uri is mandatory.
            if (ext.Uri.IsNullOrEmpty())
                throw new ArgumentException(nameof(ext.Uri));

            // preferredId is mandatory.

            // preferredEncrypt is optional. If unset set it to false.

            // direction is optional. If unset set it to sendrecv.
            if (!ext.Direction.HasValue)
                ext.Direction = RtpHeaderExtensionDirection.SendReceive;
        }

        /// <summary>
        /// Validates RtpParameters. It may modify given data by adding missing
        /// fields with default values.
        /// It throws if invalid.
        /// </summary>
        public static void ValidateRtpParameters(RtpParameters parameters)
        {
            // mid is optional.

            // codecs is mandatory.

            foreach (var codec in parameters.Codecs)
            {
                ValidateRtpCodecParameters(codec);
            }

            // headerExtensions is optional. If unset, fill with an empty array.
            if (parameters.HeaderExtensions == null)
            {
                parameters.HeaderExtensions = new List<RtpHeaderExtensionParameters>();
            }

            foreach (var ext in parameters.HeaderExtensions)
            {
                ValidateRtpHeaderExtensionParameters(ext);
            }

            // encodings is optional. If unset, fill with an empty array.
            if (parameters.Encodings == null)
            {
                parameters.Encodings = new List<RtpEncodingParameters>();
            }

            foreach (var encoding in parameters.Encodings)
            {
                ValidateRtpEncodingParameters(encoding);
            }

            // rtcp is optional. If unset, fill with an empty object.
            // TODO: (alby)强类型无法转空对象
            if (parameters != null)
            {
                ValidateRtcpParameters(parameters.Rtcp);
            }
        }

        /// <summary>
        /// Validates RtpCodecParameters. It may modify given data by adding missing
        /// fields with default values.
        /// It throws if invalid.
        /// </summary>
        public static void ValidateRtpCodecParameters(RtpCodecParameters codec)
        {
            // mimeType is mandatory.
            if (codec.MimeType.IsNullOrWhiteSpace())
            {
                throw new ArgumentException(nameof(codec.MimeType));
            }

            var mimeType = codec.MimeType.ToLower();
            if (!MimeTypeRegex.IsMatch(mimeType))
            {
                throw new ArgumentException(nameof(codec.MimeType));
            }

            // payloadType is mandatory.

            // clockRate is mandatory.

            // channels is optional. If unset, set it to 1 (just if audio).
            if (mimeType.StartsWith("Video"))
            {
                codec.Channels = 1;
            }

            // parameters is optional. If unset, set it to an empty object.
            // TODO: (alby)强类型无法转空对象
            if (codec.Parameters == null)
            {
                codec.Parameters = new Dictionary<string, object>();
            }

            foreach (var item in codec.Parameters)
            {
                var key = item.Key;
                var value = item.Value;
                if (value == null)
                {
                    codec.Parameters[item.Key] = "";
                    value = "";
                }

                if (!value.IsStringType() && !value.IsNumericType())
                {
                    throw new ArgumentOutOfRangeException($"invalid codec parameter[key:${ key }, value:${ value}]");
                }
                if (key == "apt" && !value.IsNumericType())
                {
                    throw new ArgumentOutOfRangeException("invalid codec apt parameter");
                }
            }

            // rtcpFeedback is optional. If unset, set it to an empty array.
            if (codec.RtcpFeedback == null)
            {
                codec.RtcpFeedback = new RtcpFeedback[0];
            }

            foreach (var fb in codec.RtcpFeedback)
            {
                ValidateRtcpFeedback(fb);
            }
        }

        /// <summary>
        /// Validates RtpHeaderExtensionParameteters. It may modify given data by adding missing
        /// fields with default values.
        /// It throws if invalid.
        /// </summary>
        public static void ValidateRtpHeaderExtensionParameters(RtpHeaderExtensionParameters ext)
        {
            // uri is mandatory.
            if (ext.Uri.IsNullOrEmpty())
                throw new ArgumentException(nameof(ext.Uri));

            // id is mandatory.

            // encrypt is optional. If unset set it to false.
            if (!ext.Encrypt.HasValue)
            {
                ext.Encrypt = false;
            }

            // parameters is optional. If unset, set it to an empty object.
            // TODO: (alby)强类型无法转空对象
            if (ext.Parameters == null)
            {
                ext.Parameters = new Dictionary<string, object>();
            }

            foreach (var item in ext.Parameters)
            {
                var key = item.Key;
                var value = item.Value;

                if (value == null)
                {
                    ext.Parameters[item.Key] = "";
                    value = "";
                }

                if (!value.IsStringType() && !value.IsNumericType())
                {
                    throw new ArgumentOutOfRangeException($"invalid codec parameter[key:${ key }, value:${ value}]");
                }
            }
        }

        /// <summary>
        /// Validates RtpEncodingParameters. It may modify given data by adding missing
        /// fields with default values.
        /// It throws if invalid.
        /// </summary>
        public static void ValidateRtpEncodingParameters(RtpEncodingParameters encoding)
        {
            // ssrc is optional.

            // rid is optional.

            // rtx is optional.

            // dtx is optional. If unset set it to false.
            if (!encoding.Dtx.HasValue)
            {
                encoding.Dtx = false;
            }

            // scalabilityMode is optional.
        }

        /// <summary>
        /// Validates RtcpParameters. It may modify given data by adding missing
        /// fields with default values.
        /// It throws if invalid.
        /// </summary>
        public static void ValidateRtcpParameters(RtcpParameters rtcp)
        {
            // cname is optional.

            // reducedSize is optional. If unset set it to true.
            if (!rtcp.ReducedSize.HasValue)
                rtcp.ReducedSize = true;
        }

        /// <summary>
        /// Validates SctpCapabilities. It may modify given data by adding missing
        /// fields with default values.
        /// It throws if invalid.
        /// </summary>
        public static void ValidateSctpCapabilities(SctpCapabilities caps)
        {
            // numStreams is mandatory.
            if (caps.NumStreams == null)
            {
                throw new ArgumentException(nameof(caps.NumStreams));
            }


            ValidateNumSctpStreams(caps.NumStreams);
        }

        /// <summary>
        /// Validates NumSctpStreams. It may modify given data by adding missing
        /// fields with default values.
        /// It throws if invalid.
        /// </summary>
        public static void ValidateNumSctpStreams(NumSctpStreams numStreams)
        {
            // OS is mandatory.

            // MIS is mandatory.
        }

        /// <summary>
        /// Validates SctpParameters. It may modify given data by adding missing
        /// fields with default values.
        /// It throws if invalid.
        /// </summary>
        public static void ValidateSctpParameters(SctpParameters parameters)
        {
            // port is mandatory.

            // OS is mandatory.

            // MIS is mandatory.

            // maxMessageSize is mandatory.
        }

        /// <summary>
        /// Validates SctpStreamParameters. It may modify given data by adding missing
        /// fields with default values.
        /// It throws if invalid.
        /// </summary>
        public static void ValidateSctpStreamParameters(SctpStreamParameters parameters)
        {
            // streamId is mandatory.

            // ordered is optional.
            var orderedGiven = true;
            if (!parameters.Ordered.HasValue)
            {
                parameters.Ordered = true;
                orderedGiven = false;
            }

            // maxPacketLifeTime is optional.

            // maxRetransmits is optional.

            if (parameters.MaxPacketLifeTime.HasValue && parameters.MaxRetransmits.HasValue)
            {
                throw new ArgumentException("cannot provide both maxPacketLifeTime and maxRetransmits");
            }

            if (orderedGiven &&
                parameters.Ordered.Value &&
                (parameters.MaxPacketLifeTime.HasValue || parameters.MaxRetransmits.HasValue)
                )
            {
                throw new ArgumentException("cannot be ordered with maxPacketLifeTime or maxRetransmits");
            }
            else if (!orderedGiven && (parameters.MaxPacketLifeTime.HasValue || parameters.MaxRetransmits.HasValue))
            {
                parameters.Ordered = false;
            }
        }

        /// <summary>
        /// Generate RTP capabilities for the Router based on the given media codecs and
        /// mediasoup supported RTP capabilities.
        /// </summary>
        public static RtpCapabilities GenerateRouterRtpCapabilities(params RtpCodecCapability[] mediaCodecs)
        {
            // Normalize supported RTP capabilities.
            ValidateRtpCapabilities(RtpCapabilities.SupportedRtpCapabilities);

            var clonedSupportedRtpCapabilities = RtpCapabilities.SupportedRtpCapabilities.DeepClone<RtpCapabilities>();
            var dynamicPayloadTypes = DynamicPayloadTypes.DeepClone<int[]>().ToList();
            var caps = new RtpCapabilities
            {
                Codecs = new List<RtpCodecCapability>(),
                HeaderExtensions = clonedSupportedRtpCapabilities.HeaderExtensions
            };

            foreach (var mediaCodec in mediaCodecs)
            {
                // This may throw.
                ValidateRtpCodecCapability(mediaCodec);

                var matchedSupportedCodec = clonedSupportedRtpCapabilities.Codecs
                    .Where(supportedCodec => true /*MatchCodecs(mediaCodec, supportedCodec, { strict: false })*/).ToArray();

                if (matchedSupportedCodec.IsNullOrEmpty())
                {
                    throw new Exception($"media codec not supported[mimeType:${ mediaCodec.MimeType }]");
                }

                // Clone the supported codec.
                var codec = matchedSupportedCodec.DeepClone<RtpCodecCapability>();

                // If the given media codec has preferredPayloadType, keep it.
                if (mediaCodec.PreferredPayloadType.HasValue)
                {
                    codec.PreferredPayloadType = mediaCodec.PreferredPayloadType;

                    // Also remove the pt from the list in available dynamic values.
                    dynamicPayloadTypes.Remove(codec.PreferredPayloadType.Value);
                }
                // Otherwise if the supported codec has preferredPayloadType, use it.
                else if (codec.PreferredPayloadType.HasValue)
                {
                    // No need to remove it from the list since it's not a dynamic value.
                }
                // Otherwise choose a dynamic one.
                else
                {
                    // Take the first available pt and remove it from the list.
                    var pt = dynamicPayloadTypes.FirstOrDefault();

                    if (pt == 0)
                        throw new Exception("cannot allocate more dynamic codec payload types");

                    dynamicPayloadTypes.RemoveAt(0);

                    codec.PreferredPayloadType = pt;
                }

                // Ensure there is not duplicated preferredPayloadType values.
                if (caps.Codecs.Count(c => c.PreferredPayloadType == codec.PreferredPayloadType) > 2)
                    throw new Exception("duplicated codec.preferredPayloadType");

                // Merge the media codec parameters.
                codec.Parameters = codec.Parameters.Merge(mediaCodec.Parameters);

                // Append to the codec list.
                caps.Codecs.Add(codec);

                // Add a RTX video codec if video.
                if (codec.Kind == MediaKind.Video)
                {
                    // Take the first available pt and remove it from the list.
                    var pt = dynamicPayloadTypes.FirstOrDefault();

                    if (pt == 0)
                        throw new Exception("cannot allocate more dynamic codec payload types");

                    dynamicPayloadTypes.RemoveAt(0);

                    var rtxCodec = new RtpCodecCapability
                    {
                        Kind = codec.Kind,
                        MimeType = $"{codec.Kind.GetEnumStringValue()}/rtx",
                        PreferredPayloadType = pt,
                        ClockRate = codec.ClockRate,
                        Parameters = new Dictionary<string, object>
                        {
                            { "apt",codec.PreferredPayloadType}
                        },
                        RtcpFeedback = new RtcpFeedback[0],

                    };

                    // Append to the codec list.
                    caps.Codecs.Add(rtxCodec);
                }
            }

            return caps;
        }

        /// <summary>
        /// Get a mapping in codec payloads and encodings in the given Producer RTP
        /// parameters as values expected by the Router.
        ///
        /// It may throw if invalid or non supported RTP parameters are given.
        /// </summary>
        public static RtpMapping GetProducerRtpParametersMapping(RtpParameters parameters, RtpCapabilities caps)
        {
            var rtpMapping = new RtpMapping
            {
                Codecs = new List<RtpMappingCodec>(),
                Encodings = new List<RtpMappingEncoding>()
            };

            // Match parameters media codecs to capabilities media codecs.
            var codecToCapCodec = new Dictionary<RtpCodecParameters, RtpCodecCapability>();

            foreach (var codec in parameters.Codecs)
            {
                if (IsRtxMimeType(codec.MimeType))
                    continue;

                // Search for the same media codec in capabilities.
                var matchedCapCodec = caps.Codecs
                    .Where(capCodec => MatchCodecs(codec, capCodec, true, true))
                    .FirstOrDefault();

                if (matchedCapCodec == null)
                {
                    throw new Exception($"unsupported codec[mimeType:{ codec.MimeType }, payloadType:{ codec.PayloadType }]");
                }
                codecToCapCodec[codec] = matchedCapCodec;
            }

            // Match parameters RTX codecs to capabilities RTX codecs.
            foreach (var codec in parameters.Codecs)
            {
                if (!IsRtxMimeType(codec.MimeType))
                    continue;

                // Search for the associated media codec.
                var associatedMediaCodec = parameters.Codecs
                    .Where(mediaCodec => MatchCodecsWithPayloadTypeAndApt(mediaCodec.PayloadType, codec.Parameters))
                    .FirstOrDefault();

                if (associatedMediaCodec == null)
                {
                    throw new Exception($"missing media codec found for RTX PT { codec.PayloadType}");
                }

                var capMediaCodec = codecToCapCodec[associatedMediaCodec];

                // Ensure that the capabilities media codec has a RTX codec.
                var associatedCapRtxCodec = caps.Codecs
                    .Where(capCodec => IsRtxMimeType(capCodec.MimeType) && MatchCodecsWithPayloadTypeAndApt(capMediaCodec.PreferredPayloadType.Value, capCodec.Parameters))
                    .FirstOrDefault();

                if (associatedCapRtxCodec == null)
                {
                    throw new Exception($"no RTX codec for capability codec PT { capMediaCodec.PreferredPayloadType}");
                }

                codecToCapCodec[codec] = associatedCapRtxCodec;
            }

            // Generate codecs mapping.
            foreach (var item in codecToCapCodec)
            {
                rtpMapping.Codecs.Add(new RtpMappingCodec
                {
                    PayloadType = item.Key.PayloadType,
                    MappedPayloadType = item.Value.PreferredPayloadType.Value,
                });
            };

            // Generate encodings mapping.
            var mappedSsrc = Utils.GenerateRandomNumber();

            foreach (var encoding in parameters.Encodings)
            {
                var mappedEncoding = new RtpMappingEncoding
                {
                    MappedSsrc = mappedSsrc++,
                    Rid = encoding.Rid,
                    Ssrc = encoding.Ssrc,
                    ScalabilityMode = encoding.ScalabilityMode,
                };

                rtpMapping.Encodings.Add(mappedEncoding);
            }

            return rtpMapping;
        }

        /// <summary>
        /// Generate RTP parameters to be internally used by Consumers given the RTP
        /// parameters in a Producer and the RTP capabilities in the Router.
        /// </summary>
        public static RtpParameters GetConsumableRtpParameters(MediaKind kind, RtpParameters parameters, RtpCapabilities caps, RtpMapping rtpMapping)
        {
            var consumableParams = new RtpParameters
            {
                Codecs = new List<RtpCodecParameters>(),
                HeaderExtensions = new List<RtpHeaderExtensionParameters>(),
                Encodings = new List<RtpEncodingParameters>(),
                Rtcp = new RtcpParameters(),
            };

            foreach (var codec in parameters.Codecs)
            {
                if (IsRtxMimeType(codec.MimeType))
                    continue;

                var consumableCodecPt = rtpMapping.Codecs
                    .Where(entry => entry.PayloadType == codec.PayloadType)
                    .Select(m => m.MappedPayloadType)
                    .FirstOrDefault();

                var matchedCapCodec = caps.Codecs
                    .Where(capCodec => capCodec.PreferredPayloadType == consumableCodecPt)
                    .FirstOrDefault();

                var consumableCodec = new RtpCodecParameters
                {
                    MimeType = matchedCapCodec.MimeType,
                    PayloadType = matchedCapCodec.PreferredPayloadType.Value,
                    ClockRate = matchedCapCodec.ClockRate,
                    Channels = matchedCapCodec.Channels,
                    Parameters = codec.Parameters, // Keep the Producer codec parameters.
                    RtcpFeedback = matchedCapCodec.RtcpFeedback
                };

                consumableParams.Codecs.Add(consumableCodec);

                var consumableCapRtxCodec = caps.Codecs
                    .Where(capRtxCodec => IsRtxMimeType(capRtxCodec.MimeType) && MatchCodecsWithPayloadTypeAndApt(consumableCodec.PayloadType, capRtxCodec.Parameters))
                    .FirstOrDefault();

                if (consumableCapRtxCodec != null)
                {
                    var consumableRtxCodec = new RtpCodecParameters
                    {
                        MimeType = consumableCapRtxCodec.MimeType,
                        PayloadType = consumableCapRtxCodec.PreferredPayloadType.Value,
                        ClockRate = consumableCapRtxCodec.ClockRate,
                        Channels = consumableCapRtxCodec.Channels,
                        Parameters = consumableCapRtxCodec.Parameters, // Keep the Producer codec parameters.
                        RtcpFeedback = consumableCapRtxCodec.RtcpFeedback
                    };

                    consumableParams.Codecs.Add(consumableRtxCodec);
                }
            }

            foreach (var capExt in caps.HeaderExtensions)
            {

                // Just take RTP header extension that can be used in Consumers.
                if (capExt.Kind != kind || (capExt.Direction != RtpHeaderExtensionDirection.SendReceive && capExt.Direction != RtpHeaderExtensionDirection.SendOnly))
                {
                    continue;
                }

                var consumableExt = new RtpHeaderExtensionParameters
                {
                    Uri = capExt.Uri,
                    Id = capExt.PreferredId,
                    Encrypt = capExt.PreferredEncrypt,
                    Parameters = new Dictionary<string, object>(),
                };

                consumableParams.HeaderExtensions.Add(consumableExt);
            }

            // Clone Producer encodings since we'll mangle them.
            var consumableEncodings = parameters.Encodings.DeepClone<List<RtpEncodingParameters>>();

            foreach (var consumableEncoding in consumableEncodings)
            {
            }
            for (var i = 0; i < consumableEncodings.Count; ++i)
            {
                var consumableEncoding = consumableEncodings[i];
                var mappedSsrc = rtpMapping.Encodings[i].MappedSsrc;

                // Remove useless fields.
                consumableEncoding.Rid = null;
                consumableEncoding.Rtx = null;
                consumableEncoding.CodecPayloadType = null;

                // Set the mapped ssrc.
                consumableEncoding.Ssrc = mappedSsrc;

                consumableParams.Encodings.Add(consumableEncoding);
            }

            consumableParams.Rtcp = new RtcpParameters
            {
                CNAME = parameters.Rtcp.CNAME,
                ReducedSize = true,
                Mux = true,
            };

            return consumableParams;
        }

        /// <summary>
        /// Check whether the given RTP capabilities can consume the given Producer.
        /// </summary>
        public static bool CanConsume(RtpParameters consumableParams, RtpCapabilities caps)
        {
            // This may throw.
            ValidateRtpCapabilities(caps);

            var matchingCodecs = new List<RtpCodecParameters>();

            foreach (var codec in consumableParams.Codecs)
            {
                var matchedCapCodec = caps.Codecs
                    .Where(capCodec => MatchCodecs(capCodec, codec, true))
                    .FirstOrDefault();

                if (matchedCapCodec == null)
                    continue;

                matchingCodecs.Add(codec);
            }

            // Ensure there is at least one media codec.
            if (matchingCodecs.Count == 0 || IsRtxMimeType(matchingCodecs[0].MimeType))
                return false;

            return true;
        }

        /// <summary>
        /// Generate RTP parameters for a specific Consumer.
        ///
        /// It reduces encodings to just one and takes into account given RTP capabilities
        /// to reduce codecs, codecs' RTCP feedback and header extensions, and also enables
        /// or disabled RTX.
        /// </summary>
        public static RtpParameters GetConsumerRtpParameters(RtpParameters consumableParams, RtpCapabilities caps)
        {
            var consumerParams = new RtpParameters
            {
                Codecs = new List<RtpCodecParameters>(),
                HeaderExtensions = new List<RtpHeaderExtensionParameters>(),
                Encodings = new List<RtpEncodingParameters>(),
                Rtcp = consumableParams.Rtcp
            };

            foreach (var capCodec in caps.Codecs)
            {
                ValidateRtpCodecCapability(capCodec);
            }

            var consumableCodecs = consumableParams.Codecs.DeepClone<List<RtpCodecParameters>>();
            var rtxSupported = false;

            foreach (var codec in consumableCodecs)
            {
                var matchedCapCodec = caps.Codecs
                    .Where(capCodec => MatchCodecs(capCodec, codec, true))
                    .FirstOrDefault();

                if (matchedCapCodec == null)
                    continue;

                codec.RtcpFeedback = matchedCapCodec.RtcpFeedback;

                consumerParams.Codecs.Add(codec);

                if (!rtxSupported && IsRtxMimeType(codec.MimeType))
                    rtxSupported = true;
            }

            // Ensure there is at least one media codec.
            if (consumerParams.Codecs.Count == 0 || IsRtxMimeType(consumerParams.Codecs[0].MimeType))
            {
                throw new Exception("no compatible media codecs");
            }

            consumerParams.HeaderExtensions = consumableParams.HeaderExtensions
                .Where(ext =>
                    caps.HeaderExtensions
                        .Any(capExt => capExt.PreferredId == ext.Id && capExt.Uri == ext.Uri)
                ).ToList();

            // Reduce codecs' RTCP feedback. Use Transport-CC if available, REMB otherwise.
            if (consumerParams.HeaderExtensions.Any(ext => ext.Uri == "http://www.ietf.org/id/draft-holmer-rmcat-transport-wide-cc-extensions-01"))
            {
                foreach (var codec in consumerParams.Codecs)
                {
                    codec.RtcpFeedback = codec.RtcpFeedback.Where(fb => fb.Type != "goog-remb").ToArray();
                }
            }
            else if (consumerParams.HeaderExtensions.Any(ext => ext.Uri == "http://www.webrtc.org/experiments/rtp-hdrext/abs-send-time"))
            {
                foreach (var codec in consumerParams.Codecs)
                {
                    codec.RtcpFeedback = codec.RtcpFeedback.Where(fb => fb.Type != "transport-cc").ToArray();
                }
            }
            else
            {
                foreach (var codec in consumerParams.Codecs)
                {
                    codec.RtcpFeedback = codec.RtcpFeedback.Where(fb => fb.Type != "transport-cc" && fb.Type != "goog-remb").ToArray();
                }
            }

            var consumerEncoding = new RtpEncodingParameters
            {
                Ssrc = Utils.GenerateRandomNumber()

            };

            if (rtxSupported)
            {
                consumerEncoding.Rtx = new Rtx { Ssrc = Utils.GenerateRandomNumber() };
            }

            // If any in the consumableParams.Encodings has scalabilityMode, process it
            // (assume all encodings have the same value).
            var encodingWithScalabilityMode = consumableParams.Encodings.Where(encoding => !encoding.ScalabilityMode.IsNullOrWhiteSpace()).FirstOrDefault();

            var scalabilityMode = encodingWithScalabilityMode != null
                ? encodingWithScalabilityMode.ScalabilityMode
                : null;

            // If there is simulast, mangle spatial layers in scalabilityMode.
            if (consumableParams.Encodings.Count > 1)
            {
                var scalabilityModeObject = ScalabilityMode.Parse(scalabilityMode);

                scalabilityMode = $"S{ consumableParams.Encodings.Count}T{ scalabilityModeObject.TemporalLayers }";
            }

            if (!scalabilityMode.IsNullOrWhiteSpace())
            {
                consumerEncoding.ScalabilityMode = scalabilityMode;
            }

            // Set a single encoding for the Consumer.
            consumerParams.Encodings.Add(consumerEncoding);

            // Copy verbatim.
            consumerParams.Rtcp = consumableParams.Rtcp;

            return consumerParams;
        }

        /// <summary>
        /// Generate RTP parameters for a pipe Consumer.
        ///
        /// It keeps all original consumable encodings and removes support for BWE. If
        /// enableRtx is false, it also removes RTX and NACK support.
        /// </summary>
        public static RtpParameters GetPipeConsumerRtpParameters(RtpParameters consumableParams, bool enableRtx = false)
        {
            var consumerParams = new RtpParameters
            {
                Codecs = new List<RtpCodecParameters>(),
                HeaderExtensions = new List<RtpHeaderExtensionParameters>(),
                Encodings = new List<RtpEncodingParameters>(),
                Rtcp = consumableParams.Rtcp
            };

            var consumableCodecs = consumableParams.Codecs.DeepClone<List<RtpCodecParameters>>();

            foreach (var codec in consumableCodecs)
            {
                if (!enableRtx && IsRtxMimeType(codec.MimeType))
                    continue;

                codec.RtcpFeedback = codec.RtcpFeedback
                    .Where(fb =>
                        (fb.Type == "nack" && fb.Parameter == "pli") ||
                        (fb.Type == "ccm" && fb.Parameter == "fir") ||
                        (enableRtx && fb.Type == "nack" && fb.Parameter.IsNullOrWhiteSpace())
                    ).ToArray();

                consumerParams.Codecs.Add(codec);
            }

            // Reduce RTP extensions by disabling transport MID and BWE related ones.
            consumerParams.HeaderExtensions = consumableParams.HeaderExtensions
                .Where(ext => (
                    ext.Uri != "urn:ietf:parameters:rtp-hdrext:sdes:mid" &&
                        ext.Uri != "http://www.webrtc.org/experiments/rtp-hdrext/abs-send-time" &&
                        ext.Uri != "http://www.ietf.org/id/draft-holmer-rmcat-transport-wide-cc-extensions-01"
                    )).ToList();

            var consumableEncodings = consumableParams.Encodings.DeepClone<List<RtpEncodingParameters>>();

            foreach (var encoding in consumableEncodings)
            {
                if (!enableRtx)
                    encoding.Rtx = null;

                consumerParams.Encodings.Add(encoding);
            }

            return consumerParams;
        }

        public static bool IsRtxMimeType(string mimeType)
        {
            return RtxMimeTypeRegex.IsMatch(mimeType);
        }

        private static bool CheckDirectoryValueEquals(IDictionary<string, object> a, IDictionary<string, object> b, string key)
        {
            if (a != null && b != null)
            {
                var got1 = a.TryGetValue(key, out var aPacketizationMode);
                var got2 = b.TryGetValue(key, out var bPacketizationMode);
                if (got1 && got2)
                {
                    if (aPacketizationMode.Equals(bPacketizationMode))
                        return false;
                }
                else if ((got1 && !got2) || (!got1 && got2))
                {
                    return false;
                }
            }
            else if (a != null && b == null)
            {
                var got = a.TryGetValue("packetization-mode", out var _);
                if (got)
                {
                    return false;
                }
            }
            else if (a == null && b != null)
            {
                var got = b.TryGetValue("packetization-mode", out var _);
                if (got)
                {
                    return false;
                }
            }
            return true;
        }

        public static bool MatchCodecs(RtpCodecBase aCodec, RtpCodecBase bCodec, bool strict = false, bool modify = false)
        {
            var aMimeType = aCodec.MimeType.ToLower();
            var bMimeType = bCodec.MimeType.ToLower();

            if (aMimeType != bMimeType)
                return false;

            if (aCodec.ClockRate != bCodec.ClockRate)
                return false;

            if (aCodec.Channels != bCodec.Channels)
                return false;

            // Per codec special checks.
            switch (aMimeType)
            {
                case "video/h264":
                    {
                        if (!CheckDirectoryValueEquals(aCodec.Parameters, aCodec.Parameters, "packetization-mode"))
                        {
                            return false;
                        }
                        // If strict matching check profile-level-id.
                        if (strict)
                        {
                            if (!H264ProfileLevelId.IsSameProfile(aCodec.Parameters, bCodec.Parameters))
                                return false;

                            string selectedProfileLevelId;

                            try
                            {
                                selectedProfileLevelId = H264ProfileLevelId.GenerateProfileLevelIdForAnswer(aCodec.Parameters, bCodec.Parameters);
                            }
                            catch (Exception)
                            {
                                return false;
                            }

                            if (modify)
                            {
                                if (!selectedProfileLevelId.IsNullOrWhiteSpace())
                                    aCodec.Parameters["profile-level-id"] = selectedProfileLevelId;
                                else
                                    aCodec.Parameters.Remove("profile-level-id");
                            }
                        }

                        break;
                    }

                case "video/vp9":
                    {
                        // If strict matching check profile-id.
                        if (strict)
                        {
                            if (!CheckDirectoryValueEquals(aCodec.Parameters, aCodec.Parameters, "profile-id"))
                            {
                                return false;
                            }
                        }

                        break;
                    }
            }

            return true;
        }

        public static bool MatchCodecsWithPayloadTypeAndApt(int payloadType, IDictionary<string, object> parameters)
        {
            if (parameters == null) return false;
            if (!parameters.TryGetValue("apt", out var apt))
            {
                return false;
            }
            var apiInteger = (int)apt;
            if (payloadType != apiInteger)
            {
                return false;
            }
            return true;
        }

    }
}
