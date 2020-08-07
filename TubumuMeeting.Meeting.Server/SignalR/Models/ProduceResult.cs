using TubumuMeeting.Mediasoup;

namespace TubumuMeeting.Meeting.Server
{
    public class ProduceResult
    {
        public Peer SelfPeer { get; set; }

        public Producer Producer { get; set; }

        public PeerRoomId[] PeerRoomIds { get; set; }
    }

    public class PeerRoomId
    {
        public Peer Peer { get; set; }

        public string RoomId { get; set; }
    }
}
