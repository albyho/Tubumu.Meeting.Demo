using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Tubumu.Core.Extensions;

namespace TubumuMeeting.Mediasoup
{
    public class PipeTransport : Transport
    {
        // Logger
        private readonly ILoggerFactory _loggerFactory;
        private readonly ILogger<PipeTransport> _logger;

        public TransportTuple Tuple { get; private set; }

        public bool Rtx { get; private set; }

        public SrtpParameters? SrtpParameters { get; private set; }

        /// <summary>
        /// <para>@emits sctpstatechange - (sctpState: SctpState)</para>
        /// <para>@emits trace - (trace: TransportTraceEventData)</para>
        /// <para>Observer:</para>
        /// <para>@emits close</para>
        /// <para>@emits newproducer - (producer: Producer)</para>
        /// <para>@emits newconsumer - (producer: Producer)</para>
        /// <para>@emits newdataproducer - (dataProducer: DataProducer)</para>
        /// <para>@emits newdataconsumer - (dataProducer: DataProducer)</para>
        /// <para>@emits sctpstatechange - (sctpState: SctpState)</para>
        /// <para>@emits trace - (trace: TransportTraceEventData)</para>   
        /// </summary>
        /// <param name="loggerFactory"></param>
        /// <param name="routerId"></param>
        /// <param name="transportId"></param>
        /// <param name="sctpParameters"></param>
        /// <param name="sctpState"></param>
        /// <param name="channel"></param>
        /// <param name="appData"></param>
        /// <param name="getRouterRtpCapabilities"></param>
        /// <param name="getProducerById"></param>
        /// <param name="getDataProducerById"></param>
        public PipeTransport(ILoggerFactory loggerFactory,
            string routerId,
            string transportId,
            SctpParameters? sctpParameters,
            SctpState? sctpState,
            Channel channel,
            object? appData,
            Func<RtpCapabilities> getRouterRtpCapabilities,
            Func<string, Producer> getProducerById,
            Func<string, DataProducer> getDataProducerById,
            TransportTuple tuple,
            bool rtx,
            SrtpParameters? srtpParameters
            ) : base(loggerFactory, routerId, transportId, sctpParameters, sctpState, channel, appData, getRouterRtpCapabilities, getProducerById, getDataProducerById)
        {
            _loggerFactory = loggerFactory;
            _logger = loggerFactory.CreateLogger<PipeTransport>();
            Tuple = tuple;
            Rtx = rtx;
            SrtpParameters = srtpParameters;

            HandleWorkerNotifications();
        }

        /// <summary>
        /// Close the PipeTransport.
        /// </summary>
        public override void Close()
        {
            if (Closed)
                return;

            if (SctpState.HasValue)
                SctpState = TubumuMeeting.Mediasoup.SctpState.Closed;

            base.Close();
        }

        /// <summary>
        /// Router was closed.
        /// </summary>
        public override void RouterClosed()
        {
            if (Closed)
                return;

            if (SctpState.HasValue)
                SctpState = TubumuMeeting.Mediasoup.SctpState.Closed;

            base.Close();
        }

        /// <summary>
        /// Provide the PipeTransport remote parameters.
        /// </summary>
        public override Task ConnectAsync(object parameters)
        {
            _logger.LogDebug("ConnectAsync()");

            var connectParameters = parameters as PipeTransportConnectParameters;
            if (connectParameters == null)
            {
                throw new Exception($"{nameof(parameters)} type is not PipTransportConnectParameters");
            }
            return ConnectAsync(connectParameters);
        }

        private async Task ConnectAsync(PipeTransportConnectParameters pipeTransportConnectParameters)
        {
            var reqData = pipeTransportConnectParameters;

            var status = await Channel.RequestAsync(MethodId.TRANSPORT_CONNECT, _internal, reqData);
            var responseData = JsonConvert.DeserializeObject<PipeTransportConnectResponseData>(status);

            // Update data.
            Tuple = responseData.Tuple;
        }

        /// <summary>
        /// Create a Consumer.
        /// </summary>
        /// <param name="consumerOptions"></param>
        /// <returns></returns>
        public override async Task<Consumer> ConsumeAsync(ConsumerOptions consumerOptions)
        {
            _logger.LogDebug("ConsumeAsync()");

            if (consumerOptions.ProducerId.IsNullOrWhiteSpace())
            {
                throw new Exception("missing producerId");
            }

            var producer = GetProducerById(consumerOptions.ProducerId);

            if (producer == null)
                throw new Exception($"Producer with id {consumerOptions.ProducerId} not found");

            // This may throw.
            var rtpParameters = ORTC.GetPipeConsumerRtpParameters(producer.ConsumableRtpParameters, Rtx);

            var @internal = new
            {
                RouterId,
                TransportId = Id,
                ConsumerId = Guid.NewGuid().ToString(),
                consumerOptions.ProducerId,
            };

            var reqData = new
            {
                producer.Kind,
                RtpParameters = rtpParameters,
                Type = ConsumerType.Pipe,
                ConsumableRtpEncodings = producer.ConsumableRtpParameters.Encodings,
            };

            var status = await Channel.RequestAsync(MethodId.TRANSPORT_CONSUME, @internal, reqData);
            var responseData = JsonConvert.DeserializeObject<TransportConsumeResponseData>(status);

            var data = new
            {
                producer.Kind,
                RtpParameters = rtpParameters,
                Type = ConsumerType.Pipe,
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
                responseData.Paused,
                responseData.ProducerPaused,
                responseData.Score,
                responseData.PreferredLayers);

            Consumers[consumer.Id] = consumer;

            consumer.On("@close", _ => Consumers.Remove(consumer.Id));
            consumer.On("@producerclose", _ => Consumers.Remove(consumer.Id));

            // Emit observer event.
            Observer.Emit("newconsumer", consumer);

            return consumer;
        }

        private void HandleWorkerNotifications()
        {
            Channel.MessageEvent += OnChannelMessage;
        }

        private void OnChannelMessage(string targetId, string @event, string data)
        {
            if (targetId != Id) return;
            switch (@event)
            {
                case "sctpstatechange":
                    {
                        var notification = JsonConvert.DeserializeObject<TransportSctpStateChangeNotificationData>(data);
                        SctpState = notification.SctpState;

                        Emit("sctpstatechange", SctpState);

                        // Emit observer event.
                        Observer.Emit("sctpstatechange", SctpState);

                        break;
                    }

                case "trace":
                    {
                        var notification = JsonConvert.DeserializeObject<TraceEventData>(data);

                        Emit("trace", notification);

                        // Emit observer event.
                        Observer.Emit("trace", notification);

                        break;
                    }

                default:
                    {
                        _logger.LogError($"ignoring unknown event {@event}");
                        break;
                    }
            }
        }
    }
}
