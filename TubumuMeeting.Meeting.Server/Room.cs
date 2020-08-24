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
            if (other == null)
                return false;

            return RoomId == other.RoomId;
        }

        public override bool Equals(object obj)
        {
            if (obj == null)
                return false;

            if (!(obj is Room tObj))
                return false;
            else
                return RoomId == tObj.RoomId;
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

        /// <summary>
        /// Whether the Room is closed.
        /// </summary>
        private bool _closed;

        private readonly AsyncAutoResetEvent _closeLock = new AsyncAutoResetEvent();

        public Router Router { get; private set; }

        public Room(ILoggerFactory loggerFactory, Router router, string roomId, string name)
        {
            _loggerFactory = loggerFactory;
            _logger = _loggerFactory.CreateLogger<Room>();
            Router = router;
            RoomId = roomId;
            Name = name.NullOrWhiteSpaceReplace("Default");
            _closeLock.Set();
            _closed = false;
        }

        public async Task CloseAsync()
        {
            if (_closed)
            {
                return;
            }

            await _closeLock.WaitAsync();
            try
            {
                if (_closed)
                {
                    return;
                }

                _logger.LogDebug($"Close() | Room:{RoomId}");

                _closed = true;

                await Router.CloseAsync();
            }
            finally
            {
                _closeLock.Set();
            }
        }
    }
}
