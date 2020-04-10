using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Tubumu.Core.Extensions;
using Tubumu.Core.Extensions.Object;

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
            codec.Kind = mimeType.StartsWith("Video") ? MediaKind.Video : MediaKind.Audio;

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
                parameters.HeaderExtensions = new RtpHeaderExtensionParameters[0];
            }

            foreach (var ext in parameters.HeaderExtensions)
            {
                ValidateRtpHeaderExtensionParameters(ext);
            }

            // encodings is optional. If unset, fill with an empty array.
            if (parameters.Encodings == null)
            {
                parameters.Encodings = new RtpEncodingParameters[0];
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
                        MimeType = $"{ codec.Kind.GetEnumStringValue()}/rtx",
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
                    .Where((capCodec) => MatchCodecs(codec, capCodec, { strict: true, modify: true }));
			);

                if (!matchedCapCodec)
                {
                    throw new UnsupportedError(
				`unsupported codec[mimeType:${ codec.mimeType }, payloadType:${ codec.payloadType}]`);
                }

                codecToCapCodec.Set(codec, matchedCapCodec);
            }

            // Match parameters RTX codecs to capabilities RTX codecs.
            foreach (var codec in parameters.Codecs)
	{
                if (!isRtxCodec(codec))
                    continue;

                // Search for the associated media codec.
                const associatedMediaCodec = parameters.Codecs
                    .find((mediaCodec) => mediaCodec.payloadType === codec.parameters.apt);

                if (!associatedMediaCodec)
                {
                    throw new TypeError(
				`missing media codec found for RTX PT ${ codec.payloadType}`);
                }

                const capMediaCodec = codecToCapCodec.get(associatedMediaCodec);

                // Ensure that the capabilities media codec has a RTX codec.
                const associatedCapRtxCodec = caps.Codecs
                    .find((capCodec) => (
                        isRtxCodec(capCodec) &&
                        capCodec.parameters.apt === capMediaCodec.preferredPayloadType
                    ));

                if (!associatedCapRtxCodec)
                {
                    throw new UnsupportedError(
				`no RTX codec for capability codec PT ${ capMediaCodec.preferredPayloadType}`);
                }

                codecToCapCodec.Set(codec, associatedCapRtxCodec);
            }

            // Generate codecs mapping.
            foreach (var[codec, capCodec] in codecToCapCodec)
            {
                rtpMapping.Codecs.push(
        
            {
                    payloadType: codec.payloadType,
				mappedPayloadType: capCodec.preferredPayloadType

            });
            }

            // Generate encodings mapping.
            let mappedSsrc = utils.generateRandomNumber();

            foreach (var encoding in parameters.Encodings)
	{
                const mappedEncoding: any = { };

                mappedEncoding.mappedSsrc = mappedSsrc++;

                if (encoding.rid)
                    mappedEncoding.rid = encoding.rid;
                if (encoding.Ssrc)
                    mappedEncoding.Ssrc = encoding.Ssrc;
                if (encoding.ScalabilityMode)
                    mappedEncoding.ScalabilityMode = encoding.ScalabilityMode;

                rtpMapping.Encodings.push(mappedEncoding);
            }

            return rtpMapping;
        }

        /// <summary>
        /// Generate RTP parameters to be internally used by Consumers given the RTP
        /// parameters in a Producer and the RTP capabilities in the Router.
        /// </summary>
        public getConsumableRtpParameters(
            kind: string,
            parameters: RtpParameters,
            caps: RtpCapabilities,
            rtpMapping: RtpMapping
        ) : RtpParameters
{
	const consumableParams: RtpParameters =
	{
		codecs           : [],
		headerExtensions : [],
		encodings        : [],
		rtcp             : {}
	};

	foreach(var codec in parameters.Codecs)
	{
		if (isRtxCodec(codec))
			continue;

		const consumableCodecPt = rtpMapping.Codecs
            .find((entry) => entry.payloadType === codec.payloadType)
            .mappedPayloadType;

    const matchedCapCodec = caps.Codecs
        .find((capCodec) => capCodec.preferredPayloadType === consumableCodecPt);

    const consumableCodec =
    {
            mimeType     : matchedCapCodec.mimeType,
            payloadType  : matchedCapCodec.preferredPayloadType,
            clockRate    : matchedCapCodec.clockRate,
            channels     : matchedCapCodec.channels,
            parameters   : codec.parameters, // Keep the Producer codec parameters.
			rtcpFeedback : matchedCapCodec.rtcpFeedback
        };

    consumableParams.Codecs.push(consumableCodec);

		const consumableCapRtxCodec = caps.Codecs
            .find((capRtxCodec) => (
                isRtxCodec(capRtxCodec) &&
                capRtxCodec.parameters.apt === consumableCodec.payloadType
            ));

		if (consumableCapRtxCodec)
		{
			const consumableRtxCodec =
            {
                mimeType     : consumableCapRtxCodec.mimeType,
                payloadType  : consumableCapRtxCodec.preferredPayloadType,
                clockRate    : consumableCapRtxCodec.clockRate,
                parameters   : consumableCapRtxCodec.parameters,
                rtcpFeedback : consumableCapRtxCodec.rtcpFeedback
            };

    consumableParams.Codecs.push(consumableRtxCodec);
		}
	}

	foreach(var capExt in caps.HeaderExtensions)
	{

		// Just take RTP header extension that can be used in Consumers.
		if (
            capExt.kind !== kind ||
			(capExt.direction !== 'sendrecv' && capExt.direction !== 'sendonly')
		)
		{
			continue;
		}

		const consumableExt =
        {
            uri        : capExt.uri,
            id         : capExt.preferredId,
            encrypt    : capExt.preferredEncrypt,
            parameters : {}
        };

consumableParams.HeaderExtensions.push(consumableExt);
	}

	// Clone Producer encodings since we'll mangle them.
	const consumableEncodings = utils.clone(parameters.Encodings) as RtpEncodingParameters[];

	for (let i = 0; i<consumableEncodings.length; ++i)
	{
		const consumableEncoding = consumableEncodings[i];
const { mappedSsrc } = rtpMapping.Encodings[i];

		// Remove useless fields.
		delete consumableEncoding.rid;
delete consumableEncoding.rtx;
delete consumableEncoding.codecPayloadType;

// Set the mapped ssrc.
consumableEncoding.Ssrc = mappedSsrc;

		consumableParams.Encodings.push(consumableEncoding);
	}

	consumableParams.rtcp =
	{
		cname       : parameters.rtcp.cname,
		reducedSize : true,
		mux         : true
	};

	return consumableParams;
}

/// <summary>
 /// Check whether the given RTP capabilities can consume the given Producer.
 /// </summary>
public canConsume(
    consumableParams: RtpParameters,
    caps: RtpCapabilities
) : boolean
{
	// This may throw.
	ValidateRtpCapabilities(caps);

const matchingCodecs: RtpCodecParameters[] = [];

	foreach(var codec in consumableParams.Codecs)
	{
		const matchedCapCodec = caps.Codecs
            .find((capCodec) => matchCodecs(capCodec, codec, { strict: true }));

		if (!matchedCapCodec)
			continue;

		matchingCodecs.push(codec);
	}

	// Ensure there is at least one media codec.
	if (matchingCodecs.length === 0 || isRtxCodec(matchingCodecs[0]))
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
public getConsumerRtpParameters(
    consumableParams: RtpParameters,
    caps: RtpCapabilities
) : RtpParameters
{
	const consumerParams: RtpParameters =
	{
		codecs           : [],
		headerExtensions : [],
		encodings        : [],
		rtcp             : consumableParams.rtcp
	};

	foreach(var capCodec in caps.Codecs)
	{
		ValidateRtpCodecCapability(capCodec);
	}

	const consumableCodecs =
        utils.clone(consumableParams.Codecs) as RtpCodecParameters[];

let rtxSupported = false;

	foreach(var codec in consumableCodecs)
	{
		const matchedCapCodec = caps.Codecs
            .find((capCodec) => matchCodecs(capCodec, codec, { strict: true }));

		if (!matchedCapCodec)
			continue;

		codec.rtcpFeedback = matchedCapCodec.rtcpFeedback;

		consumerParams.Codecs.push(codec);

		if (!rtxSupported && isRtxCodec(codec))
			rtxSupported = true;
	}

	// Ensure there is at least one media codec.
	if (consumerParams.Codecs.length === 0 || isRtxCodec(consumerParams.Codecs[0]))
	{
		throw new UnsupportedError('no compatible media codecs');
	}

	consumerParams.HeaderExtensions = consumableParams.HeaderExtensions
        .filter((ext) => (
            caps.HeaderExtensions
                .Some((capExt) => (
                    capExt.preferredId === ext.id &&
					capExt.uri === ext.uri
				))
		));

	// Reduce codecs' RTCP feedback. Use Transport-CC if available, REMB otherwise.
	if (
        consumerParams.HeaderExtensions.Some((ext) => (
            ext.uri === 'http://www.ietf.org/id/draft-holmer-rmcat-transport-wide-cc-extensions-01'
		))
	)
	{
		foreach(var codec in consumerParams.Codecs)
		{
			codec.rtcpFeedback = (codec.rtcpFeedback)
				.filter((fb) => fb.type !== 'goog-remb');
		}
	}
	else if (
        consumerParams.HeaderExtensions.Some((ext) => (
            ext.uri === 'http://www.webrtc.org/experiments/rtp-hdrext/abs-send-time'
		))
	)
	{
		foreach(var codec in consumerParams.Codecs)
		{
			codec.rtcpFeedback = (codec.rtcpFeedback)
				.filter((fb) => fb.type !== 'transport-cc');
		}
	}
	else
	{
		foreach(var codec in consumerParams.Codecs)
		{
			codec.rtcpFeedback = (codec.rtcpFeedback)
				.filter((fb) => (
                    fb.type !== 'transport-cc' &&

                    fb.type !== 'goog-remb'
				));
		}
	}

	const consumerEncoding: RtpEncodingParameters =
	{
		ssrc : utils.generateRandomNumber()
	};

	if (rtxSupported)
        consumerEncoding.rtx = { ssrc: utils.generateRandomNumber() };

// If any in the consumableParams.Encodings has scalabilityMode, process it
// (assume all encodings have the same value).
const encodingWithScalabilityMode =
    consumableParams.Encodings.find((encoding) => encoding.ScalabilityMode);

let scalabilityMode = encodingWithScalabilityMode
    ? encodingWithScalabilityMode.ScalabilityMode
    : undefined;

	// If there is simulast, mangle spatial layers in scalabilityMode.
	if (consumableParams.Encodings.length > 1)
	{
		const { temporalLayers } = parseScalabilityMode(scalabilityMode);

scalabilityMode = `S${consumableParams.Encodings.length}T${temporalLayers}`;
	}

	if (scalabilityMode)
        consumerEncoding.ScalabilityMode = scalabilityMode;

// Set a single encoding for the Consumer.
consumerParams.Encodings.push(consumerEncoding);

	// Copy verbatim.
	consumerParams.rtcp = consumableParams.rtcp;

	return consumerParams;
}

/// <summary>
 /// Generate RTP parameters for a pipe Consumer.
 ///
 /// It keeps all original consumable encodings and removes support for BWE. If
 /// enableRtx is false, it also removes RTX and NACK support.
 /// </summary>
public getPipeConsumerRtpParameters(
    consumableParams: RtpParameters,
    enableRtx = false
) : RtpParameters
{
	const consumerParams: RtpParameters =
	{
		codecs           : [],
		headerExtensions : [],
		encodings        : [],
		rtcp             : consumableParams.rtcp
	};

	const consumableCodecs =
        utils.clone(consumableParams.Codecs) as RtpCodecParameters[];

	foreach(var codec in consumableCodecs)
	{
		if (!enableRtx && isRtxCodec(codec))
			continue;

		codec.rtcpFeedback = codec.rtcpFeedback
            .filter((fb) => (
                (fb.type === 'nack' && fb.parameter === 'pli') ||
				(fb.type === 'ccm' && fb.parameter === 'fir') ||
				(enableRtx && fb.type === 'nack' && !fb.parameter)
			));

		consumerParams.Codecs.push(codec);
	}

	// Reduce RTP extensions by disabling transport MID and BWE related ones.
	consumerParams.HeaderExtensions = consumableParams.HeaderExtensions
        .filter((ext) => (
            ext.uri !== 'urn:ietf:parameters:rtp-hdrext:sdes:mid' &&
			ext.uri !== 'http://www.webrtc.org/experiments/rtp-hdrext/abs-send-time' &&
			ext.uri !== 'http://www.ietf.org/id/draft-holmer-rmcat-transport-wide-cc-extensions-01'
		));

	const consumableEncodings =
        utils.clone(consumableParams.Encodings) as RtpEncodingParameters[];

	foreach(var encoding in consumableEncodings)
	{
		if (!enableRtx)
			delete encoding.rtx;

consumerParams.Encodings.push(encoding);
	}

	return consumerParams;
}

public static bool IsRtxMimeType(string mimeType)
{
    return RtxMimeTypeRegex.IsMatch(mimeType);
}

public static bool MatchCodecs(
    aCodec: RtpCodecCapability | RtpCodecParameters,
    bCodec: RtpCodecCapability | RtpCodecParameters,

    { strict = false, modify = false } = {}
): boolean
{
	const aMimeType = aCodec.mimeType.toLowerCase();
const bMimeType = bCodec.mimeType.toLowerCase();

	if (aMimeType !== bMimeType)
		return false;

	if (aCodec.clockRate !== bCodec.clockRate)
		return false;

	if (aCodec.channels !== bCodec.channels)
		return false;

	// Per codec special checks.
	switch (aMimeType)
	{
		case 'video/h264':
		{
			const aPacketizationMode = aCodec.parameters['packetization-mode'] || 0;
const bPacketizationMode = bCodec.parameters['packetization-mode'] || 0;

			if (aPacketizationMode !== bPacketizationMode)
				return false;

			// If strict matching check profile-level-id.
			if (strict)
			{
				if (!h264.isSameProfile(aCodec.parameters, bCodec.parameters))
					return false;

				let selectedProfileLevelId;

				try
				{
					selectedProfileLevelId =
						h264.generateProfileLevelIdForAnswer(aCodec.parameters, bCodec.parameters);
				}
				catch (error)
				{
					return false;
				}

				if (modify)
				{
					if (selectedProfileLevelId)
                        aCodec.parameters['profile-level-id'] = selectedProfileLevelId;
					else
						delete aCodec.parameters['profile-level-id'];
				}
			}

			break;
		}

		case 'video/vp9':
		{
			// If strict matching check profile-id.
			if (strict)
			{
				const aProfileId = aCodec.parameters['profile-id'] || 0;
const bProfileId = bCodec.parameters['profile-id'] || 0;

				if (aProfileId !== bProfileId)
					return false;
			}

			break;
		}
	}

	return true;
}

    }
}
