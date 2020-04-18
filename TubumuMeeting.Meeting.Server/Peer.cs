using System;
using System.Collections.Generic;
using Tubumu.Core.Extensions;
using TubumuMeeting.Mediasoup;

namespace TubumuMeeting.Meeting.Server
{
    public class Peer : EventEmitter, IEquatable<Peer>
    {
        public int PeerId { get; }

        public string Name { get; }

        public bool Closed { get; private set; }

        public bool Joined { get; set; }

        public Room? Room { get; private set; }

        public RtpCapabilities RtpCapabilities { get; set; }

        public Dictionary<string, Transport> Transports { get; } = new Dictionary<string, Transport>();

        public Dictionary<string, Producer> Producers { get; } = new Dictionary<string, Producer>();

        public Dictionary<string, Consumer> Consumers { get; } = new Dictionary<string, Consumer>();

        public Producer? Producer
        {
            get
            {

                return null;
            }
        }

        public Consumer? Consumer
        {
            get
            {
                return null;
            }
        }

        public Peer(int peerId, string name)
        {
            PeerId = peerId;
            Name = name.IsNullOrWhiteSpace() ? "Guest" : name;
            Closed = false;
        }

        public bool JoinRoom(Room room)
        {
            if (room == null)
            {
                return false;
            }
            if (!Joined)
            {
                return false;
            }
            if (room.Closed)
            {
                return false;
            }
            Room = room;
            var addResult = room.AddPeer(this);
            if(addResult)
            {
                Emit("JoinedRoom", new RoomPeer(room, this));
                return true;
            }

            return false;
        }

        public bool LeaveRoom()
        {
            if (Room == null)
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
            Joined = false;
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
