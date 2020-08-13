using System.Collections.Generic;

namespace TubumuMeeting.Mediasoup
{
    public class PullRequest
    {
        public string RoomId { get; set; }

        public string ProducerPeerId { get; set; }

        public HashSet<string> Sources { get; set; }
    }
}
