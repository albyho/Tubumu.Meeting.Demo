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
            if (other == null)
                return false;

            return Peer.PeerId == other.Peer.PeerId;
        }

        public override bool Equals(object obj)
        {
            if (obj == null)
                return false;

            var tObj = obj as PeerWithRoomAppData;
            if (tObj == null)
                return false;
            else
                return Peer.PeerId == tObj.Peer.PeerId;
        }

        public override int GetHashCode()
        {
            return Peer.PeerId.GetHashCode();
        }
    }
}
