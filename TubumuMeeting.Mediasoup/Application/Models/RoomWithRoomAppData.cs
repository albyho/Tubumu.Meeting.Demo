using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Tubumu.Core.Extensions;

namespace TubumuMeeting.Mediasoup
{
    public class RoomWithRoomAppData : IEquatable<RoomWithRoomAppData>
    {
        public Room Room { get; }

        public string[] RoomSources { get; set; }

        public Dictionary<string, object> RoomAppData { get; }

        public RoomWithRoomAppData(Room room, string[] roomSources, Dictionary<string, object> roomAppData)
        {
            Room = room;
            RoomSources = roomSources;
            RoomAppData = roomAppData;
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
