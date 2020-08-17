using System;
using System.Collections.Generic;

namespace TubumuMeeting.Meeting.Server
{
    public class PeerWithRoomAppData : IEquatable<PeerWithRoomAppData>
    {
        public Peer Peer { get; }

        public string RoomId { get; set; }

        public string[] RoomSources { get; set; }

        public Dictionary<string, object> RoomAppData { get; set; }

        public PeerWithRoomAppData(Peer peer)
        {
            Peer = peer;
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
