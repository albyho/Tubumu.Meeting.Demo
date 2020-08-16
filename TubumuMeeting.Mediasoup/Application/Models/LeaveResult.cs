namespace TubumuMeeting.Mediasoup
{
    public class LeaveResult
    {
        public Peer SelfPeer { get; set; }

        public PeerInfo[] OtherPeers { get; set; }
    }
}
