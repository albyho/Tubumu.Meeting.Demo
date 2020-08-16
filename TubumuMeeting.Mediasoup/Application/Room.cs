using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.Threading;
using Tubumu.Core.Extensions;

namespace TubumuMeeting.Mediasoup
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

        private readonly AsyncReaderWriterLock _locker = new AsyncReaderWriterLock();

        public bool Closed { get; private set; }

        public Router Router { get; private set; }

        /// <summary>
        /// Peers 只允许 Scheduler 访问，由后者的 _peerRoomLocker 保护。
        /// </summary>
        public Dictionary<string, PeerInfo> Peers { get; } = new Dictionary<string, PeerInfo>();

        public Room(ILoggerFactory loggerFactory, Router router, string roomId, string name)
        {
            _loggerFactory = loggerFactory;
            _logger = _loggerFactory.CreateLogger<Room>();
            Router = router;
            RoomId = roomId;
            Name = name.NullOrWhiteSpaceReplace("Default");
            Closed = false;
        }

        public async Task Close()
        {
            if (Closed)
            {
                return;
            }

            using (await _locker.WriteLockAsync())
            {
                if (Closed)
                {
                    return;
                }

                _logger.LogDebug($"Close() | Room:{RoomId}");

                await Router.Close();
                Closed = true;
            }
        }

        private void CheckClosed()
        {
            if (Closed)
            {
                throw new Exception($"CheckClosed() | Room:{RoomId} was closed");
            }
        }
    }
}
