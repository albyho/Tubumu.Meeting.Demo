using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Tubumu.Core.Extensions;
using TubumuMeeting.Mediasoup;

namespace TubumuMeeting.Meeting.Server
{
    public partial class RoomInterestedSources : IEquatable<RoomInterestedSources>
    {
        public Room Room { get; }

        public string[] InterestedSources { get; }

        public bool Equals(RoomInterestedSources other)
        {
            return Room.RoomId == other.Room.RoomId;
        }

        public override int GetHashCode()
        {
            return Room.RoomId.GetHashCode();
        }

        public RoomInterestedSources(Room room, string[] interestedSources)
        {
            Room = room;
            InterestedSources = interestedSources;
        }
    }
}
