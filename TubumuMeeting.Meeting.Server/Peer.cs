using System;
using System.Collections.Generic;
using System.Linq;
using Tubumu.Core.Extensions;
using TubumuMeeting.Mediasoup;

namespace TubumuMeeting.Meeting.Server
{
    public class Peer : IEquatable<Peer>
    {
        public int PeerId { get; }

        public string DisplayName { get; }

        public bool Closed { get; private set; }

        public bool Joined { get; set; }

        public RtpCapabilities? RtpCapabilities { get; set; }

        public Dictionary<string, Transport> Transports { get; } = new Dictionary<string, Transport>();

        public Dictionary<string, Producer> Producers { get; } = new Dictionary<string, Producer>();

        public Dictionary<string, Consumer> Consumers { get; } = new Dictionary<string, Consumer>();

        public Peer(int peerId, string displayName)
        {
            PeerId = peerId;
            DisplayName = displayName.IsNullOrWhiteSpace() ? "Guest" : displayName;
            Closed = false;
        }

        public void Close()
        {
            if (Closed)
            {
                return;
            }

            Closed = true;
            Joined = false;
        }

        public Transport GetConsumerTransport()
        {
            return Transports.Values.Where(m => m.AppData != null && m.AppData.TryGetValue("Consuming", out var value) && (bool)value).FirstOrDefault();
        }

        public bool Equals(Peer other)
        {
            return PeerId == other.PeerId;
        }

        public override int GetHashCode()
        {
            return PeerId.GetHashCode();
        }
    }
}
