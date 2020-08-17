using System;
using System.Collections.Generic;

namespace TubumuMeeting.Meeting.Server
{
    public class RoomWithRoomAppData : IEquatable<RoomWithRoomAppData>
    {
        public Room Room { get; }

        public string PeerId { get; set; }

        public string[] RoomSources { get; set; }

        public Dictionary<string, object> RoomAppData { get; set; }

        public RoomWithRoomAppData(Room room)
        {
            Room = room;
        }

        public bool Equals(RoomWithRoomAppData other)
        {
            return Room.RoomId == other.Room.RoomId;
        }

        public override int GetHashCode()
        {
            return Room.RoomId.GetHashCode();
        }
    }
}
