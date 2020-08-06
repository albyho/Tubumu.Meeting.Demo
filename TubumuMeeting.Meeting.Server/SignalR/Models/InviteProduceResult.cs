using System;
using System.Collections.Generic;
using TubumuMeeting.Mediasoup;

namespace TubumuMeeting.Meeting.Server
{
    public class InviteProduceResult
    {
        public Peer Peer {get;set;}

        public PeerProducer[] ExistsProducers { get; set; }

        public string[] InviteProduceSources { get; set; }
    }
}
