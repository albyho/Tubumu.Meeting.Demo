using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.Threading;
using Tubumu.Core.Extensions;
using TubumuMeeting.Mediasoup;

namespace TubumuMeeting.Meeting.Server
{
    public partial class Room : IEquatable<Room>
    {
        public string RoomId { get; }

        public string Name { get; }

        public bool Equals(Room other)
        {
            return RoomId == other.RoomId;
        }

        public override int GetHashCode()
        {
            return RoomId.GetHashCode();
        }
    }

    public partial class Room
    {
        /// <summary>
        /// Logger factory for create logger.
        /// </summary>
        private readonly ILoggerFactory _loggerFactory;

        /// <summary>
        /// Logger.
        /// </summary>
        private readonly ILogger<Room> _logger;

        private readonly AsyncAutoResetEvent _closeLock = new AsyncAutoResetEvent();

        // TODO: (alby) Closed 的使用及线程安全。
        /// <summary>
        /// Whether the Room is closed.
        /// </summary>
        public bool Closed { get; private set; }

        public Router Router { get; private set; }

        public Room(ILoggerFactory loggerFactory, Router router, string roomId, string name)
        {
            _loggerFactory = loggerFactory;
            _logger = _loggerFactory.CreateLogger<Room>();
            Router = router;
            RoomId = roomId;
            Name = name.NullOrWhiteSpaceReplace("Default");
            _closeLock.Set();
            Closed = false;
        }

        public async Task CloseAsync()
        {
            if (Closed)
            {
                return;
            }

            await _closeLock.WaitAsync();
            try
            {
                if (Closed)
                {
                    return;
                }

                _logger.LogDebug($"Close() | Room:{RoomId}");

                Closed = true;

                await Router.CloseAsync();
            }
            finally
            {
                _closeLock.Set();
            }
        }
    }
}
