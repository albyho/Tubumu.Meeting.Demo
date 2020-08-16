namespace TubumuMeeting.Mediasoup
{
    public class LeaveRoomResult
    {
        public Peer SelfPeer { get; set; }

        public PeerWithRoomAppData[] OtherPeers { get; set; }
    }
}
