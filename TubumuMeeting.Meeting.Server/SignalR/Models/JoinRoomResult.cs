namespace TubumuMeeting.Meeting.Server
{
    public class JoinRoomResult
    {
        public Peer SelfPeer { get; set; }

        public Peer[] PeersInRoom { get; set; }
    }
}
