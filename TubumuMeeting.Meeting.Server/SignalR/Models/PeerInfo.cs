using System.Collections.Generic;
using TubumuMeeting.Mediasoup;

namespace TubumuMeeting.Meeting.Server
{
    public class PeerInfo
    {
        public string RoomId { get; set; }

        public string PeerId { get; set; }

        public string DisplayName { get; set; }

        public string[] Sources { get; set; }
    }
}
