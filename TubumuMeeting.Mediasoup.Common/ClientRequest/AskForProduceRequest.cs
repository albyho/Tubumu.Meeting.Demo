using System.Collections.Generic;

namespace TubumuMeeting.Mediasoup
{
    public class AskForProduceRequest
    {
        public string PeerId { get; set; }

        public string[] Sources { get; set; }
    }
}
