namespace TubumuMeeting.Mediasoup
{
    public class PlainTransportConnectParameters
    {
        public string Ip { get; set; }

        public int Port { get; set; }

        public int? RtcpPort { get; set; }

        public SrtpParameters SrtpParameters { get; set; }
    }
}
