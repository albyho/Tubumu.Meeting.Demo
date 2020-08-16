namespace TubumuMeeting.Mediasoup
{
    public class LeaveResult
    {
        public Peer SelfPeer { get; set; }

        public PeerWithRoomAppData[] OtherPeers { get; set; }
    }
}
