using System;
using System.Collections.Generic;
using System.Text;
using TubumuMeeting.Mediasoup;

namespace TubumuMeeting.Meeting.Server
{
    public class PeerProducer
    {
        public Peer Peer { get; set; }

        public Producer Producer { get; set; }
    }
}
