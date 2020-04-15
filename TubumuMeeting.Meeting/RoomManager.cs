using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using TubumuMeeting.Mediasoup;

namespace TubumuMeeting.Meeting
{
    public class RoomManager
    {
        private readonly MediasoupWorkerManager _mediasoupWorkerManager;

        private readonly object _locker = new object();

        public Dictionary<Guid, Room> Rooms = new Dictionary<Guid, Room>();

        public RoomManager(MediasoupWorkerManager mediasoupWorkerManager)
        {
            _mediasoupWorkerManager = mediasoupWorkerManager;
        }

        public Task<Room> GetOrCreateRoom(Guid roomId, string name)
        {
            lock (_locker)
            {
                if (Rooms.TryGetValue(roomId, out var room))
                {
                    return Task.FromResult(room);
                }
                var worker = _mediasoupWorkerManager.GetWorker();
                room = new Room(worker, roomId, name);
                Rooms[roomId] = room;

                return null;
            }
        }
    }
}
