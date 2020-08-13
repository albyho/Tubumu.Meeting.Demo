namespace TubumuMeeting.Mediasoup
{
    public class LeaveRoomResult
    {
        public Peer SelfPeer { get; set; }

        public Peer[] OtherPeers { get; set; }
    }
}
