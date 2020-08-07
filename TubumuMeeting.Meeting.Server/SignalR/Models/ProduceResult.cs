using TubumuMeeting.Mediasoup;

namespace TubumuMeeting.Meeting.Server
{
    public class ProduceResult
    {
        public Peer SelfPeer { get; set; }

        public Producer Producer { get; set; }

        public PeerWithRoomId[] OtherPeerRoomIds { get; set; }
    }

    public class PeerWithRoomId
    {
        public Peer Peer { get; set; }

        public string RoomId { get; set; }
    }
}
