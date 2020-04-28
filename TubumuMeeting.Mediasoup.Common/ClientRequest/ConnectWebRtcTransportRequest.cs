namespace TubumuMeeting.Mediasoup
{
    public class ConnectWebRtcTransportRequest
    {
        public string TransportId { get; set; }

        public DtlsParameters DtlsParameters { get; set; }
    }
}
