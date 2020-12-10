using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Tubumu.Core.Models;
using TubumuMeeting.Mediasoup;
using TubumuMeeting.Meeting.Server;

namespace TubumuMeeting.Meeting.Web.Controllers
{
    [ApiController]
    [Route("api/v1/[controller]")]
    public class TestController : ControllerBase
    {
        private readonly ILogger<TestController> _logger;
        private readonly Scheduler _scheduler;

        public TestController(ILogger<TestController> logger, Scheduler scheduler)
        {
            _logger = logger;
            _scheduler = scheduler;
        }

        [HttpGet]
        public ApiResult Get()
        {
            return new ApiResult();
        }

        [HttpGet("Test")]
        public async Task<ApiResult> Test()
        {
            var roomId = "0";
            var deviceId = "100001@100001";
            var videoSsrc = 2222u; // 作为参数传入或服务端生成

            await _scheduler.LeaveAsync(deviceId);

            var joinRequest = new JoinRequest
            {
                RtpCapabilities = new RtpCapabilities(),
                DisplayName = $"Device:{deviceId}",
                Sources = new[] { "camera" },
                AppData = new Dictionary<string, object> { ["type"] = "Device" },
            };
            if (!await _scheduler.JoinAsync(deviceId, null, joinRequest))
            {
                return new ApiResult { Code = 400, Message = "Join 失败" };
            }

            var joinRoomRequest = new JoinRoomRequest
            {
                RoomId = roomId,
            };
            var joinRoomResult = await _scheduler.JoinRoomAsync(deviceId, null, joinRoomRequest);

            var createPlainTransportRequest = new CreatePlainTransportRequest
            {
                Comedia = true,
                RtcpMux = false,
                Producing = true,
                Consuming = false,
            };
            var transport = await _scheduler.CreatePlainTransportAsync(deviceId, null, createPlainTransportRequest);

            // Audio: "{ \"codecs\": [{ \"mimeType\":\"audio/opus\", \"payloadType\":${AUDIO_PT}, \"clockRate\":48000, \"channels\":2, \"parameters\":{ \"sprop-stereo\":1 } }], \"encodings\": [{ \"ssrc\":${AUDIO_SSRC} }] }"
            // Video :"{ \"codecs\": [{ \"mimeType\":\"video/vp8\", \"payloadType\":${VIDEO_PT}, \"clockRate\":90000 }], \"encodings\": [{ \"ssrc\":${VIDEO_SSRC} }] }"
            var produceRequest = new ProduceRequest
            {
                Kind = MediaKind.Video,
                Source = "camera",
                RtpParameters = new RtpParameters
                {
                    Codecs = new List<RtpCodecParameters>
                    {
                        new RtpCodecParameters
                        {
                            MimeType = "video/h264",
                            PayloadType = 98,
                            ClockRate = 90000,
                        },
                    },
                    Encodings = new List<RtpEncodingParameters> {
                        new RtpEncodingParameters
                        {
                            Ssrc = videoSsrc
                        }
                    },
                },
                AppData = new Dictionary<string, object>
                {
                    ["peerId"] = deviceId,
                }
            };
            var produceResult = await _scheduler.ProduceAsync(deviceId, null, produceRequest);

            var result = new CreatePlainTransportResult
            {
                TransportId = transport.TransportId,
                Ip = transport.Tuple.LocalIp,
                Port = transport.Tuple.LocalPort,
            };
            return new ApiResult<CreatePlainTransportResult>
            {
                Data = result
            };
        }
    }
}
