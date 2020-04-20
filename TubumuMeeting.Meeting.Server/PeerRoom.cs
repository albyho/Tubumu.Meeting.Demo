using System;

namespace TubumuMeeting.Meeting.Server
{
    public class PeerRoom : IEquatable<PeerRoom>
    {
        public Peer Peer { get; set; }

        public Room Room { get; set; }

        public PeerRoom(Peer peer, Room room)
        {
            Peer = peer;
            Room = room;
        }

        public bool Equals(PeerRoom other)
        {
            return Peer.PeerId == other.Peer.PeerId && Room.RoomId == other.Room.RoomId;
        }

        public override int GetHashCode()
        {
            return Peer.PeerId.GetHashCode() ^ Room.RoomId.GetHashCode();
        }
    }
}
