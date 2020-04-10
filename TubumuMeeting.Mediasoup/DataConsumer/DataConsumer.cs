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
    public class DataConsumer
    {
        // Logger
        private readonly ILogger<DataConsumer> _logger;

        #region Internal data.

        public string RouterId { get; }

        public string TransportId { get; }

        /// <summary>
        /// Associated DataProducer id.
        /// </summary>
        public string DataProducerId { get; }

        /// <summary>
        /// DataConsumer id.
        /// </summary>
        public string Id { get; }

        private object _internal;

        #endregion

        #region DataConsumer data.

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
        public DataConsumerObserver Observer { get; } = new DataConsumerObserver();

        #region Events

        public event Action? CloseEvent;

        public event Action? DataProducerCloseEvent;

        public event Action? TransportCloseEvent;

        #endregion

        public DataConsumer(Logger<DataConsumer> logger,
                            string routerId,
                            string transportId,
                            string dataProducerId,
                            string dataConsumerId,
                            SctpStreamParameters sctpStreamParameters,
                            string label,
                            string protocol,
                            Channel channel,
                            object? appData)
        {
            _logger = logger;
            RouterId = routerId;
            TransportId = transportId;
            DataProducerId = dataProducerId;
            Id = dataConsumerId;
            _internal = new
            {
                RouterId,
                TransportId,
                DataProducerId,
                DataConsumerId = Id,
            };
            SctpStreamParameters = sctpStreamParameters;
            Label = label;
            Protocol = protocol;
            Channel = channel;
            AppData = appData;

            HandleWorkerNotifications();
        }

        /// <summary>
        /// Close the DataConsumer.
        /// </summary>
        public void Close()
        {
            if (Closed)
                return;

            _logger.LogDebug("Close()");

            Closed = true;

            // Remove notification subscriptions.
            Channel.MessageEvent -= OnChannelMessage;

            // Fire and forget
            Channel.RequestAsync(MethodId.DATA_CONSUMER_CLOSE.GetEnumStringValue(), new
            {
                RouterId,
                TransportId,
                DataProducerId,
                DataConsumerId = Id,
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

            // Remove notification subscriptions.
            Channel.MessageEvent -= OnChannelMessage;

            TransportCloseEvent?.Invoke();

            // Emit observer event.
            Observer.EmitClose();
        }

        /// <summary>
        /// Dump DataConsumer.
        /// </summary>
        public Task<string?> DumpAsync()
        {
            _logger.LogDebug("DumpAsync()");
            return Channel.RequestAsync(MethodId.DATA_CONSUMER_DUMP.GetEnumStringValue(), _internal);
        }

        /// <summary>
        /// Get DataConsumer stats.
        /// </summary>
        public Task<string?> GetStatsAsync()
        {
            _logger.LogDebug("GetStatsAsync()");
            return Channel.RequestAsync(MethodId.DATA_CONSUMER_GET_STATS.GetEnumStringValue(), _internal);
        }

        #region Event Handlers

        private void HandleWorkerNotifications()
        {
            Channel.MessageEvent += OnChannelMessage;
        }

        private void OnChannelMessage(string target, string @event, string data)
        {
            if (target != Id) return;
            switch (@event)
            {
                case "dataproducerclose":
                    {
                        if (Closed)
                            break;
                        Closed = true;

                        Channel.MessageEvent -= OnChannelMessage;

                        DataProducerCloseEvent?.Invoke();

                        // Emit observer event.
                        Observer.EmitClose();

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
