namespace TubumuMeeting.Meeting.Server
{
    public class JoinRoomResult
    {
        public Peer Peer { get; set; }

        public Peer[] PeersInRoom { get; set; }
    }
}
