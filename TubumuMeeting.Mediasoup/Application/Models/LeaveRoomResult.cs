namespace TubumuMeeting.Mediasoup
{
    public class LeaveRoomResult
    {
        public Peer SelfPeer { get; set; }

        public PeerInfo[] OtherPeers { get; set; }
    }
}
