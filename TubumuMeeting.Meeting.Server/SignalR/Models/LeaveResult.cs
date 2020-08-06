namespace TubumuMeeting.Meeting.Server
{
    public class LeaveResult
    {
        public Peer Peer { get; set; }

        public PeerRoom[] OtherPeerRooms { get; set; }
    }
}
