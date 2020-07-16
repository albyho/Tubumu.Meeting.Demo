using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Tubumu.Core.Extensions;
using TubumuMeeting.Mediasoup.Extensions;

namespace TubumuMeeting.Mediasoup
{
    public class ConsumerInternalData
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
        /// Associated Producer id.
        /// </summary>
        public string ProducerId { get; }

        /// <summary>
        /// Consumer id.
        /// </summary>
        public string ConsumerId { get; }

        public ConsumerInternalData(string routerId, string transportId, string producerId, string consumerId)
        {
            RouterId = routerId;
            TransportId = transportId;
            ProducerId = producerId;
            ConsumerId = consumerId;
        }
    }

    public class Consumer : EventEmitter
    {
        /// <summary>
        /// Logger.
        /// </summary>
        private readonly ILogger<Consumer> _logger;

        /// <summary>
        /// Internal data.
        /// </summary>
        public ConsumerInternalData Internal { get; private set; }

        /// <summary>
        /// Consumer id.
        /// </summary>
        public string ConsumerId => Internal.ConsumerId;

        #region Consumer data.

        /// <summary>
        /// Media kind.
        /// </summary>
        public MediaKind Kind { get; }

        /// <summary>
        /// RTP parameters.
        /// </summary>
        public RtpParameters RtpParameters { get; }

        /// <summary>
        /// Consumer type.
        /// </summary>
        public ConsumerType Type { get; }

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
        /// Source.
        /// </summary>
        public string? Source { get; set; }

        /// <summary>
        /// Whether the Consumer is closed.
        /// </summary>
        public bool Closed { get; private set; }

        /// <summary>
        /// Paused flag.
        /// </summary>
        public bool Paused { get; private set; }

        /// <summary>
        /// Whether the associate Producer is paused.
        /// </summary>
        public bool ProducerPaused { get; private set; }

        /// <summary>
        /// Current priority.
        /// </summary>
        public int Priority { get; private set; } = 1;

        /// <summary>
        /// Current score.
        /// </summary>
        public ConsumerScore? Score;

        /// <summary>
        /// Preferred layers.
        /// </summary>
        public ConsumerLayers? PreferredLayers { get; private set; }

        /// <summary>
        /// Curent layers.
        /// </summary>
        public ConsumerLayers? CurrentLayers { get; private set; }

        /// <summary>
        /// Observer instance.
        /// </summary>
        public EventEmitter Observer { get; } = new EventEmitter();

        /// <summary>
        /// <para>Events:</para>
        /// <para>@emits transportclose</para>
        /// <para>@emits producerclose</para>
        /// <para>@emits producerpause</para>
        /// <para>@emits producerresume</para>
        /// <para>@emits score - (score: ConsumerScore)</para>
        /// <para>@emits layerschange - (layers: ConsumerLayers | undefined)</para>
        /// <para>@emits trace - (trace: ConsumerTraceEventData)</para>
        /// <para>@emits @close</para>
        /// <para>@emits @producerclose</para>
        /// <para>Observer events:</para>
        /// <para>@emits close</para>
        /// <para>@emits pause</para>
        /// <para>@emits resume</para>
        /// <para>@emits score - (score: ConsumerScore)</para>
        /// <para>@emits layerschange - (layers: ConsumerLayers | undefined)</para>
        /// <para>@emits trace - (trace: ConsumerTraceEventData)</para>
        /// </summary>
        /// <param name="loggerFactory"></param>
        /// <param name="consumerInternalData"></param>
        /// <param name="kind"></param>
        /// <param name="rtpParameters"></param>
        /// <param name="type"></param>
        /// <param name="channel"></param>
        /// <param name="appData"></param>
        /// <param name="paused"></param>
        /// <param name="producerPaused"></param>
        /// <param name="score"></param>
        /// <param name="preferredLayers"></param>
        public Consumer(ILoggerFactory loggerFactory,
            ConsumerInternalData consumerInternalData,
            MediaKind kind,
            RtpParameters rtpParameters,
            ConsumerType type,
            Channel channel,
            Dictionary<string, object>? appData,
            bool paused,
            bool producerPaused,
            ConsumerScore? score,
            ConsumerLayers? preferredLayers
            )
        {
            _logger = loggerFactory.CreateLogger<Consumer>();

            // Internal
            Internal = consumerInternalData;

            // Data
            Kind = kind;
            RtpParameters = rtpParameters;
            Type = type;

            _channel = channel;
            AppData = appData;
            Paused = paused;
            ProducerPaused = producerPaused;
            Score = score;
            PreferredLayers = preferredLayers;

            HandleWorkerNotifications();
        }

        /// <summary>
        /// Close the Producer.
        /// </summary>
        public void Close()
        {
            if (Closed)
            {
                return;
            }

            _logger.LogDebug("Close()");

            Closed = true;

            // Remove notification subscriptions.
            _channel.MessageEvent -= OnChannelMessage;

            // Fire and forget
            _channel.RequestAsync(MethodId.CONSUMER_CLOSE, Internal).ContinueWithOnFaultedHandleLog(_logger);

            Emit("@close");

            // Emit observer event.
            Observer.Emit("close");
        }

        /// <summary>
        /// Transport was closed.
        /// </summary>
        public void TransportClosed()
        {
            if (Closed)
            {
                return;
            }

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

            return _channel.RequestAsync(MethodId.CONSUMER_DUMP, Internal);
        }

        /// <summary>
        /// Get DataProducer stats.
        /// </summary>
        public Task<string?> GetStatsAsync()
        {
            _logger.LogDebug("GetStatsAsync()");

            return _channel.RequestAsync(MethodId.CONSUMER_GET_STATS, Internal);
        }

        /// <summary>
        /// Pause the Consumer.
        /// </summary>
        public async Task PauseAsync()
        {
            _logger.LogDebug("PauseAsync()");

            var wasPaused = Paused || ProducerPaused;

            await _channel.RequestAsync(MethodId.CONSUMER_PAUSE, Internal);

            Paused = true;

            // Emit observer event.
            if (!wasPaused)
            {
                Observer.Emit("pause");
            }
        }

        /// <summary>
        /// Resume the Consumer.
        /// </summary>
        public async Task ResumeAsync()
        {
            _logger.LogDebug("ResumeAsync()");

            var wasPaused = Paused || ProducerPaused;

            await _channel.RequestAsync(MethodId.CONSUMER_RESUME, Internal);

            Paused = false;

            // Emit observer event.
            if (wasPaused && !ProducerPaused)
            {
                Observer.Emit("resume");
            }
        }

        /// <summary>
        /// Set preferred video layers.
        /// </summary>
        public async Task SetPreferredLayersAsync(ConsumerLayers consumerLayers)
        {
            _logger.LogDebug("SetPreferredLayersAsync()");

            var reqData = consumerLayers;
            var status = await _channel.RequestAsync(MethodId.CONSUMER_SET_PREFERRED_LAYERS, Internal, reqData);
            var responseData = JsonConvert.DeserializeObject<ConsumerSetPreferredLayersResponseData>(status!);
            PreferredLayers = responseData;
        }

        /// <summary>
        /// Set priority.
        /// </summary>
        public async Task SetPriorityAsync(int priority)
        {
            _logger.LogDebug("SetPriorityAsync()");

            var reqData = new { Priority = priority };
            var status = await _channel.RequestAsync(MethodId.CONSUMER_SET_PRIORITY, Internal, reqData);
            var responseData = JsonConvert.DeserializeObject<ConsumerSetOrUnsetPriorityResponseData>(status!);
            Priority = responseData.Priority;
        }

        /// <summary>
        /// Unset priority.
        /// </summary>
        public async Task UnsetPriorityAsync()
        {
            _logger.LogDebug("UnsetPriorityAsync()");

            var reqData = new { Priority = 1 };
            var status = await _channel.RequestAsync(MethodId.CONSUMER_SET_PRIORITY, Internal, reqData);
            var responseData = JsonConvert.DeserializeObject<ConsumerSetOrUnsetPriorityResponseData>(status!);

            Priority = responseData.Priority;
        }

        /// <summary>
        /// Request a key frame to the Producer.
        /// </summary>
        public Task RequestKeyFrameAsync()
        {
            _logger.LogDebug("RequestKeyFrameAsync()");

            return _channel.RequestAsync(MethodId.CONSUMER_REQUEST_KEY_FRAME, Internal);
        }

        /// <summary>
        /// Enable 'trace' event.
        /// </summary>
        public Task EnableTraceEventAsync(TraceEventType[] types)
        {
            _logger.LogDebug("EnableTraceEventAsync()");

            var reqData = new
            {
                Types = types ?? Array.Empty<TraceEventType>()
            };
            return _channel.RequestAsync(MethodId.CONSUMER_ENABLE_TRACE_EVENT, Internal, reqData);
        }

        #region Event Handlers

        private void HandleWorkerNotifications()
        {
            _channel.MessageEvent += OnChannelMessage;
        }

        private void OnChannelMessage(string targetId, string @event, string data)
        {
            if (targetId != ConsumerId)
            {
                return;
            }

            switch (@event)
            {
                case "producerclose":
                    {
                        if (Closed)
                        {
                            break;
                        }

                        Closed = true;

                        // Remove notification subscriptions.
                        _channel.MessageEvent -= OnChannelMessage;

                        Emit("@producerclose");
                        Emit("producerclose");

                        // Emit observer event.
                        Observer.Emit("close");

                        break;
                    }
                case "producerpause":
                    {
                        if (ProducerPaused)
                        {
                            break;
                        }

                        var wasPaused = Paused || ProducerPaused;

                        ProducerPaused = true;

                        Emit("producerpause");

                        // Emit observer event.
                        if (!wasPaused)
                        {
                            Observer.Emit("pause");
                        }

                        break;
                    }
                case "producerresume":
                    {
                        if (!ProducerPaused)
                        {
                            break;
                        }

                        var wasPaused = Paused || ProducerPaused;

                        ProducerPaused = false;

                        Emit("producerresume");

                        // Emit observer event.
                        if (wasPaused && !Paused)
                        {
                            Observer.Emit("resume");
                        }

                        break;
                    }
                case "score":
                    {
                        var score = JsonConvert.DeserializeObject<ConsumerScore>(data);
                        Score = score;

                        Emit("score", score);

                        // Emit observer event.
                        Observer.Emit("score", score);

                        break;
                    }
                case "layerschange":
                    {
                        var layers = !data.IsNullOrWhiteSpace() ? JsonConvert.DeserializeObject<ConsumerLayers>(data) : null;

                        CurrentLayers = layers;

                        Emit("layerschange", layers);

                        // Emit observer event.
                        Observer.Emit("layersChange", layers);

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
