using System.Collections.Generic;

namespace TubumuMeeting.Mediasoup
{
    public class PeerRoomAppDataResult
    {
        public Peer SelfPeer { get; set; }

        public Dictionary<string, object> RoomAppData { get; set; }

        public string[] OtherPeerIds { get; set; }
    }
}
