using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Tubumu.Core.Extensions;
using TubumuMeeting.Mediasoup;

namespace TubumuMeeting.Meeting.Server
{
    public partial class Group : IEquatable<Group>
    {
        public Guid GroupId { get; }

        public string Name { get; }

        public bool Equals(Group other)
        {
            return GroupId == other.GroupId;
        }

        public override int GetHashCode()
        {
            return GroupId.GetHashCode();
        }
    }

    public partial class Group
    {
        /// <summary>
        /// Logger factory for create logger.
        /// </summary>
        private readonly ILoggerFactory _loggerFactory;

        /// <summary>
        /// Logger.
        /// </summary>
        private readonly ILogger<Group> _logger;

        public bool Closed { get; private set; }

        public Router Router { get; private set; }

        public Dictionary<string, Room> Rooms { get; } = new Dictionary<string, Room>();

        public Dictionary<string, Peer> Peers { get; } = new Dictionary<string, Peer>();

        public Group(ILoggerFactory loggerFactory, Router router, Guid groupId, string name)
        {
            _loggerFactory = loggerFactory;
            _logger = _loggerFactory.CreateLogger<Group>();

            GroupId = groupId;
            Name = name.IsNullOrWhiteSpace() ? "Default" : name;
            Closed = false;
            Router = router;
        }

        public void Close()
        {
            _logger.LogError($"Close() | Group: {GroupId}");

            if (Closed)
            {
                return;
            }

            Router.Close();

            Closed = true;
        }
    }
}
