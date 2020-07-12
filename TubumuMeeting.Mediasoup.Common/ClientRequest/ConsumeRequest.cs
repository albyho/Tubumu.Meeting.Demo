using System.Collections.Generic;

namespace TubumuMeeting.Mediasoup
{
    public class ConsumeRequest
    {
        public string PeerId { get; set; }

        public string[] Sources { get; set; }
    }
}
