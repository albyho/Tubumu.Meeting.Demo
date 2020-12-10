using System.Collections.ObjectModel;
using Newtonsoft.Json;

namespace TubumuMeeting.Mediasoup
{
    public class CreatePlainTransportResult
    {
        public string TransportId { get; set; }

        public string Ip { get; set; }

        public int Port { get; set; }
    }
}
