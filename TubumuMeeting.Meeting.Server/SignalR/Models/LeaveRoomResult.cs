namespace TubumuMeeting.Meeting.Server
{
    public class LeaveRoomResult
    {
        public Peer SelfPeer { get; set; }

        public Peer[] OtherPeers { get; set; }
    }
}
