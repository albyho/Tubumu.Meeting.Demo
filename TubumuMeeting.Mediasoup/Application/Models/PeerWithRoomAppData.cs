using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Tubumu.Core.Extensions;

namespace TubumuMeeting.Mediasoup
{
    public class PeerWithRoomAppData : IEquatable<PeerWithRoomAppData>
    {
        public Peer Peer { get; }

        public string[] RoomSources { get; set; }

        public Dictionary<string, object> RoomAppData { get; }

        public PeerWithRoomAppData(Peer peer, string[] roomSources, Dictionary<string, object> roomAppData)
        {
            Peer = peer;
            RoomSources = roomSources;
            RoomAppData = roomAppData;
        }

        public bool Equals(PeerWithRoomAppData other)
        {
            return Peer.PeerId == other.Peer.PeerId;
        }

        public override int GetHashCode()
        {
            return Peer.PeerId.GetHashCode();
        }
    }
}
