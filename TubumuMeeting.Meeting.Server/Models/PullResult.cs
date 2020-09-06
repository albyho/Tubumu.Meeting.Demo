using TubumuMeeting.Mediasoup;

namespace TubumuMeeting.Meeting.Server
{
    public class PullResult
    {
        public Peer ConsumePeer { get; set; }

        public Peer ProducePeer { get; set; }

        public Producer[] ExistsProducers { get; set; }

        public string[] ProduceSources { get; set; }
    }
}
