using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace TubumuMeeting.Mediasoup
{
    public class PlainTransport : Transport
    {
        // Logger
        private readonly ILogger<PlainTransport> _logger;

        public bool? RtcpMux { get; set; }

        public bool? Comedia { get; set; }

        public TransportTuple Tuple { get; private set; }

        public TransportTuple? RtcpTuple { get; private set; }

        public SrtpParameters? SrtpParameters { get; private set; }

        /// <summary>
        /// <para>@emits tuple - (tuple: TransportTuple)</para>
        /// <para>@emits rtcptuple - (rtcpTuple: TransportTuple)</para>
        /// <para>@emits sctpstatechange - (sctpState: SctpState)</para>
        /// <para>@emits trace - (trace: TransportTraceEventData)</para>
        /// <para>Observer:</para>
        /// <para>@emits close</para>
        /// <para>@emits newproducer - (producer: Producer)</para>
        /// <para>@emits newconsumer - (producer: Producer)</para>
        /// <para>@emits newdataproducer - (dataProducer: DataProducer)</para>
        /// <para>@emits newdataconsumer - (dataProducer: DataProducer)</para>
        /// <para>@emits tuple - (tuple: TransportTuple)</para>
        /// <para>@emits rtcptuple - (rtcpTuple: TransportTuple)</para>
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
        public PlainTransport(ILoggerFactory loggerFactory,
            string routerId,
            string transportId,
            SctpParameters? sctpParameters,
            SctpState? sctpState,
            Channel channel,
            object? appData,
            Func<RtpCapabilities> getRouterRtpCapabilities,
            Func<string, Producer> getProducerById,
            Func<string, DataProducer> getDataProducerById,
            bool? rtcpMux,
            bool? comedia,
            TransportTuple tuple,
            TransportTuple? rtcpTuple,
            SrtpParameters? srtpParameters
            ) : base(loggerFactory, routerId, transportId, sctpParameters, sctpState, channel, appData, getRouterRtpCapabilities, getProducerById, getDataProducerById)
        {
            _logger = loggerFactory.CreateLogger<PlainTransport>();
            RtcpMux = rtcpMux;
            Comedia = comedia;
            Tuple = tuple;
            RtcpTuple = rtcpTuple;
            SrtpParameters = srtpParameters;

            HandleWorkerNotifications();
        }

        /// <summary>
        /// Close the PlainTransport.
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

            if (!(parameters is PlainTransportConnectParameters connectParameters))
            {
                throw new Exception($"{nameof(parameters)} type is not PipTransportConnectParameters");
            }
            return ConnectAsync(connectParameters);
        }

        private async Task ConnectAsync(PlainTransportConnectParameters plainTransportConnectParameters)
        {
            var reqData = plainTransportConnectParameters;

            var status = await Channel.RequestAsync(MethodId.TRANSPORT_CONNECT, _internal, reqData);
            var responseData = JsonConvert.DeserializeObject<PlainTransportConnectResponseData>(status);

            // Update data.
            Tuple = responseData.Tuple;

            if (responseData.RtcpTuple != null)
                RtcpTuple = responseData.RtcpTuple;

            SrtpParameters = responseData.SrtpParameters;
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
                case "tuple":
                    {
                        var notification = JsonConvert.DeserializeObject<PlainTransportTupleNotificationData>(data);
                        Tuple = notification.Tuple;

                        Emit("tuple", Tuple);

                        // Emit observer event.
                        Observer.Emit("tuple", Tuple);

                        break;
                    }

                case "rtcptuple":
                    {
                        var notification = JsonConvert.DeserializeObject<PlainTransportRtcpTupleNotificationData>(data);
                        RtcpTuple = notification.RtcpTuple;

                        Emit("rtcptuple", Tuple);

                        // Emit observer event.
                        Observer.Emit("rtcptuple", Tuple);

                        break;
                    }

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
