using System;
using System.Collections.Generic;

namespace TubumuMeeting.Meeting.Server
{
    public class LeaveResult
    {
        public Peer Peer { get; set; }

        public PeerRoom[] OtherPeers { get; set; }
    }
}
