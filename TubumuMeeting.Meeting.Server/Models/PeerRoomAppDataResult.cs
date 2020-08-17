using System.Collections.Generic;

namespace TubumuMeeting.Meeting.Server
{
    public class PeerRoomAppDataResult
    {
        public Peer SelfPeer { get; set; }

        public Dictionary<string, object> RoomAppData { get; set; }

        public string[] PeerIds { get; set; }
    }
}
