using System;
using System.Collections.Generic;

namespace TubumuMeeting.Mediasoup
{
    public class MediasoupOptions
    {
        public string MediasoupVersion { get; set; }

        public string WorkerPath { get; set; }

        public int NumberOfWorkers { get; set; }

        public MediasoupSettings MediasoupSettings { get; set; }

        public static MediasoupOptions Default { get; } = new MediasoupOptions
        {
            MediasoupVersion = "3.5.7",
            NumberOfWorkers = Environment.ProcessorCount,
            MediasoupSettings = new MediasoupSettings
            {
                WorkerSettings = new WorkerSettings
                {
                    LogLevel = WorkerLogLevel.Warn,
                    LogTags = new[] {
                        "info",
                        "ice",
                        "dtls",
                        "rtp",
                        "srtp",
                        "rtcp",
                        //"rtx",
                        //"bwe",
                        //"score",
                        //"simulcast",
                        //"svc",
                        //"sctp",
                    },
                    RtcMinPort = 40000,
                    RtcMaxPort = 49999,
                },
                RouteSettings = new RouteSettings
                {
                    RtpCodecCapabilities = new[]
                    {
                        new RtpCodecCapability
                        {
                            Kind      = MediaKind.Audio,
                            MimeType  = "audio/opus",
                            ClockRate = 48000,
                            Channels  = 2
                        },
                        new RtpCodecCapability{
                            Kind       = MediaKind.Video,
                            MimeType   = "video/VP8",
                            ClockRate  = 90000,
                            Parameters = new Dictionary<string, object>
                            {
                                { "x-google-start-bitrate" , 1000 }
                            }
                        },
                        new RtpCodecCapability{
                            Kind       = MediaKind.Video,
                            MimeType   = "video/VP9",
                            ClockRate  = 90000,
                            Parameters = new Dictionary<string, object>
                            {
                                { "profile-id"             , 2 },
                                { "x-google-start-bitrate" , 1000 }
                            }
                        },
                        new RtpCodecCapability{
                            Kind       = MediaKind.Video,
                            MimeType   = "video/h264",
                            ClockRate  = 90000,
                            Parameters = new Dictionary<string, object>
                            {
                                { "packetization-mode"      , 1 },
                                { "profile-level-id"        , "4d0032" },
                                { "level-asymmetry-allowed" , 1 },
                                { "x-google-start-bitrate"  , 1000 }
                            }
                        },
                        new RtpCodecCapability{
                            Kind       = MediaKind.Video,
                            MimeType   = "video/h264",
                            ClockRate  = 90000,
                            Parameters = new Dictionary<string, object>
                            {
                                { "packetization-mode"      , 1 },
                                { "profile-level-id"        , "42e01f" },
                                { "level-asymmetry-allowed" , 1 },
                                { "x-google-start-bitrate"  , 1000 }
                            }
                        }
                    },
                },
                WebRtcTransportSettings = new WebRtcTransportSettings
                {
                    ListenIps = new[]
                    {
                        new TransportListenIp { Ip = "127.0.0.1",  AnnouncedIp = "192.168.18.233"}
                    },
                    InitialAvailableOutgoingBitrate = 1000000,
                    MinimumAvailableOutgoingBitrate = 600000,
                    MaxSctpMessageSize = 262144,
                    MaximumIncomingBitrate = 1500000,
                }
            }
        };
    }
}
