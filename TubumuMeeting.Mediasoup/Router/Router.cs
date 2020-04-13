using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using TubumuMeeting.Mediasoup.Extensions;
using Microsoft.Extensions.Logging;
using Tubumu.Core.Extensions;
using Newtonsoft.Json;

namespace TubumuMeeting.Mediasoup
{
    public class Router : EventEmitter
    {
        // Logger
        private readonly ILoggerFactory _loggerFactory;
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
        public EventEmitter Observer { get; } = new EventEmitter();

        /// <summary>
        /// <para>@emits workerclose</para>
        /// <para>@emits @close</para>
        /// <para>Observer:</para>
        /// <para>@emits close</para>
        /// <para>@emits newtransport - (transport: Transport)</para>
        /// <para>@emits newrtpobserver - (rtpObserver: RtpObserver)</para>  
        /// </summary>
        /// <param name="logger"></param>
        /// <param name="routerId"></param>
        /// <param name="rtpCapabilities"></param>
        /// <param name="channel"></param>
        /// <param name="appData"></param>
        public Router(ILoggerFactory loggerFactory,
                    string routerId,
                    RtpCapabilities rtpCapabilities,
                    Channel channel,
                    object? appData)
        {
            _loggerFactory = loggerFactory;
            _logger = loggerFactory.CreateLogger<Router>();
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
                transport.RouterClosed();
            }

            _transports.Clear();

            // Clear the Producers map.
            _producers.Clear();

            // Close every RtpObserver.
            foreach (var rtpObserver in _rtpObservers.Values)
            {
                rtpObserver.RouterClosed();
            }
            _rtpObservers.Clear();

            // Clear the DataProducers map.
            _dataProducers.Clear();

            // Clear map of Router/PipeTransports.
            _mapRouterPipeTransports.Clear();

            // TODO: (alby)完善
            // Close the pipeToRouter AwaitQueue instance.
            //_pipeToRouterQueue.close();

            Emit("@close");

            // Emit observer event.
            Observer.Emit("close");
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
                transport.RouterClosed();
            }

            _transports.Clear();

            // Clear the Producers map.
            _producers.Clear();

            // Close every RtpObserver.
            foreach (var rtpObserver in _rtpObservers.Values)
            {
                rtpObserver.RouterClosed();
            }
            _rtpObservers.Clear();

            // Clear the DataProducers map.
            _dataProducers.Clear();

            // Clear map of Router/PipeTransports.
            _mapRouterPipeTransports.Clear();

            Emit("workerclose");

            // Emit observer event.
            Observer.Emit("close");
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

            var status = await Channel.RequestAsync(MethodId.ROUTER_CREATE_WEBRTC_TRANSPORT.GetEnumStringValue(), @internal, reqData);
            var responseData = JsonConvert.DeserializeObject<RouterCreateWebRtcTransportResponseData>(status);

            var transport = new WebRtcTransport(_loggerFactory,
                @internal.RouterId,
                @internal.TransportId,
                sctpParameters: null,
                sctpState: null,
                Channel, AppData,
                () => RtpCapabilities,
                m => _producers[m],
                m => _dataProducers[m],
                responseData.IceRole,
                responseData.IceParameters,
                responseData.IceState,
                responseData.IceSelectedTuple,
                responseData.DtlsParameters,
                responseData.DtlsState,
                responseData.DtlsRemoteCert
                );
            _transports[transport.Id] = transport;

            transport.On("@close", _ => _transports.Remove(transport.Id));
            transport.On("@newproducer", obj =>
            {
                var producer = (Producer)obj!;
                _producers[producer.Id] = producer;
            });
            transport.On("@producerclose", obj =>
            {
                var producer = (Producer)obj!;
                _producers.Remove(producer.Id);
            });
            transport.On("@newdataproducer", obj =>
            {
                var dataProducer = (DataProducer)obj!;
                _dataProducers[dataProducer.Id] = dataProducer;
            });
            transport.On("@dataproducerclose", obj =>
            {
                var dataProducer = (DataProducer)obj!;
                _dataProducers.Remove(dataProducer.Id);
            });

            // Emit observer event.
            Observer.Emit("newtransport", transport);

            return transport;
        }

        /// <summary>
        /// Create a PlainTransport.
        /// </summary>
        public async Task<PlainTransport> CreatePlainTransportAsync(PlainTransportOptions plainTransportOptions)
        {
            _logger.LogDebug("CreatePlainTransportAsync()");

            if (plainTransportOptions.ListenIp == null || plainTransportOptions.ListenIp.Ip.IsNullOrWhiteSpace())
                throw new Exception("missing listenIp");

            var @internal = new
            {
                RouterId,
                TransportId = Guid.NewGuid().ToString(),
            };

            var reqData = new
            {
                plainTransportOptions.ListenIp,
                plainTransportOptions.Comedia,
                plainTransportOptions.EnableSctp,
                plainTransportOptions.NumSctpStreams,
                plainTransportOptions.MaxSctpMessageSize,
                IsDataChannel = false,
                plainTransportOptions.EnableSrtp,
                plainTransportOptions.SrtpCryptoSuite
            };

            var status = await Channel.RequestAsync(MethodId.ROUTER_CREATE_PLAIN_TRANSPORT.GetEnumStringValue(), @internal, reqData);
            var responseData = JsonConvert.DeserializeObject<RouterCreatePlainTransportResponseData>(status);

            var transport = new PlainTransport(_loggerFactory,
                            @internal.RouterId,
                            @internal.TransportId,
                            sctpParameters: null,
                            sctpState: null,
                            Channel, AppData,
                            () => RtpCapabilities,
                            m => _producers[m],
                            m => _dataProducers[m],
                            responseData.RtcpMux,
                            responseData.Comedia,
                            responseData.Tuple,
                            responseData.RtcpTuple,
                            responseData.SrtpParameters
                            );
            _transports[transport.Id] = transport;

            transport.On("@close", _ => _transports.Remove(transport.Id));
            transport.On("@newproducer", obj =>
            {
                var producer = (Producer)obj!;
                _producers[producer.Id] = producer;
            });
            transport.On("@producerclose", obj =>
            {
                var producer = (Producer)obj!;
                _producers.Remove(producer.Id);
            });
            transport.On("@newdataproducer", obj =>
            {
                var dataProducer = (DataProducer)obj!;
                _dataProducers[dataProducer.Id] = dataProducer;
            });
            transport.On("@dataproducerclose", obj =>
            {
                var dataProducer = (DataProducer)obj!;
                _dataProducers.Remove(dataProducer.Id);
            });

            // Emit observer event.
            Observer.Emit("newtransport", transport);

            return transport;
        }

        /// <summary>
        /// Create a PipeTransport.
        /// </summary>
        public async Task<PipeTransport> CreatePipeTransportAsync(PipeTransportOptions pipeTransportOptions)
        {
            _logger.LogDebug("CreatePipeTransportAsync()");

            var @internal = new
            {
                RouterId,
                TransportId = Guid.NewGuid().ToString(),
            };

            var reqData = new
            {
                pipeTransportOptions.ListenIp,
                pipeTransportOptions.EnableSctp,
                pipeTransportOptions.NumSctpStreams,
                pipeTransportOptions.MaxSctpMessageSize,
                IsDataChannel = false,
                pipeTransportOptions.EnableRtx,
                pipeTransportOptions.EnableSrtp,
            };

            var status = await Channel.RequestAsync(MethodId.ROUTER_CREATE_PIPE_TRANSPORT.GetEnumStringValue(), @internal, reqData);
            var responseData = JsonConvert.DeserializeObject<RouterCreatePipeTransportResponseData>(status);

            var transport = new PipeTransport(_loggerFactory,
                            @internal.RouterId,
                            @internal.TransportId,
                            sctpParameters: null,
                            sctpState: null,
                            Channel, AppData,
                            () => RtpCapabilities,
                            m => _producers[m],
                            m => _dataProducers[m],
                            responseData.Tuple,
                            responseData.Rtx,
                            responseData.SrtpParameters
                            );

            _transports[transport.Id] = transport;

            transport.On("@close", _ => _transports.Remove(transport.Id));
            transport.On("@newproducer", obj =>
            {
                var producer = (Producer)obj!;
                _producers[producer.Id] = producer;
            });
            transport.On("@producerclose", obj =>
            {
                var producer = (Producer)obj!;
                _producers.Remove(producer.Id);
            });
            transport.On("@newdataproducer", obj =>
            {
                var dataProducer = (DataProducer)obj!;
                _dataProducers[dataProducer.Id] = dataProducer;
            });
            transport.On("@dataproducerclose", obj =>
            {
                var dataProducer = (DataProducer)obj!;
                _dataProducers.Remove(dataProducer.Id);
            });

            // Emit observer event.
            Observer.Emit("newtransport", transport);

            return transport;
        }

        #region PipeToRouterAsync
        /*
        /// <summary>
        /// Pipes the given Producer or DataProducer into another Router in same host.
        /// </summary>
        public async Task<PipeToRouterResult> PipeToRouterAsync(PipeToRouterOptions pipeToRouterOptions)
        {

            if (pipeToRouterOptions.ProducerId.IsNullOrWhiteSpace() && pipeToRouterOptions.DataProducerId.IsNullOrWhiteSpace())
                throw new Exception("missing producerId or dataProducerId");

            if (!pipeToRouterOptions.ProducerId.IsNullOrWhiteSpace() && !pipeToRouterOptions.DataProducerId.IsNullOrWhiteSpace())
                throw new Exception("just producerId or dataProducerId can be given");

            if (pipeToRouterOptions.Router == null)
                throw new Exception("Router not found");

            if (pipeToRouterOptions.Router == null)
                throw new Exception("cannot use this Router as destination");

            Producer producer = null;
            DataProducer dataProducer = null;

            if (!pipeToRouterOptions.ProducerId.IsNullOrWhiteSpace())
            {
                producer = _producers[pipeToRouterOptions.ProducerId!];

                if (producer == null)
                    throw new Exception("Producer not found");
            }
            else if (!pipeToRouterOptions.DataProducerId.IsNullOrWhiteSpace())
            {
                dataProducer = _dataProducers[pipeToRouterOptions.DataProducerId!];

                if (dataProducer == null)
                    throw new Exception("DataProducer not found");
            }

            // Here we may have to create a new PipeTransport pair to connect source and
            // destination Routers. We just want to keep a PipeTransport pair for each
            // pair of Routers. Since this operation is async, it may happen that two
            // simultaneous calls to router1.pipeToRouter({ producerId: xxx, router: router2 })
            // would end up generating two pairs of PipeTranports. To prevent that, let's
            // use an async queue.

            PipeTransport localPipeTransport = null;
            PipeTransport remotePipeTransport = null;

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
*/
        #endregion

        /// <summary>
        /// Create an AudioLevelObserver.
        /// </summary>
        public async Task<AudioLevelObserver> CreateAudioLevelObserverAsync(AudioLevelObserverOptions audioLevelObserverOptions)
        {
            _logger.LogDebug("createAudioLevelObserver()");

            var @internal = new
            {
                RouterId,
                RtpObserverId = Guid.NewGuid().ToString(),
            };

            var reqData = new
            {
                audioLevelObserverOptions.MaxEntries,
                audioLevelObserverOptions.Threshold,
                audioLevelObserverOptions.Interval
            };

            var status = await Channel.RequestAsync(MethodId.ROUTER_CREATE_AUDIO_LEVEL_OBSERVER.GetEnumStringValue(), @internal, reqData);
            var responseData = JsonConvert.DeserializeObject<RouterCreatePipeTransportResponseData>(status);

            var audioLevelObserver = new AudioLevelObserver(_loggerFactory,
                @internal.RouterId,
                @internal.RtpObserverId,
                Channel,
                AppData,
                m => _producers[m]);

            _rtpObservers[audioLevelObserver.Id] = audioLevelObserver;

            audioLevelObserver.On("@close", _ => _rtpObservers.Remove(audioLevelObserver.Id));

            // Emit observer event.
            Observer.Emit("newrtpobserver", audioLevelObserver);

            return audioLevelObserver;
        }

        /// <summary>
        /// Check whether the given RTP capabilities can consume the given Producer.
        /// </summary>
        public bool CanConsume(string producerId, RtpCapabilities rtpCapabilities)
        {
            var producer = _producers[producerId];
            if (producer == null)
            {
                _logger.LogError($"canConsume() | Producer with id {producerId} not found");
                return false;
            }

            try
            {
                return ORTC.CanConsume(producer.ConsumableRtpParameters, rtpCapabilities);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "canConsume() | unexpected error");
                return false;
            }
        }
    }
}
