using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.Threading;
using Newtonsoft.Json;
using Tubumu.Core.Extensions;
using Tubumu.Core.Extensions.Object;
using TubumuMeeting.Mediasoup.Extensions;

namespace TubumuMeeting.Mediasoup
{
    public class TransportInternalData
    {
        /// <summary>
        /// Router id.
        /// </summary>
        public string RouterId { get; }

        /// <summary>
        /// Trannsport id.
        /// </summary>
        public string TransportId { get; }

        public TransportInternalData(string routerId, string transportId)
        {
            RouterId = routerId;
            TransportId = transportId;
        }
    }

    public abstract class Transport : EventEmitter
    {
        /// <summary>
        /// Logger factory for create logger.
        /// </summary>
        private readonly ILoggerFactory _loggerFactory;

        /// <summary>
        /// Logger.
        /// </summary>
        private readonly ILogger<Transport> _logger;

        /// <summary>
        /// Internal data.
        /// </summary>
        protected TransportInternalData Internal { get; set; }

        /// <summary>
        /// Trannsport id.
        /// </summary>
        public string TransportId => Internal.TransportId;

        #region Transport data. This is set by the subclass.

        /// <summary>
        /// SCTP parameters.
        /// </summary>
        public SctpParameters? SctpParameters { get; protected set; }

        /// <summary>
        /// Sctp state.
        /// </summary>
        public SctpState? SctpState { get; protected set; }

        #endregion

        /// <summary>
        /// Channel instance.
        /// </summary>
        protected readonly Channel Channel;

        /// <summary>
        /// PayloadChannel instance.
        /// </summary>
        protected readonly PayloadChannel PayloadChannel;

        /// <summary>
        /// App custom data.
        /// </summary>
        public Dictionary<string, object>? AppData { get; private set; }

        /// <summary>
        /// Whether the Transport is closed.
        /// </summary>
        public bool Closed { get; private set; }

        /// <summary>
        /// Method to retrieve Router RTP capabilities.
        /// </summary>
        protected readonly Func<RtpCapabilities> GetRouterRtpCapabilities;

        /// <summary>
        /// Method to retrieve a Producer.
        /// </summary>
        protected readonly Func<string, Producer?> GetProducerById;

        /// <summary>
        /// Method to retrieve a DataProducer.
        /// </summary>
        protected readonly Func<string, DataProducer?> GetDataProducerById;

        /// <summary>
        /// Producers map.
        /// </summary>
        protected readonly Dictionary<string, Producer> Producers = new Dictionary<string, Producer>();

        /// <summary>
        /// Consumers map.
        /// </summary>
        protected readonly Dictionary<string, Consumer> Consumers = new Dictionary<string, Consumer>();

        /// <summary>
        /// DataProducers map.
        /// </summary>
        protected readonly Dictionary<string, DataProducer> DataProducers = new Dictionary<string, DataProducer>();

        /// <summary>
        /// DataConsumers map.
        /// </summary>
        protected readonly Dictionary<string, DataConsumer> DataConsumers = new Dictionary<string, DataConsumer>();

        /// <summary>
        /// Locker.
        /// </summary>
        protected readonly AsyncReaderWriterLock Locker = new AsyncReaderWriterLock();

        /// <summary>
        /// RTCP CNAME for Producers.
        /// </summary>
        private string? _cnameForProducers;

        /// <summary>
        /// Next MID for Consumers. It's converted into string when used.
        /// </summary>
        private int _nextMidForConsumers = 0;

        /// <summary>
        /// Buffer with available SCTP stream ids.
        /// </summary>
        private int[]? _sctpStreamIds;

        /// <summary>m
        /// Next SCTP stream id.
        /// </summary>
        private int _nextSctpStreamId;

        /// <summary>
        /// Observer instance.
        /// </summary>
        public EventEmitter Observer { get; } = new EventEmitter();

        /// <summary>
        /// <para>Events:</para>
        /// <para>@emits routerclose</para>
        /// <para>@emits @close</para>
        /// <para>@emits @newproducer - (producer: Producer)</para>
        /// <para>@emits @producerclose - (producer: Producer)</para>
        /// <para>@emits @newdataproducer - (dataProducer: DataProducer)</para>
        /// <para>@emits @dataproducerclose - (dataProducer: DataProducer)</para>
        /// <para>Observer events:</para>
        /// <para>@emits close</para>
        /// <para>@emits newproducer - (producer: Producer)</para>
        /// <para>@emits newconsumer - (producer: Producer)</para>
        /// <para>@emits newdataproducer - (dataProducer: DataProducer)</para>
        /// <para>@emits newdataconsumer - (dataProducer: DataProducer)</para>
        /// </summary>
        /// <param name="loggerFactory"></param>
        /// <param name="transportInternalData"></param>
        /// <param name="sctpParameters"></param>
        /// <param name="sctpState"></param>
        /// <param name="channel"></param>
        /// <param name="payloadChannel"></param>
        /// <param name="appData"></param>
        /// <param name="getRouterRtpCapabilities"></param>
        /// <param name="getProducerById"></param>
        /// <param name="getDataProducerById"></param>
        protected Transport(ILoggerFactory loggerFactory,
            TransportInternalData transportInternalData,
            SctpParameters? sctpParameters,
            SctpState? sctpState,
            Channel channel,
            PayloadChannel payloadChannel,
            Dictionary<string, object>? appData,
            Func<RtpCapabilities> getRouterRtpCapabilities,
            Func<string, Producer?> getProducerById,
            Func<string, DataProducer?> getDataProducerById
            )
        {
            _loggerFactory = loggerFactory;
            _logger = loggerFactory.CreateLogger<Transport>();

            // Internal
            Internal = transportInternalData;

            // Data
            SctpParameters = sctpParameters;
            SctpState = sctpState;

            Channel = channel;
            PayloadChannel = payloadChannel;
            AppData = appData;
            GetRouterRtpCapabilities = getRouterRtpCapabilities;
            GetProducerById = getProducerById;
            GetDataProducerById = getDataProducerById;
        }

        /// <summary>
        /// Close the Transport.
        /// </summary>
        public virtual async Task CloseAsync()
        {
            if (Closed)
            {
                return;
            }

            _logger.LogDebug("Close()");

            Closed = true;

            // Remove notification subscriptions.
            //_channel.MessageEvent -= OnChannelMessage;

            // Fire and forget
            Channel.RequestAsync(MethodId.TRANSPORT_CLOSE, Internal).ContinueWithOnFaultedHandleLog(_logger);

            using (await Locker.WriteLockAsync())
            {
                // Close every Producer.
                foreach (var producer in Producers.Values)
                {
                    producer.TransportClosed();

                    // Must tell the Router.
                    Emit("@producerclose", producer);
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
                    Emit("@dataproducerclose", dataProducer);
                }
                DataProducers.Clear();

                // Close every DataConsumer.
                foreach (var dataConsumer in DataConsumers.Values)
                {
                    dataConsumer.TransportClosed();
                }
                DataConsumers.Clear();
            }

            Emit("@close");

            // Emit observer event.
            Observer.Emit("close");
        }

        /// <summary>
        /// Router was closed.
        /// </summary>
        public virtual async Task RouterClosedAsync()
        {
            if (Closed)
            {
                return;
            }

            _logger.LogDebug("RouterClosed()");

            Closed = true;

            // Remove notification subscriptions.
            //_channel.MessageEvent -= OnChannelMessage;

            using (await Locker.WriteLockAsync())
            {
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
            }

            Emit("routerclose");

            // Emit observer event.
            Observer.Emit("close");
        }

        /// <summary>
        /// Dump Transport.
        /// </summary>
        public Task<string?> DumpAsync()
        {
            _logger.LogDebug("DumpAsync()");

            return Channel.RequestAsync(MethodId.TRANSPORT_DUMP, Internal);
        }

        /// <summary>
        /// Get Transport stats.
        /// </summary>
        public virtual Task<string?> GetStatsAsync()
        {
            // 在 Node.js 实现中，Transport 类没有实现 getState 方法。
            _logger.LogDebug("GetStatsAsync()");

            return Channel.RequestAsync(MethodId.TRANSPORT_GET_STATS, Internal);
        }

        /// <summary>
        /// Provide the Transport remote parameters.
        /// </summary>
        /// <param name="params"></param>
        /// <returns></returns>
        public abstract Task ConnectAsync(object parameters);

        /// <summary>
        /// Set maximum incoming bitrate for receiving media.
        /// </summary>
        /// <param name="bitrate"></param>
        /// <returns></returns>
        public virtual Task<string?> SetMaxIncomingBitrateAsync(int bitrate)
        {
            _logger.LogDebug($"SetMaxIncomingBitrateAsync() [bitrate:{bitrate}]");

            var reqData = new { Bitrate = bitrate };
            return Channel.RequestAsync(MethodId.TRANSPORT_SET_MAX_INCOMING_BITRATE, Internal, reqData);
        }

        /// <summary>
        /// Create a Producer.
        /// </summary>
        public virtual async Task<Producer> ProduceAsync(ProducerOptions producerOptions)
        {
            _logger.LogDebug("ProduceAsync()");

            using (await Locker.WriteLockAsync())
            {
                if (!producerOptions.Id.IsNullOrWhiteSpace() && Producers.ContainsKey(producerOptions.Id!))
                {
                    throw new Exception($"a Producer with same id \"{ producerOptions.Id }\" already exists");
                }

                // This may throw.
                ORTC.ValidateRtpParameters(producerOptions.RtpParameters);

                // If missing or empty encodings, add one.
                // TODO: (alby)注意检查这样做是否合适
                // 在 mediasoup-worker 中，要求 Encodings 至少要有一个元素。
                if (producerOptions.RtpParameters.Encodings.IsNullOrEmpty())
                {
                    producerOptions.RtpParameters.Encodings = new List<RtpEncodingParameters>
                {
                    new RtpEncodingParameters()
                };
                }

                // Don't do this in PipeTransports since there we must keep CNAME value in
                // each Producer.
                // TODO: (alby)反模式
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

                var @internal = new ProducerInternalData
                (
                    Internal.RouterId,
                    Internal.TransportId,
                    producerOptions.Id.NullOrWhiteSpaceReplace(Guid.NewGuid().ToString())
                );
                var reqData = new
                {
                    producerOptions.Kind,
                    producerOptions.RtpParameters,
                    RtpMapping = rtpMapping,
                    producerOptions.KeyFrameRequestDelay,
                    producerOptions.Paused,
                };

                var status = await Channel.RequestAsync(MethodId.TRANSPORT_PRODUCE, @internal, reqData);
                var responseData = JsonConvert.DeserializeObject<TransportProduceResponseData>(status!);
                var data = new
                {
                    producerOptions.Kind,
                    producerOptions.RtpParameters,
                    responseData.Type,
                    ConsumableRtpParameters = consumableRtpParameters
                };

                var producer = new Producer(_loggerFactory,
                    @internal,
                    data.Kind,
                    data.RtpParameters,
                    data.Type,
                    data.ConsumableRtpParameters,
                    Channel,
                    PayloadChannel,
                    producerOptions.AppData,
                    producerOptions.Paused!.Value);


                Producers[producer.ProducerId] = producer;

                producer.On("@close", async _ =>
                {
                    using (await Locker.WriteLockAsync())
                    {
                        Producers.Remove(producer.ProducerId);
                    }
                    Emit("@producerclose", producer);
                });

                Emit("@newproducer", producer);

                // Emit observer event.
                Observer.Emit("newproducer", producer);

                return producer;
            }
        }

        /// <summary>
        /// Create a Consumer.
        /// </summary>
        /// <param name="consumerOptions"></param>
        /// <returns></returns>
        public virtual async Task<Consumer> ConsumeAsync(ConsumerOptions consumerOptions)
        {
            _logger.LogDebug("ConsumeAsync()");

            using (await Locker.WriteLockAsync())
            {
                if (consumerOptions.ProducerId.IsNullOrWhiteSpace())
                {
                    throw new ArgumentException("missing producerId");
                }

                if (consumerOptions.RtpCapabilities == null)
                {
                    throw new ArgumentException(nameof(consumerOptions.RtpCapabilities));
                }

                if (!consumerOptions.Paused.HasValue)
                {
                    consumerOptions.Paused = false;
                }

                // This may throw.
                ORTC.ValidateRtpCapabilities(consumerOptions.RtpCapabilities);

                var producer = GetProducerById(consumerOptions.ProducerId);
                if (producer == null)
                {
                    throw new NullReferenceException($"Producer with id {consumerOptions.ProducerId} not found");
                }

                // This may throw.
                var rtpParameters = ORTC.GetConsumerRtpParameters(producer.ConsumableRtpParameters, consumerOptions.RtpCapabilities);

                // Set MID.
                rtpParameters.Mid = $"{_nextMidForConsumers++}";

                // We use up to 8 bytes for MID (string).
                if (_nextMidForConsumers == 100_000_000)
                {
                    _logger.LogDebug($"ConsumeAsync() | reaching max MID value {_nextMidForConsumers}");
                    _nextMidForConsumers = 0;
                }

                var @internal = new ConsumerInternalData
                (
                    Internal.RouterId,
                    Internal.TransportId,
                    consumerOptions.ProducerId,
                    Guid.NewGuid().ToString()
                );

                var reqData = new
                {
                    producer.Kind,
                    RtpParameters = rtpParameters,
                    producer.Type,
                    ConsumableRtpEncodings = producer.ConsumableRtpParameters.Encodings,
                    consumerOptions.Paused,
                    consumerOptions.PreferredLayers
                };

                var status = await Channel.RequestAsync(MethodId.TRANSPORT_CONSUME, @internal, reqData);
                var responseData = JsonConvert.DeserializeObject<TransportConsumeResponseData>(status!);

                var data = new
                {
                    producer.Kind,
                    RtpParameters = rtpParameters,
                    Type = (ConsumerType)producer.Type, // 注意：类型转换。ProducerType 的每一种值在 ConsumerType 都有对应且相同的值。
                };

                var consumer = new Consumer(_loggerFactory,
                    @internal,
                    data.Kind,
                    data.RtpParameters,
                    data.Type,
                    Channel,
                    PayloadChannel,
                    AppData,
                    responseData.Paused,
                    responseData.ProducerPaused,
                    responseData.Score,
                    responseData.PreferredLayers);

                Consumers[consumer.ConsumerId] = consumer;

                consumer.On("@close", async _ =>
                {
                    using (await Locker.WriteLockAsync())
                    {
                        Consumers.Remove(consumer.ConsumerId);
                    }
                });
                consumer.On("@producerclose", async _ =>
                {
                    using (await Locker.WriteLockAsync())
                    {
                        Consumers.Remove(consumer.ConsumerId);
                    }
                });

                // Emit observer event.
                Observer.Emit("newconsumer", consumer);

                return consumer;
            }
        }

        /// <summary>
        /// Create a DataProducer.
        /// </summary>
        /// <returns></returns>
        public async Task<DataProducer> ProduceDataAsync(DataProducerOptions dataProducerOptions)
        {
            _logger.LogDebug("ProduceDataAsync()");

            using (await Locker.WriteLockAsync())
            {

                if (!dataProducerOptions.Id.IsNullOrWhiteSpace() && DataProducers.ContainsKey(dataProducerOptions.Id!))
                {
                    throw new Exception($"a DataProducer with same id {dataProducerOptions.Id} already exists");
                }

                if (dataProducerOptions.Label.IsNullOrWhiteSpace())
                {
                    dataProducerOptions.Label = string.Empty;
                }
                if (dataProducerOptions.Protocol.IsNullOrWhiteSpace())
                {
                    dataProducerOptions.Protocol = string.Empty;
                }

                DataProducerType type;
                // If this is not a DirectTransport, sctpStreamParameters are required.
                // TODO: (alby)反模式
                if (GetType() != typeof(DirectTransport))
                {
                    type = DataProducerType.Sctp;

                    // This may throw.
                    ORTC.ValidateSctpStreamParameters(dataProducerOptions.SctpStreamParameters!);
                }
                // If this is a DirectTransport, sctpStreamParameters must not be given.
                else
                {
                    type = DataProducerType.Direct;

                    if (dataProducerOptions.SctpStreamParameters != null)
                    {
                        _logger.LogWarning("ProduceDataAsync() | sctpStreamParameters are ignored when producing data on a DirectTransport");
                    }
                }

                var @internal = new DataProducerInternalData
                (
                    Internal.RouterId,
                    Internal.TransportId,
                    dataProducerOptions.Id.NullOrWhiteSpaceReplace(Guid.NewGuid().ToString())
                );

                var reqData = new
                {
                    Type = type.GetEnumStringValue(),
                    dataProducerOptions.SctpStreamParameters,
                    Label = dataProducerOptions.Label!,
                    Protocol = dataProducerOptions.Protocol!
                };

                var status = await Channel.RequestAsync(MethodId.TRANSPORT_PRODUCE_DATA, @internal, reqData);
                var responseData = JsonConvert.DeserializeObject<TransportDataProduceResponseData>(status!);
                var dataProducer = new DataProducer(_loggerFactory,
                    @internal,
                    responseData.SctpStreamParameters,
                    responseData.Label!,
                    responseData.Protocol!,
                    Channel,
                    PayloadChannel,
                    AppData);

                DataProducers[dataProducer.DataProducerId] = dataProducer;

                dataProducer.On("@close", async _ =>
                {
                    using (await Locker.WriteLockAsync())
                    {
                        DataProducers.Remove(dataProducer.DataProducerId);
                    }
                    Emit("@dataproducerclose", dataProducer);
                });

                Emit("@newdataproducer", dataProducer);

                // Emit observer event.
                Observer.Emit("newdataproducer", dataProducer);

                return dataProducer;
            }
        }

        /// <summary>
        /// Create a DataConsumer.
        /// </summary>
        /// <param name="dataConsumerOptions"></param>
        /// <returns></returns>
        public async Task<DataConsumer> ConsumeDataAsync(DataConsumerOptions dataConsumerOptions)
        {
            _logger.LogDebug("ConsumeDataAsync()");

            using (await Locker.WriteLockAsync())
            {
                if (dataConsumerOptions.DataProducerId.IsNullOrWhiteSpace())
                {
                    throw new Exception("missing dataProducerId");
                }

                var dataProducer = GetDataProducerById(dataConsumerOptions.DataProducerId);
                if (dataProducer == null)
                {
                    throw new Exception($"DataProducer with id {dataConsumerOptions.DataProducerId} not found");
                }

                DataProducerType type;
                SctpStreamParameters? sctpStreamParameters = null;
                int sctpStreamId = -1;

                // If this is not a DirectTransport, use sctpStreamParameters from the
                // DataProducer (if type 'sctp') unless they are given in method parameters.
                // TODO: (alby)反模式
                if (GetType() != typeof(DirectTransport))
                {
                    type = DataProducerType.Sctp;

                    sctpStreamParameters = dataProducer.SctpStreamParameters.DeepClone<SctpStreamParameters>();
                    // This may throw.
                    sctpStreamId = GetNextSctpStreamId();

                    if (_sctpStreamIds == null || sctpStreamId > _sctpStreamIds.Length - 1)
                    {
                        throw new IndexOutOfRangeException(nameof(_sctpStreamIds));
                    }
                    _sctpStreamIds[sctpStreamId] = 1;
                    sctpStreamParameters.StreamId = sctpStreamId;
                }
                // If this is a DirectTransport, sctpStreamParameters must not be used.
                else
                {
                    type = DataProducerType.Direct;

                    if (dataConsumerOptions.Ordered.HasValue ||
                        dataConsumerOptions.MaxPacketLifeTime.HasValue ||
                        dataConsumerOptions.MaxRetransmits.HasValue
                    )
                    {
                        _logger.LogWarning("ConsumeDataAsync() | ordered, maxPacketLifeTime and maxRetransmits are ignored when consuming data on a DirectTransport");
                    }
                }

                var @internal = new DataConsumerInternalData
                (
                    Internal.RouterId,
                    Internal.TransportId,
                    dataConsumerOptions.DataProducerId,
                    Guid.NewGuid().ToString()
                );

                var reqData = new
                {
                    Type = type.GetEnumStringValue(),
                    SctpStreamParameters = sctpStreamParameters,
                    dataProducer.Label,
                    dataProducer.Protocol
                };

                var status = await Channel.RequestAsync(MethodId.TRANSPORT_CONSUME_DATA, @internal, reqData);
                var responseData = JsonConvert.DeserializeObject<TransportDataConsumeResponseData>(status!);

                var dataConsumer = new DataConsumer(_loggerFactory,
                    @internal,
                    responseData.SctpStreamParameters,
                    responseData.Label,
                    responseData.Protocol,
                    Channel,
                    PayloadChannel,
                    AppData);

                DataConsumers[dataConsumer.DataConsumerId] = dataConsumer;

                dataConsumer.On("@close", async _ =>
                {
                    using (await Locker.WriteLockAsync())
                    {
                        DataConsumers.Remove(dataConsumer.DataConsumerId);
                        if (_sctpStreamIds != null && sctpStreamId >= 0)
                        {
                            _sctpStreamIds[sctpStreamId] = 0;
                        }
                    }
                });

                dataConsumer.On("@dataproducerclose", async _ =>
                {
                    using (await Locker.WriteLockAsync())
                    {
                        DataConsumers.Remove(dataConsumer.DataConsumerId);
                        if (_sctpStreamIds != null && sctpStreamId >= 0)
                        {
                            _sctpStreamIds[sctpStreamId] = 0;
                        }
                    }
                });

                // Emit observer event.
                Observer.Emit("newdataconsumer", dataConsumer);

                return dataConsumer;
            }
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

            return Channel.RequestAsync(MethodId.TRANSPORT_ENABLE_TRACE_EVENT, Internal, reqData);
        }

        #region Private Methods

        private int GetNextSctpStreamId()
        {
            if (SctpParameters == null)
            {
                throw new Exception("missing data.sctpParameters.MIS");
            }
            if (_sctpStreamIds == null)
            {
                throw new IndexOutOfRangeException(nameof(_sctpStreamIds));
            }

            var numStreams = SctpParameters.MIS;

            if (_sctpStreamIds.IsNullOrEmpty())
            {
                _sctpStreamIds = new int[numStreams];
            }

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

        #endregion
    }
}
