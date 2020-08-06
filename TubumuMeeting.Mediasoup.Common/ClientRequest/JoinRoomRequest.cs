namespace TubumuMeeting.Mediasoup
{
    public class JoinRoomRequest
    {
        public string RoomId { get; set; }

        public string[] InterestedSources { get; set; }
    }
}
