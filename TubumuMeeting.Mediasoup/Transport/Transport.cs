using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Tubumu.Core.Extensions;
using TubumuMeeting.Mediasoup.Extensions;

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
        protected readonly Func<RtpCapabilities> GetRouterRtpCapabilities;

        // Method to retrieve a Producer.
        protected readonly Func<string, Producer> GetProducerById;

        // Method to retrieve a DataProducer.
        protected readonly Func<string, DataProducer> GetDataProducerById;

        // Producers map.
        protected readonly Dictionary<string, Producer> Producers = new Dictionary<string, Producer>();

        // Consumers map.
        protected readonly Dictionary<string, Consumer> Consumers = new Dictionary<string, Consumer>();

        // DataProducers map.
        protected readonly Dictionary<string, DataProducer> DataProducers = new Dictionary<string, DataProducer>();

        // DataConsumers map.
        protected readonly Dictionary<string, DataConsumer> DataConsumers = new Dictionary<string, DataConsumer>();

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

        /// <summary>
        /// Close the Transport.
        /// </summary>
        public void Close()
        {
            if (Closed)
                return;

            _logger.LogDebug("Close()");

            Closed = true;


            // Fire and forget
            Channel.RequestAsync(MethodId.TRANSPORT_CLOSE.GetEnumStringValue(), _internal).ContinueWithOnFaultedHandleLog(_logger);

            // Close every Producer.
            foreach (var producer in Producers.Values)
            {
                producer.TransportClosed();

                // Must tell the Router.
                ProducerCloseEvent?.Invoke(producer);
            }
            Producers.Clear();

            // Close every Consumer.
            foreach (var consumer in Consumers.Values)
            {
                consumer.TransportClosed();
            }
            Consumers.Clear();

            // Close every DataProducer.
            foreach (var dataProducer in DataProducers.Values)
            {
                dataProducer.TransportClosed();

                // Must tell the Router.
                DataProducerCloseEvent?.Invoke(dataProducer);
            }
            DataProducers.Clear();

            // Close every DataConsumer.
            foreach (var dataConsumer in DataConsumers.Values)
            {
                dataConsumer.TransportClosed();
            }
            DataConsumers.Clear();

            CloseEvent?.Invoke();

            // Emit observer event.
            Observer.EmitClose();
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

            // Close every Producer.
            foreach (var producer in Producers.Values)
            {
                producer.TransportClosed();

                // NOTE: No need to tell the Router since it already knows (it has
                // been closed in fact).
            }

            Producers.Clear();

            // Close every Consumer.
            foreach (var consumer in Consumers.Values)
            {
                consumer.TransportClosed();
            }
            Consumers.Clear();

            // Close every DataProducer.
            foreach (var dataProducer in DataProducers.Values)
            {
                dataProducer.TransportClosed();

                // NOTE: No need to tell the Router since it already knows (it has
                // been closed in fact).
            }
            DataProducers.Clear();

            // Close every DataConsumer.
            foreach (var dataConsumer in DataConsumers.Values)
            {
                dataConsumer.TransportClosed();
            }
            DataConsumers.Clear();

            RouterCloseEvent?.Invoke();

            // Emit observer event.
            Observer.EmitClose();
        }

        /// <summary>
        /// Dump Transport.
        /// </summary>
        public Task<string?> DumpAsync()
        {
            _logger.LogDebug("DumpAsync()");
            return Channel.RequestAsync(MethodId.TRANSPORT_DUMP.GetEnumStringValue(), _internal);
        }

        /// <summary>
        /// Get Transport stats.
        /// </summary>
        public Task<string?> GetStatsAsync()
        {
            _logger.LogDebug("GetStatsAsync()");
            return Channel.RequestAsync(MethodId.TRANSPORT_GET_STATS.GetEnumStringValue(), _internal);
        }

        public abstract Task Connect(object @params);

        /// <summary>
        /// Set maximum incoming bitrate for receiving media.
        /// </summary>
        /// <param name="bitrate"></param>
        /// <returns></returns>
        public Task<string?> SetMaxIncomingBitrateAsync(int bitrate)
        {
            _logger.LogDebug($"setMaxIncomingBitrate() [bitrate:{bitrate}]");

            var reqData = new { Bitrate = bitrate };
            return Channel.RequestAsync(MethodId.TRANSPORT_SET_MAX_INCOMING_BITRATE.GetEnumStringValue(), _internal, reqData);
        }

        /// <summary>
        /// Create a Producer.
        /// </summary>
        public Task<Producer> ProduceAsync(ProducerOptions producerOptions)
        {
            _logger.LogDebug("ProduceAsync()");

            if(!producerOptions.Id.IsNullOrWhiteSpace() && Producers.ContainsKey(producerOptions.Id!))
            {
                throw new Exception($"a Producer with same id \"{ producerOptions.Id }\" already exists");
            }

            // This may throw.
            ORTC.ValidateRtpParameters(producerOptions.RtpParameters);

            // If missing or empty encodings, add one.
            // TODO: (alby)注意检查这样做是否合适
            if (producerOptions.RtpParameters.Encodings.IsNullOrEmpty())
            {
                producerOptions.RtpParameters.Encodings = new RtpEncodingParameters[0];
            }

            // Don't do this in PipeTransports since there we must keep CNAME value in
            // each Producer.
            if (GetType() != typeof(PipeTransport))
            {
                // If CNAME is given and we don't have yet a CNAME for Producers in this
                // Transport, take it.
                if (_cnameForProducers.IsNullOrWhiteSpace() && producerOptions.RtpParameters.Rtcp!=null && !producerOptions.RtpParameters.Rtcp.CNAME.IsNullOrWhiteSpace())
                {
                    _cnameForProducers = producerOptions.RtpParameters.Rtcp.CNAME;
                }
                // Otherwise if we don't have yet a CNAME for Producers and the RTP parameters
                // do not include CNAME, create a random one.
                else if (_cnameForProducers.IsNullOrWhiteSpace())
                {
                    this._cnameForProducers = Guid.NewGuid().ToString().Substring(0, 8);
                }

                // Override Producer's CNAME.
                // TODO: (alby)注意检查这样做是否合适
                producerOptions.RtpParameters.Rtcp = producerOptions.RtpParameters.Rtcp ?? new RtcpParameters();
                producerOptions.RtpParameters.Rtcp.CNAME = _cnameForProducers;
            }

            var routerRtpCapabilities = GetRouterRtpCapabilities();

            // This may throw.
            var rtpMapping = ORTC.GetProducerRtpParametersMapping(producerOptions.RtpParameters, routerRtpCapabilities);

            // This may throw.
            const consumableRtpParameters = ORTC.GetConsumableRtpParameters(kind, rtpParameters, routerRtpCapabilities, rtpMapping);

            const internal = { ...this._internal, producerId: id || uuidv4() };
        const reqData = { kind, rtpParameters, rtpMapping, keyFrameRequestDelay, paused };

        const status =
            await this._channel.request('transport.produce', internal, reqData);

		const data =
        {
            kind,
            rtpParameters,
            type : status.type,
            consumableRtpParameters
        };

        const producer = new Producer(
			{

                internal,
				data,
				channel : this._channel,
                appData,
                paused

        });


        this._producers.set(producer.id, producer);

        producer.on('@close', () =>
		{

            this._producers.delete(producer.id);

            this.emit('@producerclose', producer);
    });


        this.emit('@newproducer', producer);

		// Emit observer event.
		this._observer.safeEmit('newproducer', producer);

		return producer;
    }

    /**
     * Create a Consumer.
     *
     * @virtual
     */
    async consume(
    
        {
        producerId,
			rtpCapabilities,
			paused = false,
			preferredLayers,
			appData = { }
    }: ConsumerOptions
	): Promise<Consumer>
	{
		logger.debug('consume()');

		if (!producerId || typeof producerId !== 'string')
			throw new TypeError('missing producerId');
		else if (appData && typeof appData !== 'object')
			throw new TypeError('if given, appData must be an object');

    // This may throw.
    ortc.validateRtpCapabilities(rtpCapabilities);

		const producer = this._getProducerById(producerId);

		if (!producer)
			throw Error(`Producer with id "${producerId}" not found`);

    // This may throw.
    const rtpParameters = ortc.getConsumerRtpParameters(
        producer.consumableRtpParameters, rtpCapabilities);

    // Set MID.
    rtpParameters.mid = `${this._nextMidForConsumers++}`;

		// We use up to 8 bytes for MID (string).
		if (this._nextMidForConsumers === 100000000)

        {
        logger.error(
				`consume() | reaching max MID value "${this._nextMidForConsumers}"`);

        this._nextMidForConsumers = 0;
    }

		const internal = { ...this._internal, consumerId: uuidv4(), producerId };
    const reqData =
    {
            kind                   : producer.kind,
            rtpParameters,
            type                   : producer.type,
            consumableRtpEncodings : producer.consumableRtpParameters.encodings,
            paused,
            preferredLayers
        };

    const status =
        await this._channel.request('transport.consume', internal, reqData);

		const data = { kind: producer.kind, rtpParameters, type: producer.type };

    const consumer = new Consumer(
			{

                internal,
				data,
				channel         : this._channel,
                appData,
                paused          : status.paused,
                producerPaused  : status.producerPaused,
                score           : status.score,
                preferredLayers : status.preferredLayers

            });


        this._consumers.set(consumer.id, consumer);

        consumer.on('@close', () => this._consumers.delete(consumer.id));
    consumer.on('@producerclose', () => this._consumers.delete(consumer.id));

		// Emit observer event.
		this._observer.safeEmit('newconsumer', consumer);

		return consumer;
    }
}
}
