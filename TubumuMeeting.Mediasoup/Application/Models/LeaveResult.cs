namespace TubumuMeeting.Mediasoup
{
    public class LeaveResult
    {
        public Peer SelfPeer { get; set; }

        public string[] OtherPeerIds { get; set; }
    }
}
