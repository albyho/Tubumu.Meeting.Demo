using Newtonsoft.Json;

namespace TubumuMeeting.Mediasoup
{
    public class IceCandidate
    {
        public string Foundation { get; set; }

        public int Priority { get; set; }

        public string Ip { get; set; }

        public TransportProtocol Protocol { get; set; }

        public int Port { get; set; }

        public string Type { get; set; } = "host";

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string? TcpType { get; set; } // passive
    }
}
