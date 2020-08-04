using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Tubumu.Core.Extensions;
using TubumuMeeting.Mediasoup;

namespace TubumuMeeting.Meeting.Server
{
    public partial class RoomWithInterestedSources : IEquatable<RoomWithInterestedSources>
    {
        public Room Room { get; }

        public string[] InterestedSources { get; }

        public bool Equals(RoomWithInterestedSources other)
        {
            return Room.RoomId == other.Room.RoomId;
        }

        public override int GetHashCode()
        {
            return Room.RoomId.GetHashCode();
        }

        public RoomWithInterestedSources(Room room, string[] interestedSources)
        {
            Room = room;
            InterestedSources = interestedSources;
        }
    }
}
