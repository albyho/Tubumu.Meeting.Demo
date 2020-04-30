namespace TubumuMeeting.Mediasoup
{
    public class JoinRequest
    {
        public RtpCapabilities RtpCapabilities { get; set; }

        public SctpCapabilities? SctpCapabilities { get; set; }
    }
}
