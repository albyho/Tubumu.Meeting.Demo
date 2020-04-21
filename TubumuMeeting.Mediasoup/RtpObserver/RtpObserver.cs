using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TubumuMeeting.Mediasoup.Extensions;

namespace TubumuMeeting.Mediasoup
{
    public class RtpObserverInternalData
    {
        /// <summary>
        /// Router id.
        /// </summary>
        public string RouterId { get; }

        /// <summary>
        /// RtpObserver id.
        /// </summary>
        public string RtpObserverId { get; }

        public RtpObserverInternalData(string routerId, string rtpObserverId)
        {
            RouterId = routerId;
            RtpObserverId = rtpObserverId;
        }
    }

    public class RtpObserver : EventEmitter
    {
        // Logger
        private readonly ILogger<RtpObserver> _logger;

        /// <summary>
        /// Internal data.
        /// </summary>
        public RtpObserverInternalData Internal { get; private set; }

        /// <summary>
        /// Channel instance.
        /// </summary>
        protected readonly Channel Channel;

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
        /// Method to retrieve a Producer.
        /// </summary>
        protected readonly Func<string, Producer> GetProducerById;

        /// <summary>
        /// Observer instance.
        /// </summary>
        public EventEmitter Observer { get; } = new EventEmitter();

        /// <summary>
        /// <para>Events:</para>
        /// <para>@emits routerclose</para>
        /// <para>@emits @close</para>
        /// <para>Observer events:</para>
        /// <para>@emits close</para>
        /// <para>@emits pause</para>
        /// <para>@emits resume</para>
        /// <para>@emits addproducer - (producer: Producer)</para>
        /// <para>@emits removeproducer - (producer: Producer)</para>
        /// </summary>
        /// <param name="loggerFactory"></param>
        /// <param name="rtpObserverInternalData"></param>
        /// <param name="channel"></param>
        /// <param name="appData"></param>
        /// <param name="getProducerById"></param>
        public RtpObserver(ILoggerFactory loggerFactory,
                    RtpObserverInternalData rtpObserverInternalData,
                    Channel channel,
                    Dictionary<string, object>? appData,
                    Func<string, Producer> getProducerById)
        {
            _logger = loggerFactory.CreateLogger<RtpObserver>();

            // Internal
            Internal = rtpObserverInternalData;

            Channel = channel;
            AppData = appData;
            GetProducerById = getProducerById;
        }

        /// <summary>
        /// Close the RtpObserver.
        /// </summary>
        public void Close()
        {
            if (Closed)
                return;

            _logger.LogDebug("Close()");

            Closed = true;

            // Fire and forget.
            Channel.RequestAsync(MethodId.RTP_OBSERVER_CLOSE, Internal).ContinueWithOnFaultedHandleLog(_logger);

            Emit("@close");

            // Emit observer event.
            Observer.Emit("close");
        }

        /// <summary>
        /// Router was closed.
        /// </summary>
        public void RouterClosed()
        {
            if (Closed)
                return;

            _logger.LogDebug("RouterClosed()");

            Closed = true;

            Emit("routerclose");

            // Emit observer event.
            Observer.Emit("close");
        }

        /// <summary>
        /// Pause the RtpObserver.
        /// </summary>
        public async Task PauseAsync()
        {
            _logger.LogDebug("PauseAsync()");

            var wasPaused = Paused;

            await Channel.RequestAsync(MethodId.RTP_OBSERVER_PAUSE, Internal);

            Paused = true;

            // Emit observer event.
            if (!wasPaused)
            {
                Observer.Emit("pause");
            }
        }

        /// <summary>
        /// Resume the RtpObserver.
        /// </summary>
        public async Task ResumeAsync()
        {
            _logger.LogDebug("ResumeAsync()");

            var wasPaused = Paused;

            await Channel.RequestAsync(MethodId.RTP_OBSERVER_RESUME, Internal);

            Paused = false;

            // Emit observer event.
            if (wasPaused)
            {
                Observer.Emit("resume");
            }
        }

        /// <summary>
        /// Add a Producer to the RtpObserver.
        /// </summary>
        public async Task AddProducerAsync(string producerId)
        {
            _logger.LogDebug("AddProducerAsync()");

            var producer = GetProducerById(producerId);
            var @internal = new
            {
                Internal.RouterId,
                Internal.RtpObserverId,
                ProducerId = producerId,
            };

            await Channel.RequestAsync(MethodId.RTP_OBSERVER_ADD_PRODUCER, @internal);

            // Emit observer event.
            Observer.Emit("addproducer", producer);
        }

        /// <summary>
        /// Remove a Producer from the RtpObserver.
        /// </summary>
        public async Task RemoveProducerAsync(string producerId)
        {
            _logger.LogDebug("RemoveProducerAsync()");

            var producer = GetProducerById(producerId);
            var @internal = new
            {
                Internal.RouterId,
                Internal.RtpObserverId,
                ProducerId = producerId,
            };
            await Channel.RequestAsync(MethodId.RTP_OBSERVER_REMOVE_PRODUCER, @internal);

            // Emit observer event.
            Observer.Emit("removeproducer", producer);
        }
    }
}
