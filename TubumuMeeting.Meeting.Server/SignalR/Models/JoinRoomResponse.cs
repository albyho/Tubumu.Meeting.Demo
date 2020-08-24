using System;
using System.Collections.Generic;
using System.Text;

namespace TubumuMeeting.Meeting.Server
{
    public class JoinRoomResponse
    {
        public string RoomId { get; set; }

        public PeerWithRoomAppData[] Peers { get; set; }
    }
}
