using System;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Tubumu.Core.Extensions;
using TubumuMeeting.Mediasoup;

namespace TubumuMeeting.Meeting.Server
{
    public class Room : IEquatable<Room>
    {
        private readonly ILoggerFactory _loggerFactory;

        private readonly ILogger<Room> _logger;

        public Guid RoomId { get; }

        public string Name { get; }

        public bool Closed { get; private set; }

        [JsonIgnore]
        public Router Router { get; set; }

        public Room(ILoggerFactory loggerFactory, Guid roomId, string name)
        {
            _loggerFactory = loggerFactory;
            _logger = _loggerFactory.CreateLogger<Room>();

            RoomId = roomId;
            Name = name.IsNullOrWhiteSpace() ? "Meeting" : name;
            Closed = false;
        }

        public void Close()
        {
            if (Closed)
            {
                return;
            }

            Closed = true;
        }

        public bool Equals(Room other)
        {
            return RoomId == other.RoomId;
        }

        public override int GetHashCode()
        {
            return RoomId.GetHashCode();
        }
    }
}
