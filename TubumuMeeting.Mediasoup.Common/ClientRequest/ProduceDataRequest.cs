using System.Collections.Generic;

namespace TubumuMeeting.Mediasoup
{
    public class ProduceDataRequest
    {
        public string TransportId { get; set; }

        public SctpStreamParameters SctpStreamParameters { get; set; }

        public string Label { get; set; }

        public string Protocol { get; set; }

        public Dictionary<string, object> AppData { get; set; }
    }
}
