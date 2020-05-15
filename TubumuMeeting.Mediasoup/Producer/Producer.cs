using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using TubumuMeeting.Mediasoup.Extensions;

namespace TubumuMeeting.Mediasoup
{
    public class ProducerInternalData
    {
        /// <summary>
        /// Router id.
        /// </summary>
        public string RouterId { get; }

        /// <summary>
        /// Transport id.
        /// </summary>
        public string TransportId { get; }

        /// <summary>
        /// Producer id.
        /// </summary>
        public string ProducerId { get; }

        public ProducerInternalData(string routerId, string transportId, string producerId)
        {
            RouterId = routerId;
            TransportId = transportId;
            ProducerId = producerId;
        }
    }

    public class Producer : EventEmitter
    {
        /// <summary>
        /// Logger
        /// </summary>
        private readonly ILogger<Producer> _logger;

        /// <summary>
        /// Internal data.
        /// </summary>
        private ProducerInternalData Internal { get; set; }

        /// <summary>
        /// Producer id.
        /// </summary>
        public string ProducerId => Internal.ProducerId;

        #region Producer data.

        /// <summary>
        /// Media kind.
        /// </summary>
        public MediaKind Kind { get; }

        /// <summary>
        /// RTP parameters.
        /// </summary>
        public RtpParameters RtpParameters { get; }

        /// <summary>
        /// Producer type.
        /// </summary>
        public ProducerType Type { get; }

        /// <summary>
        /// Consumable RTP parameters.
        /// </summary>
        public RtpParameters ConsumableRtpParameters { get; }

        #endregion

        /// <summary>
        /// Channel instance.
        /// </summary>
        private readonly Channel _channel;

        /// <summary>
        /// App custom data.
        /// </summary>
        public Dictionary<string, object>? AppData { get; private set; }

        /// <summary>
        /// Whether the Producer is closed.
        /// </summary>
        public bool Closed { get; private set; }

        /// <summary>
        /// Paused flag.
        /// </summary>
        public bool Paused { get; private set; }

        /// <summary>
        /// Current score.
        /// </summary>
        public ProducerScore[] Score = Array.Empty<ProducerScore>();

        /// <summary>
        /// Observer instance.
        /// </summary>
        public EventEmitter Observer { get; } = new EventEmitter();

        /// <summary>
        /// <para>Events:</para>
        /// <para>@emits transportclose</para></para>
        /// <para>@emits score - (score: ProducerScore[])</para>
        /// <para>@emits videoorientationchange - (videoOrientation: ProducerVideoOrientation)</para>
        /// <para>@emits trace - (trace: ProducerTraceEventData)</para>
        /// <para>@emits @close</para>
        /// <para>Observer events:</para>
        /// <para>@emits close</para>
        /// <para>@emits pause</para>
        /// <para>@emits resume</para>
        /// <para>@emits score - (score: ProducerScore[])</para>
        /// <para>@emits videoorientationchange - (videoOrientation: ProducerVideoOrientation)</para>
        /// <para>@emits trace - (trace: ProducerTraceEventData)</para>      
        /// </summary>
        /// <param name="loggerFactory"></param>
        /// <param name="producerInternalData"></param>
        /// <param name="kind"></param>
        /// <param name="rtpParameters"></param>
        /// <param name="type"></param>
        /// <param name="consumableRtpParameters"></param>
        /// <param name="channel"></param>
        /// <param name="appData"></param>
        /// <param name="paused"></param>
        public Producer(ILoggerFactory loggerFactory,
                    ProducerInternalData producerInternalData,
                    MediaKind kind,
                    RtpParameters rtpParameters,
                    ProducerType type,
                    RtpParameters consumableRtpParameters,
                    Channel channel,
                    Dictionary<string, object>? appData,
                    bool paused)
        {
            _logger = loggerFactory.CreateLogger<Producer>();

            // Internal
            Internal = producerInternalData;

            // Data
            Kind = kind;
            RtpParameters = rtpParameters;
            Type = type;
            ConsumableRtpParameters = consumableRtpParameters;

            _channel = channel;
            AppData = appData;
            Paused = paused;

            HandleWorkerNotifications();
        }

        /// <summary>
        /// Close the Producer.
        /// </summary>
        public void Close()
        {
            if (Closed)
                return;

            _logger.LogDebug("Close()");

            Closed = true;

            // Remove notification subscriptions.
            _channel.MessageEvent -= OnChannelMessage;

            // Fire and forget
            _channel.RequestAsync(MethodId.PRODUCER_CLOSE, Internal).ContinueWithOnFaultedHandleLog(_logger);

            Emit("close");

            // Emit observer event.
            Observer.Emit("close");
        }

        /// <summary>
        /// Transport was closed.
        /// </summary>
        public void TransportClosed()
        {
            if (Closed)
                return;

            _logger.LogDebug("TransportClosed()");

            Closed = true;

            // Remove notification subscriptions.
            _channel.MessageEvent -= OnChannelMessage;

            Emit("transportclose");

            // Emit observer event.
            Observer.Emit("close");
        }

        /// <summary>
        /// Dump DataProducer.
        /// </summary>
        public Task<string?> DumpAsync()
        {
            _logger.LogDebug("DumpAsync()");

            return _channel.RequestAsync(MethodId.PRODUCER_DUMP, Internal);
        }

        /// <summary>
        /// Get DataProducer stats.
        /// </summary>
        public Task<string?> GetStatsAsync()
        {
            _logger.LogDebug("GetStatsAsync()");

            return _channel.RequestAsync(MethodId.PRODUCER_GET_STATS, Internal);
        }

        /// <summary>
        /// Pause the Producer.
        /// </summary>
        public async Task PauseAsync()
        {
            _logger.LogDebug("PauseAsync()");

            var wasPaused = Paused;

            await _channel.RequestAsync(MethodId.PRODUCER_PAUSE, Internal);

            Paused = true;

            // Emit observer event.
            if (!wasPaused)
            {
                Observer.Emit("pause");
            }
        }

        /// <summary>
        /// Resume the Producer.
        /// </summary>
        public async Task ResumeAsync()
        {
            _logger.LogDebug("ResumeAsync()");

            var wasPaused = Paused;

            await _channel.RequestAsync(MethodId.PRODUCER_RESUME, Internal);

            Paused = false;

            // Emit observer event.
            if (wasPaused)
            {
                Observer.Emit("resume");
            }
        }

        /// <summary>
        /// Enable 'trace' event.
        /// </summary>
        public Task EnableTraceEventAsync(TraceEventType[] types)
        {
            _logger.LogDebug("EnableTraceEventAsync()");

            var reqData = new
            {
                Types = types ?? new TraceEventType[0]
            };

            return _channel.RequestAsync(MethodId.PRODUCER_ENABLE_TRACE_EVENT, Internal, reqData);
        }

        #region Event Handlers

        private void HandleWorkerNotifications()
        {
            _channel.MessageEvent += OnChannelMessage;
        }

        private void OnChannelMessage(string targetId, string @event, string data)
        {
            if (targetId != ProducerId) return;
            switch (@event)
            {
                case "score":
                    {
                        var score = JsonConvert.DeserializeObject<ProducerScore[]>(data);
                        Score = score;

                        Emit("score", score);

                        // Emit observer event.
                        Observer.Emit("score", score);

                        break;
                    }
                case "videoorientationchange":
                    {
                        var videoOrientation = JsonConvert.DeserializeObject<ProducerVideoOrientation>(data);

                        Emit("videoorientationchange", videoOrientation);

                        // Emit observer event.
                        Observer.Emit("videoorientationchange", videoOrientation);

                        break;
                    }
                case "trace":
                    {
                        var trace = JsonConvert.DeserializeObject<TransportTraceEventData>(data);

                        Emit("trace", trace);

                        // Emit observer event.
                        Observer.Emit("trace", trace);

                        break;
                    }
                default:
                    {
                        _logger.LogError($"OnChannelMessage() | ignoring unknown event{@event}");
                        break;
                    }
            }
        }

        #endregion
    }
}
