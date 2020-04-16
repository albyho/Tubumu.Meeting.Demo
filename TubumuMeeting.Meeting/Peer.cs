using System;
using System.Collections.Generic;
using System.Text;
using Tubumu.Core.Extensions;
using TubumuMeeting.Mediasoup;

namespace TubumuMeeting.Meeting
{
    public class Peer : EventEmitter, IEquatable<Peer>
    {
        public int PeerId { get; }

        public string Name { get; }

        public bool Closed { get; private set; }

		public bool Joined { get; private set; }

        public Room? Room { get; private set; }

        public Peer(int peerId, string name)
        {
            PeerId = peerId;
            Name = name.IsNullOrWhiteSpace() ? "Guest" : name;
        }

        public bool JoinRoom(Room room)
        {
            if (Room != null)
            {
                throw new Exception($"Peer:{PeerId} 已经在房间");
            }

            Room = room;
            Emit("joinedroom", new RoomPeer(room, this));

            return true;
        }

        public bool LeaveRoom()
        {
            if(Room == null)
            {
                throw new Exception($"Peer:{PeerId} 没在任何房间");
            }

            var tempRoom = Room;
            Room = null;
            Emit("leftroom", new RoomPeer(tempRoom, this));

            return true;
        }

        public bool Close()
        {
            if (Room != null)
            {
                LeaveRoom();
            }

            Emit("closed", this);

            return true;
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
