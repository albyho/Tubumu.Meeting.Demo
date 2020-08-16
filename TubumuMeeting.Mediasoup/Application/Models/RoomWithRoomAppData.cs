using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace TubumuMeeting.Mediasoup
{
    public class RoomWithRoomAppData : IEquatable<RoomWithRoomAppData>
    {
        public Room Room { get; }

        public string[] RoomSources { get; set; }

        public ConcurrentDictionary<string, object> RoomAppData { get; }

        public RoomWithRoomAppData(Room room, string[] roomSources, Dictionary<string, object> roomAppData)
        {
            Room = room;
            RoomSources = roomSources;
            RoomAppData = new ConcurrentDictionary<string, object>();
            if (roomAppData != null)
            {
                foreach (var kv in roomAppData)
                {
                    RoomAppData[kv.Key] = kv.Value;
                }
            }
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
