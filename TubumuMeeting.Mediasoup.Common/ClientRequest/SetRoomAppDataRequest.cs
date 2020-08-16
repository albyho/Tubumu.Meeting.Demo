using System.Collections.Generic;

namespace TubumuMeeting.Mediasoup
{
    public class SetRoomAppDataRequest
    {
        public string RoomId { get; set; }

        public Dictionary<string, object> RoomAppData { get; set; }
    }

    public class UnsetRoomAppDataRequest
    {
        public string RoomId { get; set; }

        public string[] Keys { get; set; }
    }
}
