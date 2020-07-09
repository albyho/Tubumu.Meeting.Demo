using System.Collections.Generic;

namespace TubumuMeeting.Mediasoup
{
    public class ConsumeRequest
    {
        public string PeerId { get; set; }

        public string[]? ProducerIds { get; set; }
    }
}
