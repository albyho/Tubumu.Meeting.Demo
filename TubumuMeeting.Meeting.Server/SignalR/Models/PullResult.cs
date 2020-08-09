using TubumuMeeting.Mediasoup;

namespace TubumuMeeting.Meeting.Server
{
    public class PullResult
    {
        public Peer SelfPeer { get; set; }

        public Peer TargetPeer { get; set; }

        public Producer[] ExistsProducers { get; set; }

        public string RoomId { get; set; }

        public string TargetPeerId { get; set; }

        public string[] ProduceSources { get; set; }
    }
}
