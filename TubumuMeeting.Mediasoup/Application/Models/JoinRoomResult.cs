using System.Collections.Generic;

namespace TubumuMeeting.Mediasoup
{
    public class JoinRoomResult
    {
        public PeerInfo SelfPeer { get; set; }

        public string[] RoomSources { get; set; }

        public Dictionary<string, object> RoomAppData { get; set; }

        public PeerInfo[] PeersInRoom { get; set; }
    }
}
