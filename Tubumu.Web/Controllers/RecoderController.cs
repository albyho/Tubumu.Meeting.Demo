using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Tubumu.Mediasoup;
using Tubumu.Meeting.Server;
using Tubumu.Utils.Models;
using System.Linq;

namespace Tubumu.Web.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class RecoderController : ControllerBase
    {
        private readonly ILogger<TestController> _logger;
        private readonly Scheduler _scheduler;

        public RecoderController(ILogger<TestController> logger, Scheduler scheduler)
        {
            _logger = logger;
            _scheduler = scheduler;
        }
        
        [HttpGet]
        public ApiResult Get()
        {
            return new ApiResult();
        }

        [HttpGet("Record.sdp")]
        public async Task<object> Record()
        {
            var recorderPrepareRequest = new RecorderPrepareRequest
            {
                PeerId = "100001@100001",
                RoomId = "0",
                ProducerPeerId = "0",
                ProducerSources = new string[] { "audio:mic" }
            };

            // Join
            _ = await _scheduler.LeaveAsync(recorderPrepareRequest.PeerId);
            var joinRequest = new JoinRequest
            {
                RtpCapabilities = new RtpCapabilities
                {
                    Codecs = new List<RtpCodecCapability>
                    {
                        new RtpCodecCapability {
                            Kind = MediaKind.Audio,
                            MimeType = "audio/opus",
                            ClockRate = 48000,
                            Channels = 2,
                            RtcpFeedback = new RtcpFeedback[]
                            {
                                new RtcpFeedback{
                                    Type = "transport-cc",
                                },
                            }
                        },
                        //new RtpCodecCapability {
                        //    Kind = MediaKind.Audio,
                        //    MimeType ="audio/PCMA",
                        //    PreferredPayloadType= 8,
                        //    ClockRate = 8000,
                        //    RtcpFeedback = new RtcpFeedback[]
                        //    {
                        //        new RtcpFeedback{
                        //            Type = "transport-cc",
                        //        },
                        //    }
                        //},
                        new RtpCodecCapability {
                            Kind = MediaKind.Video,
                            MimeType ="video/H264",
                            ClockRate = 90000,
                            Parameters = new Dictionary<string, object> {
                                { "level-asymmetry-allowed", 1 },
                            },
                            RtcpFeedback = new RtcpFeedback[]
                            {
                                new RtcpFeedback {
                                    Type = "nack",
                                },
                                new RtcpFeedback {
                                    Type = "nack", Parameter = "pli",
                                },
                                new RtcpFeedback {
                                    Type = "ccm", Parameter = "fir",
                                },
                                new RtcpFeedback {
                                    Type = "goog-remb",
                                },
                                new RtcpFeedback {
                                    Type = "transport-cc",
                                },
                            }
                        },

                    },
                },
                DisplayName = $"Recorder:{recorderPrepareRequest.PeerId}",
                Sources = null,
                AppData = new Dictionary<string, object> { ["type"] = "Recorder" },
            };

            _ = await _scheduler.JoinAsync(recorderPrepareRequest.PeerId, "", joinRequest);

            // Join room
            var joinRoomRequest = new JoinRoomRequest
            {
                RoomId = recorderPrepareRequest.RoomId,
            };
            var joinRoomResult = await _scheduler.JoinRoomAsync(recorderPrepareRequest.PeerId, "", joinRoomRequest);

            // Create PlainTransport
            var transport = await CreatePlainTransport(recorderPrepareRequest.PeerId);
            var recorderPrepareResult = new RecorderPrepareResult
            {
                TransportId = transport.TransportId,
                Ip = transport.Tuple.LocalIp,
                Port = transport.Tuple.LocalPort,
                RtcpPort = transport.RtcpTuple != null ? transport.RtcpTuple.LocalPort : null
            };

            // Create Consumers
            var producerPeer = joinRoomResult.Peers.Where(m => m.PeerId == recorderPrepareRequest.ProducerPeerId).FirstOrDefault();
            if(producerPeer == null)
            {
                return new ApiResult { Code = 400, Message = "生产者 Peer 不存在" };
            }

            var producers = await producerPeer.GetProducersASync();
            foreach (var source in recorderPrepareRequest.ProducerSources)
            {
                if (!producerPeer.Sources.Contains(source))
                {
                    return new ApiResult { Code = 400, Message = $"生产者 Sources 不包含请求的 {source}" };
                }

                var producer = producers.Values.FirstOrDefault(m => m.Source == source);
                if(producer == null)
                {
                    return new ApiResult { Code = 400, Message = $"生产者尚未生产 {source}" };
                }
                var consumer = await _scheduler.ConsumeAsync(recorderPrepareRequest.ProducerPeerId, recorderPrepareRequest.PeerId, producer.ProducerId);
                if(consumer == null)
                {
                    return new ApiResult { Code = 400, Message = $"已经在消费 {source}" };
                }

                recorderPrepareResult.ConsumerParameters.Add(new ConsumerParameters
                {
                    Source = source,
                    Kind = consumer.Kind,
                    PayloadType = consumer.RtpParameters.Codecs[0].PayloadType,
                    Ssrc = consumer.RtpParameters!.Encodings![0].Ssrc
                });
            }


            return Content(recorderPrepareResult.Sdp(0));
        }

        private async Task<PlainTransport> CreatePlainTransport(string peerId)
        {
            var createPlainTransportRequest = new CreatePlainTransportRequest
            {
                Comedia = true,
                RtcpMux = false,
                Producing = false,
                Consuming = true,
            };
            var transport = await _scheduler.CreatePlainTransportAsync(peerId, "", createPlainTransportRequest);
            return transport;
        }
    }
}
