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
            Closed = false;
        }

        public bool JoinRoom(Room room)
        {
            if (Joined)
            {
                return false;
            }
            if(room == null)
            {
                return false;
            }
            if (room.Closed)
            {
                return false;
            }
            Room = room;
            room.AddPeer(this);
            Emit("JoinedRoom", new RoomPeer(room, this));

            return true;
        }

        public bool LeaveRoom()
        {
            if(Room == null)
            {
                return false;
            }

            var tempRoom = Room;
            Room = null;
            tempRoom.RemovePeer(this.PeerId);
            Emit("LeftRoom", new RoomPeer(tempRoom, this));

            return true;
        }

        public void Close()
        {
            if (Room != null)
            {
                LeaveRoom();
            }

            if (Closed)
            {
                return;
            }

            Closed = true;
            Emit("Closed", this);
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
