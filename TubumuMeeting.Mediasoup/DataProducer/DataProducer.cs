using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using TubumuMeeting.Libuv;
using TubumuMeeting.Libuv.Threading;
using TubumuMeeting.Mediasoup.Extensions;
using Microsoft.Extensions.Logging;
using Tubumu.Core.Extensions;

namespace TubumuMeeting.Mediasoup
{
    public class DataProducer
    {
        // Logger
        private readonly ILoggerFactory _loggerFactory;
        private readonly ILogger<DataProducer> _logger;

        #region Internal data.

        public string RouterId { get; }

        public string TransportId { get; }

        /// <summary>
        /// DataProducer id.
        /// </summary>
        public string Id { get; }

        private object _internal;

        #endregion

        #region Producer data.

        /// <summary>
        /// SCTP stream parameters.
        /// </summary>
        public SctpStreamParameters SctpStreamParameters { get; }

        /// <summary>
        /// DataChannel label.
        /// </summary>
        public string Label { get; }

        /// <summary>
        /// DataChannel protocol.
        /// </summary>
        public string Protocol { get; }

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
        /// Whether the DataConsumer is closed.
        /// </summary>
        public bool Closed { get; private set; }

        /// <summary>
        /// Observer instance.
        /// </summary>
        public DataProducerObserver Observer { get; } = new DataProducerObserver();

        #region Events

        public event Action? CloseEvent;

        public event Action? DataProducerCloseEvent;

        public event Action? TransportCloseEvent;

        #endregion

        public DataProducer(ILoggerFactory loggerFactory,
                            string routerId,
                            string transportId,
                            string dataProducerId,
                            SctpStreamParameters sctpStreamParameters,
                            string label,
                            string protocol,
                            Channel channel,
                            object? appData)
        {
            _loggerFactory = loggerFactory;
            _logger = loggerFactory.CreateLogger<DataProducer>(); RouterId = routerId;
            RouterId = routerId;
            TransportId = transportId;
            Id = dataProducerId;
            _internal = new
            {
                RouterId,
                TransportId,
                DataConsumerId = Id
            };
            SctpStreamParameters = sctpStreamParameters;
            Label = label;
            Protocol = protocol;
            Channel = channel;
            AppData = appData;

            HandleWorkerNotifications();
        }

        /// <summary>
        /// Close the DataProducer.
        /// </summary>
        public void Close()
        {
            if (Closed)
                return;

            _logger.LogDebug("Close()");

            Closed = true;

            // Fire and forget
            Channel.RequestAsync(MethodId.DATA_PRODUCER_CLOSE.GetEnumStringValue(), new
            {
                RouterId,
                TransportId,
                DataProducerId = Id,
            }).ContinueWithOnFaultedHandleLog(_logger);

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
            return Channel.RequestAsync(MethodId.DATA_PRODUCER_DUMP.GetEnumStringValue(), _internal);
        }

        /// <summary>
        /// Get DataProducer stats.
        /// </summary>
        public Task<string?> GetStatsAsync()
        {
            _logger.LogDebug("GetStatsAsync()");
            return Channel.RequestAsync(MethodId.DATA_PRODUCER_GET_STATS.GetEnumStringValue(), _internal);
        }

        #region Event Handlers

        private void HandleWorkerNotifications()
        {
            // No need to subscribe to any event.
        }

        #endregion
    }
}
