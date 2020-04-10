using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Extensions.Logging;

namespace TubumuMeeting.Mediasoup
{
    public abstract class Transport
    {
        // Logger
        private readonly ILogger<Transport> _logger;

        #region Internal data.

        public string RouterId { get; }

        public string TransportId { get; }

        private object _internal;

        #endregion

        #region Transport data. This is set by the subclass.

        /// <summary>
        /// SCTP parameters.
        /// </summary>
        public SctpParameters? SctpParameters { get; }

        /// <summary>
        /// Sctp state.
        /// </summary>
        public SctpState? SctpState { get; }

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
        /// Whether the Transport is closed.
        /// </summary>
        public bool Closed { get; private set; }

        // Method to retrieve Router RTP capabilities.
        protected readonly Func<RtpCapabilities>  GetRouterRtpCapabilities;

	    // Method to retrieve a Producer.
	    protected readonly Func<string, Producer> GetProducerById;

	    // Method to retrieve a DataProducer.
	    protected readonly Func<string, DataProducer> GetDataProducerById;

        // Producers map.
        protected readonly Dictionary<string, Producer> _producers = new Dictionary<string, Producer>();

        // Consumers map.
        protected readonly Dictionary<string, Consumer> _consumers = new Dictionary<string, Consumer>();

        // DataProducers map.
        protected readonly Dictionary<string, DataProducer> _dataProducers = new Dictionary<string, DataProducer>();

        // DataConsumers map.
        protected readonly Dictionary<string, DataConsumer> _dataConsumers = new Dictionary<string, DataConsumer>();

        // RTCP CNAME for Producers.
        private string? _cnameForProducers;

        // Next MID for Consumers. It's converted into string when used.
        private int _nextMidForConsumers;

        // Buffer with available SCTP stream ids.
        private byte[]? _sctpStreamIds;

        // Next SCTP stream id.
        private int _nextSctpStreamId;

        /// <summary>
        /// Observer instance.
        /// </summary>
        public TransportObserver Observer { get; } = new TransportObserver();

        #region Events

        public event Action? RouterCloseEvent;

        public event Action? CloseEvent;

        public event Action<Producer>? NewProducer;

        public event Action<Producer>? ProducerCloseEvent;

        public event Action<DataProducer>? NewDataProducer;

        public event Action<DataProducer>? DataProducerCloseEvent;

        #endregion

        public Transport(Logger<Transport> logger,
            string routerId,
            string transportId,
            SctpParameters sctpParameters,
            SctpState sctpState,
            RtpParameters consumableRtpParameters,
            Channel channel,
            object? appData,
            Func<RtpCapabilities> getRouterRtpCapabilities,
            Func<string, Producer> getProducerById,
            Func<string, DataProducer> getDataProducerById)
        {
            _logger = logger;
            RouterId = routerId;
            TransportId = transportId;
            _internal = new
            {
                RouterId,
                TransportId,
            };
            SctpParameters = sctpParameters;
            SctpState = sctpState;
            Channel = channel;
            AppData = appData;
            GetRouterRtpCapabilities = getRouterRtpCapabilities;
            GetProducerById = getProducerById;
            GetDataProducerById = getDataProducerById;
        }


    }
}
