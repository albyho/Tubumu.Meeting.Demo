using System.Collections.Generic;

namespace TubumuMeeting.Mediasoup
{
    public class PeerInfo
    {
        public string RoomId { get; set; }

        public string PeerId { get; set; }

        public string DisplayName { get; set; }

        public string[] Sources { get; set; }

        public Dictionary<string, object> AppData { get; set; }

        public string[] RoomSources { get; set; }

        public Dictionary<string, object> RoomAppData { get; set; }
    }
}
