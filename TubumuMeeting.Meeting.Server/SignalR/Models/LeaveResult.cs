using System;
using System.Collections.Generic;

namespace TubumuMeeting.Meeting.Server
{
    public class LeaveResult
    {
        public Peer Peer { get; set; }

        public PeerIdRoomId[] OtherPeers { get; set; }
    }
}
