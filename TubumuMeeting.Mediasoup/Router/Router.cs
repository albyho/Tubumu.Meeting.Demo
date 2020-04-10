using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using TubumuMeeting.Mediasoup.Extensions;
using Microsoft.Extensions.Logging;
using Tubumu.Core.Extensions;

namespace TubumuMeeting.Mediasoup
{
    public class Router
    {
        // Logger
        private readonly ILogger<Router> _logger;

        #region Internal data.

        public string RouterId { get; }

        private object _internal;

        #endregion

        #region Router data.

        public RtpCapabilities RtpCapabilities { get; }

        private object _data;

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

        // Transports map.
        private readonly Dictionary<string, Transport> _transports = new Dictionary<string, Transport>();

        // Producers map.
        private readonly Dictionary<string, Producer> _producers = new Dictionary<string, Producer>();

        // RtpObservers map.
        private readonly Dictionary<string, RtpObserver> _rtpObservers = new Dictionary<string, RtpObserver>();

        // DataProducers map.
        private readonly Dictionary<string, DataProducer> _dataProducers = new Dictionary<string, DataProducer>();

        // Router to PipeTransport map.
        private readonly Dictionary<Router, PipeTransport[]> _mapRouterPipeTransports = new Dictionary<Router, PipeTransport[]>();

        // TODO: (alby)完善
        // AwaitQueue instance to make pipeToRouter tasks happen sequentially.
        //private readonly _pipeToRouterQueue = new AwaitQueue({ ClosedErrorClass: InvalidStateError });

        /// <summary>
        /// Observer instance.
        /// </summary>
        public RouterObserver Observer { get; } = new RouterObserver();

        #region Events

        public event Action? CloseEvent;

        public event Action? WorkerCloseEvent;

        #endregion

        public Router(Logger<Router> logger,
                    string routerId,
                    RtpCapabilities rtpCapabilities,
                    Channel channel,
                    object? appData)
        {
            _logger = logger;
            RouterId = routerId;
            _internal = new
            {
                RouterId,
            };
            RtpCapabilities = rtpCapabilities;
            _data = new
            {
                RtpCapabilities,
            };

            Channel = channel;
            AppData = appData;
        }

        /// <summary>
        /// Close the Router.
        /// </summary>
        public void Close()
        {
            if (Closed)
                return;

            _logger.LogDebug("Close()");

            Closed = true;

            // Fire and forget
            Channel.RequestAsync(MethodId.ROUTER_CLOSE.GetEnumStringValue(), _internal).ContinueWithOnFaultedHandleLog(_logger);

            // Close every Transport.
            foreach (var transport in _transports.Values)
            {
                // TODO: (alby)完善
                //transport.RouterClosed();
            }

            _transports.Clear();

            // Clear the Producers map.
            _producers.Clear();

            // Close every RtpObserver.
            foreach (var rtpObserver in _rtpObservers.Values)
            {
                // TODO: (alby)完善
                //rtpObserver.RouterClosed();
            }
            _rtpObservers.Clear();

            // Clear the DataProducers map.
            _dataProducers.Clear();

            // Clear map of Router/PipeTransports.
            _mapRouterPipeTransports.Clear();

            // TODO: (alby)完善
            // Close the pipeToRouter AwaitQueue instance.
            //_pipeToRouterQueue.close();

            CloseEvent?.Invoke();

            // Emit observer event.
            Observer.EmitClose();
        }

        /// <summary>
        /// Worker was closed.
        /// </summary>
        public void WorkerClosed()
        {
            if (Closed)
                return;

            _logger.LogDebug("WorkerClosed()");

            Closed = true;

            // Close every Transport.
            foreach (var transport in _transports.Values)
            {
                // TODO: (alby)完善
                //transport.RouterClosed();
            }

            _transports.Clear();

            // Clear the Producers map.
            _producers.Clear();

            // Close every RtpObserver.
            foreach (var rtpObserver in _rtpObservers.Values)
            {
                // TODO: (alby)完善
                //rtpObserver.RouterClosed();
            }
            _rtpObservers.Clear();

            // Clear the DataProducers map.
            _dataProducers.Clear();

            // Clear map of Router/PipeTransports.
            _mapRouterPipeTransports.Clear();

            WorkerCloseEvent?.Invoke();

            // Emit observer event.
            Observer.EmitClose();
        }

        /// <summary>
        /// Dump Router.
        /// </summary>
        public Task<string?> DumpAsync()
        {
            _logger.LogDebug("DumpAsync()");
            return Channel.RequestAsync(MethodId.ROUTER_DUMP.GetEnumStringValue(), _internal);
        }

        /// <summary>
        /// Create a WebRtcTransport.
        /// </summary>
        public async Task<WebRtcTransport> CreateWebRtcTransportAsync(WebRtcTransportOptions webRtcTransportOptions)
        {
            _logger.LogDebug("CreateWebRtcTransportAsync()");

            var @internal = new
            {
                RouterId,
                TransportId = Guid.NewGuid().ToString(),
            };
            var reqData = new
            {
                webRtcTransportOptions.ListenIps,
                webRtcTransportOptions.EnableUdp,
                webRtcTransportOptions.EnableTcp,
                webRtcTransportOptions.PreferUdp,
                webRtcTransportOptions.PreferTcp,
                webRtcTransportOptions.InitialAvailableOutgoingBitrate,
                webRtcTransportOptions.EnableSctp,
                webRtcTransportOptions.NumSctpStreams,
                webRtcTransportOptions.MaxSctpMessageSize,
                IsDataChannel = true
            };

            var data = await Channel.RequestAsync(MethodId.ROUTER_CREATE_WEBRTC_TRANSPORT.GetEnumStringValue(), @internal, reqData);

            var transport = new WebRtcTransport(
                {

                internal,
				data,
				channel                  : this._channel,
                appData,
                getRouterRtpCapabilities : (): RtpCapabilities => this._data.rtpCapabilities,
                getProducerById          : (producerId: string): Producer => (
                    this._producers.get(producerId)
                ),
				getDataProducerById : (dataProducerId: string): DataProducer => (
                    this._dataProducers.get(dataProducerId)
                )
            });


        this._transports.set(transport.id, transport);

        transport.on('@close', () => this._transports.delete(transport.id));
        transport.on('@newproducer', (producer: Producer) => this._producers.set(producer.id, producer));
        transport.on('@producerclose', (producer: Producer) => this._producers.delete(producer.id));
        transport.on('@newdataproducer', (dataProducer: DataProducer) => (
        this._dataProducers.set(dataProducer.id, dataProducer)
        ));
        transport.on('@dataproducerclose', (dataProducer: DataProducer) => (


            this._dataProducers.delete(dataProducer.id)
        ));

        // Emit observer event.
        this._observer.safeEmit('newtransport', transport);

        return transport;
}

    /// <summary>
    /// Create a PlainTransport.
    /// </summary>
    async createPlainTransport(


        {
        listenIp,
			rtcpMux = true,
			comedia = false,
			enableSctp = false,
			numSctpStreams = { OS: 1024, MIS: 1024 },
			maxSctpMessageSize = 262144,
			enableSrtp = false,
			srtpCryptoSuite = 'AES_CM_128_HMAC_SHA1_80',
			appData = { }
    }: PlainTransportOptions
	): Promise<PlainTransport>

    {
    logger.debug('createPlainTransport()');

    if (!listenIp)
        throw new TypeError('missing listenIp');
    else if (appData && typeof appData !== 'object')
        throw new TypeError('if given, appData must be an object');

    if (typeof listenIp === 'string' && listenIp)
    {
        listenIp = { ip: listenIp
};
    }
    else if (typeof listenIp === 'object')
    {
        listenIp =

            {
            ip: listenIp.ip,
				announcedIp: listenIp.announcedIp || undefined

            };
    }
    else
    {
        throw new TypeError('wrong listenIp');
    }

    const internal = { ...this._internal, transportId: uuidv4() };
const reqData = {
    listenIp,
            rtcpMux,
            comedia,
            enableSctp,
            numSctpStreams,
            maxSctpMessageSize,
            isDataChannel: false,
            enableSrtp,
            srtpCryptoSuite

        };

const data =
    await this._channel.request('router.createPlainTransport', internal, reqData);

		const transport = new PlainTransport(
			{

                internal,
				data,
				channel                  : this._channel,
                appData,
                getRouterRtpCapabilities : (): RtpCapabilities => this._data.rtpCapabilities,
                getProducerById          : (producerId: string): Producer => (
                    this._producers.get(producerId)
                ),
				getDataProducerById : (dataProducerId: string): DataProducer => (
                    this._dataProducers.get(dataProducerId)
                )

            });


        this._transports.set(transport.id, transport);

        transport.on('@close', () => this._transports.delete(transport.id));
transport.on('@newproducer', (producer: Producer) => this._producers.set(producer.id, producer));
transport.on('@producerclose', (producer: Producer) => this._producers.delete(producer.id));
transport.on('@newdataproducer', (dataProducer: DataProducer) => (
    this._dataProducers.set(dataProducer.id, dataProducer)
));
transport.on('@dataproducerclose', (dataProducer: DataProducer) => (
    this._dataProducers.delete(dataProducer.id)
));

// Emit observer event.
this._observer.safeEmit('newtransport', transport);

return transport;
}

/// <summary>
/// DEPRECATED: Use createPlainTransport().
/// </summary>
async createPlainRtpTransport(
		options: PlainTransportOptions
	): Promise<PlainTransport>

    {
    logger.warn(
        'createPlainRtpTransport() is DEPRECATED, use createPlainTransport()');

    return this.createPlainTransport(options);
}

/// <summary>
/// Create a PipeTransport.
/// </summary>
async createPipeTransport(

        {
    listenIp,
			enableSctp = false,
			numSctpStreams = { OS: 1024, MIS: 1024 },
			maxSctpMessageSize = 1073741823,
			enableRtx = false,
			enableSrtp = false,
			appData = { }
}: PipeTransportOptions
	): Promise<PipeTransport>

    {
    logger.debug('createPipeTransport()');

    if (!listenIp)
        throw new TypeError('missing listenIp');
    else if (appData && typeof appData !== 'object')
        throw new TypeError('if given, appData must be an object');

    if (typeof listenIp === 'string' && listenIp)
    {
        listenIp = { ip: listenIp };
    }
    else if (typeof listenIp === 'object')
    {
        listenIp =

            {
            ip: listenIp.ip,
				announcedIp: listenIp.announcedIp || undefined

            };
    }
    else
    {
        throw new TypeError('wrong listenIp');
    }

    const internal = { ...this._internal, transportId: uuidv4() };
		const reqData = {
    listenIp,
			enableSctp,
			numSctpStreams,
			maxSctpMessageSize,
			isDataChannel: false,
			enableRtx,
			enableSrtp

        };

		const data =
			await this._channel.request('router.createPipeTransport', internal, reqData);

		const transport = new PipeTransport(
			{

                internal,
				data,
				channel                  : this._channel,
                appData,
                getRouterRtpCapabilities : (): RtpCapabilities => this._data.rtpCapabilities,
                getProducerById          : (producerId: string): Producer => (
                    this._producers.get(producerId)
                ),
				getDataProducerById : (dataProducerId: string): DataProducer => (
                    this._dataProducers.get(dataProducerId)
                )

            });


        this._transports.set(transport.id, transport);

        transport.on('@close', () => this._transports.delete(transport.id));
transport.on('@newproducer', (producer: Producer) => this._producers.set(producer.id, producer));
transport.on('@producerclose', (producer: Producer) => this._producers.delete(producer.id));
transport.on('@newdataproducer', (dataProducer: DataProducer) => (
    this._dataProducers.set(dataProducer.id, dataProducer)
));
transport.on('@dataproducerclose', (dataProducer: DataProducer) => (
    this._dataProducers.delete(dataProducer.id)
));

// Emit observer event.
this._observer.safeEmit('newtransport', transport);

return transport;
}

/// <summary>
/// Pipes the given Producer or DataProducer into another Router in same host.
/// </summary>
async pipeToRouter(

        {
    producerId,
			dataProducerId,
			router,
			listenIp = '127.0.0.1',
			enableSctp = true,
			numSctpStreams = { OS: 1024, MIS: 1024 },
			enableRtx = false,
			enableSrtp = false

        }: PipeToRouterOptions
	): Promise<PipeToRouterResult>

    {
    if (!producerId && !dataProducerId)
        throw new TypeError('missing producerId or dataProducerId');
    else if (producerId && dataProducerId)
        throw new TypeError('just producerId or dataProducerId can be given');
    else if (!router)
        throw new TypeError('Router not found');
    else if (router === this)
        throw new TypeError('cannot use this Router as destination');

    let producer: Producer;
    let dataProducer: DataProducer;

    if (producerId)
    {
        producer = this._producers.get(producerId);

        if (!producer)
            throw new TypeError('Producer not found');
    }
    else if (dataProducerId)
    {
        dataProducer = this._dataProducers.get(dataProducerId);

        if (!dataProducer)
            throw new TypeError('DataProducer not found');
    }

    // Here we may have to create a new PipeTransport pair to connect source and
    // destination Routers. We just want to keep a PipeTransport pair for each
    // pair of Routers. Since this operation is async, it may happen that two
    // simultaneous calls to router1.pipeToRouter({ producerId: xxx, router: router2 })
    // would end up generating two pairs of PipeTranports. To prevent that, let's
    // use an async queue.

    let localPipeTransport: PipeTransport;
    let remotePipeTransport: PipeTransport;

    await this._pipeToRouterQueue.push(async () =>
    {
    let pipeTransportPair = this._mapRouterPipeTransports.get(router);

    if (pipeTransportPair)
    {
        localPipeTransport = pipeTransportPair[0];
        remotePipeTransport = pipeTransportPair[1];
    }
    else
    {
        try
        {
            pipeTransportPair = await Promise.all(


                [
                    this.createPipeTransport(

                                { listenIp, enableSctp, numSctpStreams, enableRtx, enableSrtp }),

							router.createPipeTransport(

                                { listenIp, enableSctp, numSctpStreams, enableRtx, enableSrtp })
						]);

    localPipeTransport = pipeTransportPair[0];
    remotePipeTransport = pipeTransportPair[1];

    await Promise.all(


        [
            localPipeTransport.connect(

                                {
        ip: remotePipeTransport.tuple.localIp,
									port: remotePipeTransport.tuple.localPort,
									srtpParameters: remotePipeTransport.srtpParameters

                                }),

							remotePipeTransport.connect(

                                {
        ip: localPipeTransport.tuple.localIp,
									port: localPipeTransport.tuple.localPort,
									srtpParameters: localPipeTransport.srtpParameters

                                })
						]);

    localPipeTransport.observer.on('close', () =>
    {
        remotePipeTransport.close();
        this._mapRouterPipeTransports.delete(router);
    });

    remotePipeTransport.observer.on('close', () =>
    {
        localPipeTransport.close();
        this._mapRouterPipeTransports.delete(router);
    });

    this._mapRouterPipeTransports.set(
        router, [localPipeTransport, remotePipeTransport]);
}
				catch (error)

                {
    logger.error(
        'pipeToRouter() | error creating PipeTransport pair:%o',
        error);

    if (localPipeTransport)
        localPipeTransport.close();

    if (remotePipeTransport)
        remotePipeTransport.close();

    throw error;
}
}
		});

		if (producer)
		{
			let pipeConsumer: Consumer;
			let pipeProducer: Producer;

			try
			{
				pipeConsumer = await localPipeTransport.consume({ producerId });

				pipeProducer = await remotePipeTransport.produce(

                    {
    id: producer.id,
						kind: pipeConsumer.kind,
						rtpParameters: pipeConsumer.rtpParameters,
						paused: pipeConsumer.producerPaused,
						appData: producer.appData

                    });

				// Pipe events from the pipe Consumer to the pipe Producer.
				pipeConsumer.observer.on('close', () => pipeProducer.close());
				pipeConsumer.observer.on('pause', () => pipeProducer.pause());
				pipeConsumer.observer.on('resume', () => pipeProducer.resume());

				// Pipe events from the pipe Producer to the pipe Consumer.
				pipeProducer.observer.on('close', () => pipeConsumer.close());

				return { pipeConsumer, pipeProducer };
			}
			catch (error)
			{
				logger.error(
					'pipeToRouter() | error creating pipe Consumer/Producer pair:%o',
					error);

				if (pipeConsumer)
                    pipeConsumer.close();

				if (pipeProducer)
                    pipeProducer.close();

				throw error;
			}
		}
		else if (dataProducer)
		{
			let pipeDataConsumer: DataConsumer;
			let pipeDataProducer: DataProducer;

			try
			{
				pipeDataConsumer = await localPipeTransport.consumeData(

                    {
    dataProducerId

                    });

				pipeDataProducer = await remotePipeTransport.produceData(

                    {
    id: dataProducer.id,
						sctpStreamParameters: pipeDataConsumer.sctpStreamParameters,
						label: pipeDataConsumer.label,
						protocol: pipeDataConsumer.protocol,
						appData: dataProducer.appData

                    });

				// Pipe events from the pipe DataConsumer to the pipe DataProducer.
				pipeDataConsumer.observer.on('close', () => pipeDataProducer.close());

				// Pipe events from the pipe DataProducer to the pipe DataConsumer.
				pipeDataProducer.observer.on('close', () => pipeDataConsumer.close());

				return { pipeDataConsumer, pipeDataProducer };
			}
			catch (error)
			{
				logger.error(
					'pipeToRouter() | error creating pipe DataConsumer/DataProducer pair:%o',
					error);

				if (pipeDataConsumer)
                    pipeDataConsumer.close();

				if (pipeDataProducer)
                    pipeDataProducer.close();

				throw error;
			}
		}
		else
		{
			throw new Error('internal error');
		}
	}

	/// <summary>
	 /// Create an AudioLevelObserver.
	 /// </summary>
	async createAudioLevelObserver(

        {
    maxEntries = 1,
			threshold = -80,
			interval = 1000,
			appData = { }
}: AudioLevelObserverOptions = {}
	): Promise<AudioLevelObserver>
	{
		logger.debug('createAudioLevelObserver()');

		if (appData && typeof appData !== 'object')
			throw new TypeError('if given, appData must be an object');

const internal = { ...this._internal, rtpObserverId: uuidv4() };
const reqData = { maxEntries, threshold, interval };

await this._channel.request('router.createAudioLevelObserver', internal, reqData);

		const audioLevelObserver = new AudioLevelObserver(
			{

                internal,
				channel         : this._channel,
                appData,
                getProducerById : (producerId: string): Producer => (
                    this._producers.get(producerId)
                )

            });


        this._rtpObservers.set(audioLevelObserver.id, audioLevelObserver);

        audioLevelObserver.on('@close', () =>
		{

            this._rtpObservers.delete(audioLevelObserver.id);

        });

        // Emit observer event.
        this._observer.safeEmit('newrtpobserver', audioLevelObserver);

		return audioLevelObserver;
}

/// <summary>
/// Check whether the given RTP capabilities can consume the given Producer.
/// </summary>
canConsume(

        {
    producerId,
			rtpCapabilities

        }:
		{
			producerId: string;
			rtpCapabilities: RtpCapabilities;
		}
	): boolean
	{
		const producer = this._producers.get(producerId);

		if (!producer)
		{
			logger.error(
				'canConsume() | Producer with id "%s" not found', producerId);

			return false;
		}

		try
		{
			return ortc.canConsume(producer.consumableRtpParameters, rtpCapabilities);
		}
		catch (error)
		{
			logger.error('canConsume() | unexpected error: %s', String(error));

			return false;
		}
	}
    }
}
