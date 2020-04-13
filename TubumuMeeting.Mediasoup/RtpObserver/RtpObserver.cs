using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Tubumu.Core.Extensions;
using TubumuMeeting.Mediasoup.Extensions;

namespace TubumuMeeting.Mediasoup
{
    public class RtpObserver : EventEmitter
    {
        // Logger
        private readonly ILoggerFactory _loggerFactory;
        private readonly ILogger<RtpObserver> _logger;

        #region Internal data.

        public string RouterId { get; }

        public string RtpObserverId { get; }

        private object _internal;

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

        // Method to retrieve a Producer.
        protected readonly Func<string, Producer> GetProducerById;

        public EventEmitter Observer { get; } = new EventEmitter();

        /// <summary>
        /// @emits routerclose
        /// @emits @close
        /// Observer:
        /// @emits close
        /// @emits pause
        /// @emits resume
        /// @emits addproducer - (producer: Producer)
        /// @emits removeproducer - (producer: Producer)
        /// </summary>
        /// <param name="loggerFactory"></param>
        /// <param name="routerId"></param>
        /// <param name="rtpObserverId"></param>
        /// <param name="channel"></param>
        /// <param name="appData"></param>
        /// <param name="getProducerById"></param>
        public RtpObserver(ILoggerFactory loggerFactory,
                    string routerId,
                    string rtpObserverId,
                    Channel channel,
                    object? appData,
                    Func<string, Producer> getProducerById)
        {
            _loggerFactory = loggerFactory;
            _logger = loggerFactory.CreateLogger<RtpObserver>();
            RouterId = routerId;
            RtpObserverId = rtpObserverId;
            _internal = new
            {
                RouterId,
                RtpObserverId,
            };
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
            Channel.RequestAsync(MethodId.RTP_OBSERVER_CLOSE.GetEnumStringValue(), _internal).ContinueWithOnFaultedHandleLog(_logger);

            Emit("@close");

            // Emit observer event.
            Observer.Emit("close");
        }

        /// <summary>
        /// Router was closed.
        ///
        /// @private
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

            await Channel.RequestAsync(MethodId.RTP_OBSERVER_PAUSE.GetEnumStringValue(), _internal);

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

            await Channel.RequestAsync(MethodId.RTP_OBSERVER_RESUME.GetEnumStringValue(), _internal);

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
                RouterId,
                RtpObserverId,
                ProducerId = producerId,
            };

            await Channel.RequestAsync(MethodId.RTP_OBSERVER_ADD_PRODUCER.GetEnumStringValue(), _internal);

            // Emit observer event.
            Observer.Emit("addproducer", producer);
        }

        /// <summary>
        /// Remove a Producer from the RtpObserver.
        /// </summary>
        public async Task RemoveProducerAsync(string producerId)
        {
            _logger.LogDebug("AddProducerAsync()");

            var producer = GetProducerById(producerId);
            var @internal = new
            {
                RouterId,
                RtpObserverId,
                ProducerId = producerId,
            };
            await Channel.RequestAsync(MethodId.RTP_OBSERVER_REMOVE_PRODUCER.GetEnumStringValue(), _internal);

            // Emit observer event.
            Observer.Emit("removeproducer", producer);
        }
    }
}
