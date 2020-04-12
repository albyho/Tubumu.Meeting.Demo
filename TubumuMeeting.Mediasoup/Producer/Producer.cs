using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using TubumuMeeting.Mediasoup.Extensions;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Tubumu.Core.Extensions;

namespace TubumuMeeting.Mediasoup
{
    public class Producer
    {
        // Logger
        private readonly ILoggerFactory _loggerFactory;
        private readonly ILogger<Producer> _logger;

        #region Internal data.

        public string RouterId { get; }

        public string TransportId { get; }

        /// <summary>
        /// Producer id.
        /// </summary>
        public string Id { get; }

        private object _internal;

        #endregion

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
        public Channel Channel { get; private set; }

        /// <summary>
        /// App custom data.
        /// </summary>
        public object? AppData { get; private set; }

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
        public ProducerScore[] Score = new ProducerScore[0];

        /// <summary>
        /// Observer instance.
        /// </summary>
        public ProducerObserver Observer { get; } = new ProducerObserver();

        #region Events

        public event Action? TransportCloseEvent;

        public event Action<ProducerScore[]>? ScoreEvent;

        public event Action<ProducerVideoOrientation>? VideoOrientationChangeEvent;

        public event Action<TraceEventData>? TraceEvent;

        public event Action? CloseEvent;

        #endregion

        public Producer(ILoggerFactory loggerFactory,
                    string routerId,
                    string transportId,
                    string producerId,
                    MediaKind kind,
                    RtpParameters rtpParameters,
                    ProducerType type,
                    RtpParameters consumableRtpParameters,
                    Channel channel,
                    object? appData,
                    bool paused)
        {
            _loggerFactory = loggerFactory;
            _logger = loggerFactory.CreateLogger<Producer>();
            RouterId = routerId;
            TransportId = transportId;
            Id = producerId;
            _internal = new
            {
                RouterId,
                TransportId,
                DataProducerId = Id,
            };
            Kind = kind;
            RtpParameters = rtpParameters;
            Type = type;
            ConsumableRtpParameters = consumableRtpParameters;
            Channel = channel;
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

            // Fire and forget
            Channel.RequestAsync(MethodId.PRODUCER_CLOSE.GetEnumStringValue(), _internal).ContinueWithOnFaultedHandleLog(_logger);

            CloseEvent?.Invoke();

            // Emit observer event.
            Observer.EmitClose();
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

            TransportCloseEvent?.Invoke();

            // Emit observer event.
            Observer.EmitClose();
        }

        /// <summary>
        /// Dump DataProducer.
        /// </summary>
        public Task<string?> DumpAsync()
        {
            _logger.LogDebug("DumpAsync()");
            return Channel.RequestAsync(MethodId.PRODUCER_DUMP.GetEnumStringValue(), _internal);
        }

        /// <summary>
        /// Get DataProducer stats.
        /// </summary>
        public Task<string?> GetStatsAsync()
        {
            _logger.LogDebug("GetStatsAsync()");
            return Channel.RequestAsync(MethodId.PRODUCER_GET_STATS.GetEnumStringValue(), _internal);
        }

        /// <summary>
        /// Pause the Producer.
        /// </summary>
        public async Task PauseAsync()
        {
            _logger.LogDebug("PauseAsync()");

            var wasPaused = Paused;

            await Channel.RequestAsync(MethodId.PRODUCER_PAUSE.GetEnumStringValue(), _internal);

            Paused = true;

            // Emit observer event.
            if (!wasPaused)
                Observer.EmitPause();
        }

        /// <summary>
        /// Resume the Producer.
        /// </summary>
        public async Task ResumeAsync()
        {
            _logger.LogDebug("ResumeAsync()");

            var wasPaused = Paused;

            await Channel.RequestAsync(MethodId.PRODUCER_RESUME.GetEnumStringValue(), _internal);

            Paused = false;

            // Emit observer event.
            if (wasPaused)
                Observer.EmitResume();
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

            return Channel.RequestAsync(MethodId.PRODUCER_ENABLE_TRACE_EVENT.GetEnumStringValue(), _internal, reqData);
        }

        #region Event Handlers

        private void HandleWorkerNotifications()
        {
            Channel.MessageEvent += OnChannelMessage;
        }

        private void OnChannelMessage(string targetId, string @event, string data)
        {
            if (targetId != Id) return;
            switch (@event)
            {
                case "score":
                    {
                        var score = JsonConvert.DeserializeObject<ProducerScore[]>(data);
                        Score = score;

                        ScoreEvent?.Invoke(score);

                        // Emit observer event.
                        Observer.EmitScore(score);

                        break;
                    }
                case "videoorientationchange":
                    {
                        var videoOrientation = JsonConvert.DeserializeObject<ProducerVideoOrientation>(data);
                        VideoOrientationChangeEvent?.Invoke(videoOrientation);

                        // Emit observer event.
                        Observer.EmitVideoOrientationChange(videoOrientation);

                        break;
                    }
                case "trace":
                    {
                        var trace = JsonConvert.DeserializeObject<TraceEventData>(data);

                        TraceEvent?.Invoke(trace);

                        // Emit observer event.
                        Observer.EmitTrace(trace);

                        break;
                    }
                default:
                    {
                        _logger.LogError($"ignoring unknown event{@event}");
                        break;
                    }
            }
        }

        #endregion
    }
}
