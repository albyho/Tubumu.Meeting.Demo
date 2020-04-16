using System;
using System.Collections.Generic;
using System.Text;

namespace TubumuMeeting.Meeting
{
    public class RoomPeer
    {
        public Room Room { get; set; }

        public Peer Peer { get; set; }

        public RoomPeer(Room room, Peer peer)
        {
            Room = room;
            Peer = peer;
        }
    }
}
