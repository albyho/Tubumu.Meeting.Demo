using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Nito.AsyncEx;
using Tubumu.Core.Extensions;
using TubumuMeeting.Mediasoup;

namespace TubumuMeeting.Meeting.Server
{
    public partial class Peer : IEquatable<Peer>
    {
        public string PeerId { get; }

        public string DisplayName { get; }

        public bool Equals(Peer other)
        {
            return PeerId == other.PeerId;
        }

        public override int GetHashCode()
        {
            return PeerId.GetHashCode();
        }
    }

    public partial class Peer
    {
        private readonly object _groupLocker = new object();

        private readonly object _roomLocker = new object();

        private readonly AsyncLock _transportLocker = new AsyncLock();

        public bool Closed { get; private set; }

        public RtpCapabilities? RtpCapabilities { get; set; }

        public SctpCapabilities? SctpCapabilities { get; set; }

        public Group? Group { get; set; }

        public Dictionary<string, RoomInterestedSources> Rooms { get; } = new Dictionary<string, RoomInterestedSources>();

        public Dictionary<string, Transport> Transports { get; } = new Dictionary<string, Transport>();

        public Dictionary<string, Producer> Producers { get; } = new Dictionary<string, Producer>();

        public Dictionary<string, Consumer> Consumers { get; } = new Dictionary<string, Consumer>();

        public Dictionary<string, DataProducer> DataProducers { get; } = new Dictionary<string, DataProducer>();

        public Dictionary<string, DataConsumer> DataConsumers { get; } = new Dictionary<string, DataConsumer>();

        public string[]? Sources { get; set; }

        public Dictionary<string, object>? AppData { get; set; }

        public Peer(string peerId, string displayName)
        {
            PeerId = peerId;
            DisplayName = displayName.IsNullOrWhiteSpace() ? "Guest" : displayName;
            Closed = false;
        }

        public bool JoinGroup(Group group)
        {
            lock (_groupLocker)
            {
                if (Group != null)
                {
                    throw new Exception($"Peer:{PeerId} was already in Group:{group.GroupId}");
                }

                if(!group.Peers.TryGetValue(PeerId, out var _))
                {
                    throw new Exception($"group.Peers[{PeerId}] is not exists");
                }

                Group = group;
                group.PeerJoinGroup(this);

                return true;
            }
        }

        public bool LeaveGroup()
        {
            lock (_groupLocker)
            {
                if (Group == null)
                {
                    return true;
                }

                var localGroup = Group;
                Group = null;
                Close();
                localGroup.PeerLeaveGroup(this);

                return true;
            }
        }

        public RoomInterestedSources JoinRoom(Room room, string[] interestedSources)
        {
            lock (_roomLocker)
            {
                if (Rooms.TryGetValue(room.RoomId, out var _))
                {
                    throw new Exception($"Peer:{PeerId} was already in Room:{room.RoomId}");
                }

                if (!room.Peers.TryGetValue(PeerId, out var _))
                {
                    throw new Exception($"room.Peers[{PeerId}] is not exists");
                }

                var roomInterestedSources = new RoomInterestedSources(room, interestedSources);
                Rooms[room.RoomId] = roomInterestedSources;

                return roomInterestedSources;
            }
        }

        public bool LeaveRoom(string roomId)
        {
            lock (_roomLocker)
            {
                if (!Rooms.TryGetValue(roomId, out var room))
                {
                    return true;
                }

                Rooms.Remove(roomId);
                room.Room.PeerLeaveRoom(this);

                return true;
            }
        }

        public bool LeaveRooms()
        {
            lock (_roomLocker)
            {
                Rooms.Values.ForEach(m => m.Room.PeerLeaveRoom(this));
                Rooms.Clear();

                return true;
            }
        }

        public async Task<WebRtcTransport> CreateWebRtcTransportAsync(Peer peer, CreateWebRtcTransportRequest createWebRtcTransportRequest)
        {
            using (await _transportLocker.LockAsync())
            {
                if (Group == null)
                {
                    throw new Exception($"CreateWebRtcTransportAsync() | Peer:{peer.PeerId} is not in Group");
                }

                var webRtcTransportOptions = new WebRtcTransportOptions
                {
                    ListenIps = WebRtcTransportSettings.ListenIps,
                    InitialAvailableOutgoingBitrate = WebRtcTransportSettings.InitialAvailableOutgoingBitrate,
                    MaxSctpMessageSize = WebRtcTransportSettings.MaxSctpMessageSize,
                    EnableSctp = createWebRtcTransportRequest.SctpCapabilities != null,
                    NumSctpStreams = createWebRtcTransportRequest.SctpCapabilities?.NumStreams,
                    AppData = new Dictionary<string, object>
                {
                    { "Consuming", createWebRtcTransportRequest.Consuming },
                    { "Producing", createWebRtcTransportRequest.Producing },
                },
                };

                if (createWebRtcTransportRequest.ForceTcp)
                {
                    webRtcTransportOptions.EnableUdp = false;
                    webRtcTransportOptions.EnableTcp = true;
                }

                var transport = await Router.CreateWebRtcTransportAsync(webRtcTransportOptions);
                if (transport == null)
                {
                    throw new Exception($"CreateWebRtcTransportAsync() | Peer:{peer.PeerId} was already in Group:{GroupId}");
                }

                // Store the WebRtcTransport into the Peer data Object.
                peer.AddTransport(transport);

                return transport;
            }
        }


        public bool AddTransport(Transport transport)
        {
            lock(_transportLocker)
            {
                if (Transports.TryGetValue(transport.TransportId, out var _))
                {
                    throw new Exception($"AddTransport() | Transport:{transport.TransportId} is exists");
                }

                Transports[transport.TransportId] = transport;
                return true;
            }
        }

        public Transport GetConsumerTransport()
        {
            return Transports.Values.Where(m => m.AppData != null && m.AppData.TryGetValue("Consuming", out var value) && (bool)value).FirstOrDefault();
        }

        private void Close()
        {
            using (_transportLocker.Lock())
            {
                if (Closed)
                {
                    return;
                }

                Closed = true;
                RtpCapabilities = null;
                SctpCapabilities = null;
                Group = null;

                // Iterate and close all mediasoup Transport associated to this Peer, so all
                // its Producers and Consumers will also be closed.
                Transports.Values.ForEach(m => m.Close());
                Transports.Clear();
                Producers.Clear();
                Consumers.Clear();
                DataProducers.Clear();
                DataConsumers.Clear();
            }
        }
    }
}
