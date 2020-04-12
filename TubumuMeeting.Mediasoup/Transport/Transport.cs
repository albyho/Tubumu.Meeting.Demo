using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Tubumu.Core.Extensions;
using Tubumu.Core.Extensions.Object;
using TubumuMeeting.Mediasoup.Extensions;

namespace TubumuMeeting.Mediasoup
{
    public abstract class Transport
    {
        // Logger
        private readonly ILoggerFactory _loggerFactory;
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
        private int _nextMidForConsumers = 0;

        // Buffer with available SCTP stream ids.
        private int[]? _sctpStreamIds;

        // Next SCTP stream id.
        private int _nextSctpStreamId;

        /// <summary>
        /// Observer instance.
        /// </summary>
        public TransportObserver Observer { get; } = new TransportObserver();

        #region Events

        public event Action? RouterCloseEvent;

        public event Action? CloseEvent;

        public event Action<Producer>? NewProducerEvent;

        public event Action<Producer>? ProducerCloseEvent;

        public event Action<DataProducer>? NewDataProducerEvent;

        public event Action<DataProducer>? DataProducerCloseEvent;

        #endregion

        public Transport(ILoggerFactory loggerFactory,
            string routerId,
            string transportId,
            SctpParameters sctpParameters,
            SctpState sctpState,
            Channel channel,
            object? appData,
            Func<RtpCapabilities> getRouterRtpCapabilities,
            Func<string, Producer> getProducerById,
            Func<string, DataProducer> getDataProducerById)
        {
            _loggerFactory = loggerFactory;
            _logger = loggerFactory.CreateLogger<Transport>();
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

        /// <summary>
        /// Provide the Transport remote parameters.
        /// </summary>
        /// <param name="params"></param>
        /// <returns></returns>
        public abstract Task ConnectAsync(object @params);

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
        public async Task<Producer> ProduceAsync(ProducerOptions producerOptions)
        {
            _logger.LogDebug("ProduceAsync()");

            if (!producerOptions.Id.IsNullOrWhiteSpace() && Producers.ContainsKey(producerOptions.Id!))
            {
                throw new Exception($"a Producer with same id \"{ producerOptions.Id }\" already exists");
            }

            // This may throw.
            ORTC.ValidateRtpParameters(producerOptions.RtpParameters);

            // If missing or empty encodings, add one.
            // TODO: (alby)注意检查这样做是否合适
            if (producerOptions.RtpParameters.Encodings.IsNullOrEmpty())
            {
                producerOptions.RtpParameters.Encodings = new List<RtpEncodingParameters>();
            }

            // Don't do this in PipeTransports since there we must keep CNAME value in
            // each Producer.
            if (GetType() != typeof(PipeTransport))
            {
                // If CNAME is given and we don't have yet a CNAME for Producers in this
                // Transport, take it.
                if (_cnameForProducers.IsNullOrWhiteSpace() && producerOptions.RtpParameters.Rtcp != null && !producerOptions.RtpParameters.Rtcp.CNAME.IsNullOrWhiteSpace())
                {
                    _cnameForProducers = producerOptions.RtpParameters.Rtcp.CNAME;
                }
                // Otherwise if we don't have yet a CNAME for Producers and the RTP parameters
                // do not include CNAME, create a random one.
                else if (_cnameForProducers.IsNullOrWhiteSpace())
                {
                    _cnameForProducers = Guid.NewGuid().ToString().Substring(0, 8);
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
            var consumableRtpParameters = ORTC.GetConsumableRtpParameters(producerOptions.Kind, producerOptions.RtpParameters, routerRtpCapabilities, rtpMapping);

            var @internal = new
            {
                RouterId,
                TransportId,
                ProducerId = producerOptions.Id.IsNullOrWhiteSpace() ? Guid.NewGuid().ToString() : producerOptions.Id!,
            };
            var reqData = new
            {
                producerOptions.Kind,
                producerOptions.RtpParameters,
                RtpMapping = rtpMapping,
                producerOptions.KeyFrameRequestDelay,
                producerOptions.Paused,
            };

            var status = await Channel.RequestAsync(MethodId.TRANSPORT_PRODUCE.GetEnumStringValue(), @internal, reqData);
            var transportProduceResponseData = JsonConvert.DeserializeObject<TransportProduceResponseData>(status);
            var data = new
            {
                producerOptions.Kind,
                producerOptions.RtpParameters,
                transportProduceResponseData.Type,
                ConsumableRtpParameters = consumableRtpParameters
            };

            var producer = new Producer(_loggerFactory,
                @internal.RouterId,
                @internal.TransportId,
                @internal.ProducerId,
                data.Kind,
                data.RtpParameters,
                data.Type,
                data.ConsumableRtpParameters,
                Channel,
                AppData,
                producerOptions.Paused.Value);

            Producers[producer.Id] = producer;

            producer.CloseEvent += () =>
            {
                Producers.Remove(producer.Id);
                ProducerCloseEvent?.Invoke(producer);
            };

            NewProducerEvent?.Invoke(producer);

            // Emit observer event.
            Observer.EmitNewProducer(producer);

            return producer;
        }

        public async Task<Consumer> ConsumeAsync(ConsumerOptions consumerOptions)
        {
            _logger.LogDebug("ConsumeAsync()");

            if (consumerOptions.ProducerId.IsNullOrWhiteSpace())
            {
                throw new Exception("missing producerId");
            }

            // This may throw.
            ORTC.ValidateRtpCapabilities(consumerOptions.RtpCapabilities);

            var producer = GetProducerById(consumerOptions.ProducerId);

            if (producer == null)
                throw new Exception($"Producer with id {consumerOptions.ProducerId} not found");

            // This may throw.
            var rtpParameters = ORTC.GetConsumerRtpParameters(producer.ConsumableRtpParameters, consumerOptions.RtpCapabilities);

            // Set MID.
            rtpParameters.Mid = $"{_nextMidForConsumers++}";

            // We use up to 8 bytes for MID (string).
            if (_nextMidForConsumers == 100000000)

            {
                _logger.LogDebug($"consume() | reaching max MID value {_nextMidForConsumers}");
                _nextMidForConsumers = 0;
            }

            var @internal = new
            {
                RouterId,
                TransportId,
                ConsumerId = Guid.NewGuid().ToString(),
                consumerOptions.ProducerId,
            };

            var reqData = new
            {
                producer.Kind,
                RtpParameters = rtpParameters,
                producer.Type,
                ConsumableRtpEncodings = producer.ConsumableRtpParameters.Encodings,
                consumerOptions.Paused,
                consumerOptions.PreferredLayers
            };

            var status = await Channel.RequestAsync(MethodId.TRANSPORT_CONSUME.GetEnumStringValue(), @internal, reqData);
            var transportConsumeResponseData = JsonConvert.DeserializeObject<TransportConsumeResponseData>(status);

            var data = new
            {
                producer.Kind,
                RtpParameters = rtpParameters,
                Type = (ConsumerType)producer.Type, // 注意：类型转换
            };

            var consumer = new Consumer(_loggerFactory,
                @internal.RouterId,
                @internal.TransportId,
                @internal.ConsumerId,
                @internal.ProducerId,
                data.Kind,
                data.RtpParameters,
                data.Type,
                Channel,
                AppData,
                transportConsumeResponseData.Paused,
                transportConsumeResponseData.ProducerPaused,
                transportConsumeResponseData.Score,
                transportConsumeResponseData.PreferredLayers);

            Consumers[consumer.Id] = consumer;

            consumer.CloseEvent += () => Consumers.Remove(consumer.Id);
            consumer.ProducerCloseEvent += () => Consumers.Remove(consumer.Id);

            // Emit observer event.
            Observer.EmitNewConsumer(consumer);

            return consumer;
        }

        /// <summary>
        /// Create a DataProducer.
        /// </summary>
        /// <returns></returns>
        public async Task<DataProducer> ProduceDataAsync(DataProducerOptions dataProducerOptions)
        {
            _logger.LogDebug("ProduceDataAsync()");

            if (!dataProducerOptions.Id.IsNullOrWhiteSpace() && DataProducers.ContainsKey(dataProducerOptions.Id))
                throw new Exception($"a DataProducer with same id {dataProducerOptions.Id} already exists");

            // This may throw.
            ORTC.ValidateSctpStreamParameters(dataProducerOptions.SctpStreamParameters);

            var @internal = new
            {
                RouterId,
                TransportId,
                DataProducerId = !dataProducerOptions.Id.IsNullOrWhiteSpace() ? dataProducerOptions.Id : Guid.NewGuid().ToString(),
            };

            var reqData = new
            {
                dataProducerOptions.SctpStreamParameters,
                dataProducerOptions.Label,
                dataProducerOptions.Protocol
            };

            var status = await Channel.RequestAsync(MethodId.TRANSPORT_PRODUCE_DATA.GetEnumStringValue(), @internal, reqData);
            var transportProduceResponseData = JsonConvert.DeserializeObject<TransportDataProduceResponseData>(status);
            var dataProducer = new DataProducer(_loggerFactory,
                @internal.RouterId,
                @internal.TransportId,
                @internal.DataProducerId,
                transportProduceResponseData.SctpStreamParameters,
                transportProduceResponseData.Label,
                transportProduceResponseData.Protocol,
                Channel,
                AppData);

            DataProducers[dataProducer.Id] = dataProducer;

            dataProducer.CloseEvent += () =>
            {
                DataProducers.Remove(dataProducer.Id);
                DataProducerCloseEvent?.Invoke(dataProducer);
            };

            NewDataProducerEvent?.Invoke(dataProducer);

            // Emit observer event.
            Observer.EmitNewDataProducer(dataProducer);

            return dataProducer;
        }

        /// <summary>
        /// Create a DataConsumer.
        /// </summary>
        /// <param name="dataConsumerOptions"></param>
        /// <returns></returns>
        public async Task<DataConsumer> ConsumeDataAsync(DataConsumerOptions dataConsumerOptions)
        {
            _logger.LogDebug("ConsumeDataAsync()");

            if (dataConsumerOptions.DataProducerId.IsNullOrWhiteSpace())
                throw new Exception("missing dataProducerId");

            var dataProducer = GetDataProducerById(dataConsumerOptions.DataProducerId);

            if (dataProducer == null)
                throw new Exception($"DataProducer with id {dataConsumerOptions.DataProducerId} not found");

            var sctpStreamParameters = dataProducer.SctpStreamParameters.DeepClone<SctpStreamParameters>();

            // This may throw.
            var sctpStreamId = GetNextSctpStreamId();

            _sctpStreamIds[sctpStreamId] = 1;

            sctpStreamParameters.StreamId = sctpStreamId;

            var @internal = new
            {
                RouterId,
                TransportId,
                DataConsumerId = Guid.NewGuid().ToString(),
                dataConsumerOptions.DataProducerId,
            };

            var reqData = new
            {
                SctpStreamParameters = sctpStreamParameters,
                dataProducer.Label,
                dataProducer.Protocol
            };

            var status = await Channel.RequestAsync(MethodId.TRANSPORT_CONSUME_DATA.GetEnumStringValue(), @internal, reqData);
            var transportDataConsumeResponseData = JsonConvert.DeserializeObject<TransportDataConsumeResponseData>(status);

            var dataConsumer = new DataConsumer(_loggerFactory,
                @internal.RouterId,
                @internal.TransportId,
                @internal.DataProducerId,
                @internal.DataConsumerId,
                transportDataConsumeResponseData.SctpStreamParameters,
                transportDataConsumeResponseData.Label,
                transportDataConsumeResponseData.Protocol,
                Channel,
                AppData);

            DataConsumers[dataConsumer.Id] = dataConsumer;

            dataConsumer.CloseEvent += () =>
            {
                DataConsumers.Remove(dataConsumer.Id);
                _sctpStreamIds[sctpStreamId] = 0;
            };

            dataConsumer.DataProducerCloseEvent += () =>
            {
                DataConsumers.Remove(dataConsumer.Id);
                _sctpStreamIds[sctpStreamId] = 0;
            };

            // Emit observer event.
            Observer.EmitNewDataConsumer(dataConsumer);

            return dataConsumer;
        }

        /// <summary>
        /// Enable 'trace' event.
        /// </summary>
        /// <param name="types"></param>
        /// <returns></returns>
        public Task EnableTraceEventAsync(TransportTraceEventType[] types)
        {

            _logger.LogDebug("EnableTraceEventAsync()");

            var reqData = new { Types = types };

            return Channel.RequestAsync(MethodId.TRANSPORT_ENABLE_TRACE_EVENT.GetEnumStringValue(), _internal, reqData);
        }

        private int GetNextSctpStreamId()
        {
            if (SctpParameters == null)
            {
                throw new Exception("missing data.sctpParameters.MIS");
            }

            var numStreams = SctpParameters.MIS;

            if (_sctpStreamIds.IsNullOrEmpty())
                _sctpStreamIds = new int[numStreams];

            int sctpStreamId;

            for (var idx = 0; idx < _sctpStreamIds.Length; ++idx)

            {
                sctpStreamId = (_nextSctpStreamId + idx) % _sctpStreamIds.Length;

                if (_sctpStreamIds[sctpStreamId] == 0)
                {
                    _nextSctpStreamId = sctpStreamId + 1;
                    return sctpStreamId;
                }
            }

            throw new Exception("no sctpStreamId available");
        }
    }
}
