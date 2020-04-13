using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TubumuMeeting.Mediasoup.Extensions;

namespace TubumuMeeting.Mediasoup
{
    public class DataConsumer : EventEmitter
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

        private readonly object _internal;

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
        public EventEmitter Observer { get; } = new EventEmitter();

        /// <summary>
        /// <para>@emits transportclose</para>
        /// <para>@emits dataproducerclose</para>
        /// <para>@emits @close</para>
        /// <para>@emits @dataproducerclose</para>
        /// <para>Observer:</para>
        /// <para>@emits close</para>
        /// </summary>
        /// <param name="loggerFactory"></param>
        /// <param name="routerId"></param>
        /// <param name="transportId"></param>
        /// <param name="dataProducerId"></param>
        /// <param name="dataConsumerId"></param>
        /// <param name="sctpStreamParameters"></param>
        /// <param name="label"></param>
        /// <param name="protocol"></param>
        /// <param name="channel"></param>
        /// <param name="appData"></param>
        public DataConsumer(ILoggerFactory loggerFactory,
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
            _logger = loggerFactory.CreateLogger<DataConsumer>(); RouterId = routerId;
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
            Channel.RequestAsync(MethodId.DATA_CONSUMER_CLOSE, new
            {
                RouterId,
                TransportId,
                DataProducerId,
                DataConsumerId = Id,
            }).ContinueWithOnFaultedHandleLog(_logger);

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
                return;

            _logger.LogDebug("TransportClosed()");

            Closed = true;

            // Remove notification subscriptions.
            Channel.MessageEvent -= OnChannelMessage;

            Emit("transportclose");

            // Emit observer event.
            Observer.Emit("close");
        }

        /// <summary>
        /// Dump DataConsumer.
        /// </summary>
        public Task<string?> DumpAsync()
        {
            _logger.LogDebug("DumpAsync()");
            return Channel.RequestAsync(MethodId.DATA_CONSUMER_DUMP, _internal);
        }

        /// <summary>
        /// Get DataConsumer stats.
        /// </summary>
        public Task<string?> GetStatsAsync()
        {
            _logger.LogDebug("GetStatsAsync()");
            return Channel.RequestAsync(MethodId.DATA_CONSUMER_GET_STATS, _internal);
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
                case "dataproducerclose":
                    {
                        if (Closed)
                            break;
                        Closed = true;

                        Channel.MessageEvent -= OnChannelMessage;

                        Emit("@dataproducerclose");
                        Emit("dataproducerclose");

                        // Emit observer event.
                        Observer.Emit("close");

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
