using TubumuMeeting.Mediasoup;

namespace TubumuMeeting.Mediasoup
{
    public class PeerPullResult
    {
        public Producer[] ExistsProducers { get; set; }

        public string[] ProduceSources { get; set; }
    }
}
