using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Tubumu.Core.Extensions;

namespace TubumuMeeting.Mediasoup
{
    public class WebRtcTransport : Transport
    {
        // Logger
        private readonly ILogger<WebRtcTransport> _logger;

        public string IceRole { get; private set; } = "controlled";

        public IceParameters IceParameters { get; private set; }

        public IceCandidate[] IceCandidates { get; private set; }

        public IceState IceState { get; private set; }

        public TransportTuple? IceSelectedTuple { get; private set; }

        public DtlsParameters DtlsParameters { get; private set; }

        public DtlsState DtlsState { get; private set; }

        public string? DtlsRemoteCert { get; private set; }

        /// <summary>
        /// <para>@emits icestatechange - (iceState: IceState)</para>
        /// <para>@emits iceselectedtuplechange - (iceSelectedTuple: TransportTuple)</para>
        /// <para>@emits dtlsstatechange - (dtlsState: DtlsState)</para>
        /// <para>@emits sctpstatechange - (sctpState: SctpState)</para>
        /// <para>@emits trace - (trace: TransportTraceEventData)</para>
        /// <para>Observer:</para>
        /// <para>@emits close</para>
        /// <para>@emits newproducer - (producer: Producer)</para>
        /// <para>@emits newconsumer - (producer: Producer)</para>
        /// <para>@emits newdataproducer - (dataProducer: DataProducer)</para>
        /// <para>@emits newdataconsumer - (dataProducer: DataProducer)</para>
        /// <para>@emits icestatechange - (iceState: IceState)</para>
        /// <para>@emits iceselectedtuplechange - (iceSelectedTuple: TransportTuple)</para>
        /// <para>@emits dtlsstatechange - (dtlsState: DtlsState)</para>
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
        /// <param name="iceRole"></param>
        /// <param name="iceParameters"></param>
        /// <param name="iceState"></param>
        /// <param name="iceSelectedTuple"></param>
        /// <param name="dtlsParameters"></param>
        /// <param name="dtlsState"></param>
        /// <param name="dtlsRemoteCert"></param>
        public WebRtcTransport(ILoggerFactory loggerFactory,
            string routerId,
            string transportId,
            SctpParameters? sctpParameters,
            SctpState? sctpState,
            Channel channel,
            object? appData,
            Func<RtpCapabilities> getRouterRtpCapabilities,
            Func<string, Producer> getProducerById,
            Func<string, DataProducer> getDataProducerById,
            string iceRole,
            IceParameters iceParameters,
            IceState iceState,
            TransportTuple? iceSelectedTuple,
            DtlsParameters dtlsParameters,
            DtlsState dtlsState,
            string? dtlsRemoteCert
            ) : base(loggerFactory, routerId, transportId, sctpParameters, sctpState, channel, appData, getRouterRtpCapabilities, getProducerById, getDataProducerById)
        {
            _logger = loggerFactory.CreateLogger<WebRtcTransport>();
            IceRole = iceRole;
            IceParameters = iceParameters;
            IceState = iceState;
            IceSelectedTuple = iceSelectedTuple;
            DtlsParameters = dtlsParameters;
            DtlsState = dtlsState;
            DtlsRemoteCert = dtlsRemoteCert;

            HandleWorkerNotifications();
        }

        /// <summary>
        /// Close the WebRtcTransport.
        /// </summary>
        public override void Close()
        {
            if (Closed)
                return;

            IceState = IceState.Closed;
            IceSelectedTuple = null;
            DtlsState = DtlsState.Closed;

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

            IceState = IceState.Closed;
            IceSelectedTuple = null;
            DtlsState = DtlsState.Closed;

            if (SctpState.HasValue)
                SctpState = TubumuMeeting.Mediasoup.SctpState.Closed;

            base.Close();
        }

        /// <summary>
        /// Provide the WebRtcTransport remote parameters.
        /// </summary>
        public override Task ConnectAsync(object parameters)
        {
            _logger.LogDebug("ConnectAsync()");

            if (!(parameters is DtlsParameters dtlsParameters))
            {
                throw new Exception($"{nameof(parameters)} type is not DtlsParameters");
            }
            return ConnectAsync(dtlsParameters);
        }

        private async Task ConnectAsync(DtlsParameters dtlsParameters)
        {
            var reqData = new { DtlsParameters = dtlsParameters };

            var status = await Channel.RequestAsync(MethodId.TRANSPORT_CONNECT, _internal, reqData);
            var responseData = JsonConvert.DeserializeObject<WebRtcTransportConnectResponseData>(status);

            // Update data.
            DtlsParameters.Role = responseData.DtlsLocalRole;
        }

        /// <summary>
        /// Restart ICE.
        /// </summary>
        public async Task<IceParameters> RestartIceAsync(IceParameters iceParameters)
        {
            _logger.LogDebug("RestartIceAsync()");

            var reqData = new { IceParameters = iceParameters };

            var status = await Channel.RequestAsync(MethodId.TRANSPORT_RESTART_ICE, _internal, reqData);
            var responseData = JsonConvert.DeserializeObject<WebRtcTransportRestartIceResponseData>(status);

            // Update data.
            IceParameters = responseData.IceParameters;

            return IceParameters;
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
                case "icestatechange":
                    {
                        var notification = JsonConvert.DeserializeObject<TransportIceStateChangeNotificationData>(data);
                        IceState = notification.IceState;

                        Emit("icestatechange", IceState);

                        // Emit observer event.
                        Observer.Emit("icestatechange", IceState);

                        break;
                    }

                case "iceselectedtuplechange":
                    {
                        var notification = JsonConvert.DeserializeObject<TransportIceSelectedTupleChangeNotificationData>(data);
                        IceSelectedTuple = notification.IceSelectedTuple;

                        Emit("iceselectedtuplechange", IceSelectedTuple);

                        // Emit observer event.
                        Observer.Emit("iceselectedtuplechange", IceSelectedTuple);

                        break;
                    }

                case "dtlsstatechange":
                    {
                        var notification = JsonConvert.DeserializeObject<TransportDtlsStateChangeNotificationData>(data);
                        DtlsState = notification.DtlsState;

                        if (DtlsState == DtlsState.Connecting)
                        {
                            DtlsRemoteCert = notification.DtlsRemoteCert;
                        }

                        Emit("dtlsstatechange", DtlsState);

                        // Emit observer event.
                        Observer.Emit("dtlsstatechange", DtlsState);

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
