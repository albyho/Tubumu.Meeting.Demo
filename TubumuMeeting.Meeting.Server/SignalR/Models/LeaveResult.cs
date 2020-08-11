namespace TubumuMeeting.Meeting.Server
{
    public class LeaveResult
    {
        public Peer SelfPeer { get; set; }

        public Peer[] OtherPeers { get; set; }
    }
}
