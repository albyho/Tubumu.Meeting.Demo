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
            if (other == null)
                return false;

            return Room.RoomId == other.Room.RoomId;
        }

        public override bool Equals(object obj)
        {
            if (obj == null)
                return false;

            var tObj = obj as RoomWithRoomAppData;
            if (tObj == null)
                return false;
            else
                return Room.RoomId == tObj.Room.RoomId;
        }

        public override int GetHashCode()
        {
            return Room.RoomId.GetHashCode();
        }
    }
}
