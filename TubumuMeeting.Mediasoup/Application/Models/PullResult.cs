namespace TubumuMeeting.Mediasoup
{
    public class PullResult
    {
        public string RoomId { get; set; }

        public Peer ConsumePeer { get; set; }

        public Peer ProducePeer { get; set; }

        public Producer[] ExistsProducers { get; set; }

        public string[] ProduceSources { get; set; }
    }
}
