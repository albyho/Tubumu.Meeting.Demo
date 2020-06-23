namespace TubumuMeeting.Mediasoup
{
    public class CreateWebRtcTransportRequest
    {
        public bool ForceTcp { get; set; }

        public bool Producing { get; set; }

        public bool Consuming { get; set; }

        public SctpCapabilities? SctpCapabilities { get; set; }
    }
}
