namespace TubumuMeeting.Mediasoup
{
    public class JoinRoomResult
    {
        public Peer SelfPeer { get; set; }

        public Peer[] PeersInRoom { get; set; }
    }
}
