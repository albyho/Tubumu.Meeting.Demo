using System;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Tubumu.Core.Extensions;
using TubumuMeeting.Mediasoup;

namespace TubumuMeeting.Meeting.Server
{
    public partial class Room : IEquatable<Room>
    {
        public Guid RoomId { get; }

        public string Name { get; }
    }

    public partial class Room
    {
        private readonly ILoggerFactory _loggerFactory;
        private readonly ILogger<Room> _logger;

        [JsonIgnore]
        public bool Closed { get; private set; }

        [JsonIgnore]
        public Router Router { get; private set; }

        [JsonIgnore]
        public AudioLevelObserver AudioLevelObserver { get; private set; }

        public Room(ILoggerFactory loggerFactory, Guid roomId, string name)
        {
            _loggerFactory = loggerFactory;
            _logger = _loggerFactory.CreateLogger<Room>();

            RoomId = roomId;
            Name = name.IsNullOrWhiteSpace() ? "Meeting" : name;
            Closed = false;
        }

        public void Active(Router router, AudioLevelObserver audioLevelObserver)
        {
            Router = router;
            AudioLevelObserver = audioLevelObserver;
        }

        public void Close()
        {
            _logger.LogError($"Close() | Room: {RoomId}");

            if (Closed)
            {
                return;
            }

            Router.Close();

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
