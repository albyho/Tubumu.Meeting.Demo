using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace TubumuMeeting.Mediasoup
{
    public class WebRtcTransport : Transport
    {
        public string IceRole { get; } = "controlled";

        public IceParameters IceParameters { get; }

        public IceCandidate[] IceCandidates { get; }

        public IceState IceState { get; }

        public TransportTuple IceSelectedTuple { get; }

        public DtlsParameters DtlsParameters { get; }

        public DtlsState DtlsState { get; }

        public string? DtlsRemoteCert { get; }

        public WebRtcTransport(ILoggerFactory loggerFactory,
            string routerId,
            string transportId,
            SctpParameters sctpParameters,
            SctpState sctpState,
            Channel channel,
            object? appData,
            Func<RtpCapabilities> getRouterRtpCapabilities,
            Func<string, Producer> getProducerById,
            Func<string, DataProducer> getDataProducerById,
            string iceRole,
            IceParameters iceParameters,
            IceState iceState,
            TransportTuple iceSelectedTuple,
            DtlsParameters dtlsParameters,
            DtlsState dtlsState,
            string? dtlsRemoteCert
            ) : base(loggerFactory, routerId, transportId, sctpParameters, sctpState, channel, appData, getRouterRtpCapabilities, getProducerById, getDataProducerById)
        {
            IceRole = iceRole;
            IceParameters = iceParameters;
            IceState = iceState;
            IceSelectedTuple = iceSelectedTuple;
            DtlsParameters = dtlsParameters;
            DtlsState = dtlsState;
            DtlsRemoteCert = dtlsRemoteCert;
        }

        public override Task ConnectAsync(object @params)
        {
            throw new NotImplementedException();
        }
    }
}
