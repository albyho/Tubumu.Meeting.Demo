using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Nito.AsyncEx;
using Tubumu.Core.Extensions;
using TubumuMeeting.Mediasoup;

namespace TubumuMeeting.Meeting.Server
{
    public class Scheduler
    {
        #region Private Fields

        /// <summary>
        /// Logger factory for create logger.
        /// </summary>
        private readonly ILoggerFactory _loggerFactory;

        /// <summary>
        /// Logger
        /// </summary>
        private readonly ILogger<Scheduler> _logger;

        private readonly MediasoupOptions _mediasoupOptions;

        private readonly MediasoupServer _mediasoupServer;

        private readonly AsyncLock _groupLocker = new AsyncLock();

        private readonly object _peerLocker = new object();

        private readonly object _roomLocker = new object();

        private readonly object _peerRoomLocker = new object();

        #endregion

        public RtpCapabilities DefaultRtpCapabilities { get; private set; }

        public Dictionary<Guid, Group> Groups { get; } = new Dictionary<Guid, Group>();

        public Dictionary<string, Room> Rooms { get; } = new Dictionary<string, Room>();

        public Dictionary<string, Peer> Peers { get; } = new Dictionary<string, Peer>();

        public Scheduler(ILoggerFactory loggerFactory, MediasoupOptions mediasoupOptions, MediasoupServer mediasoupServer)
        {
            _loggerFactory = loggerFactory;
            _logger = _loggerFactory.CreateLogger<Scheduler>();
            _mediasoupOptions = mediasoupOptions;
            _mediasoupServer = mediasoupServer;

            var rtpCodecCapabilities = mediasoupOptions.MediasoupSettings.RouterSettings.RtpCodecCapabilities;
            // This may throw.
            DefaultRtpCapabilities = ORTC.GenerateRouterRtpCapabilities(rtpCodecCapabilities);
        }

        public async Task<bool> PeerJoinGroupAsync(string peerId, JoinRequest joinRequest)
        {
            using (await _groupLocker.LockAsync())
            {
                if (!Groups.TryGetValue(joinRequest.GroupId, out var group))
                {
                    group = await CreateGroupAsync(joinRequest.GroupId, "Default");
                }

                lock (_peerLocker)
                {
                    if (Peers.TryGetValue(peerId, out var peer))
                    {
                        _logger.LogError($"PeerJoinAsync() | Peer:{peerId} has already in Group:{joinRequest.GroupId}.");
                        return false;
                    }

                    peer = new Peer(_loggerFactory,
                        _mediasoupOptions.MediasoupSettings.WebRtcTransportSettings,
                        group.GroupId,
                        group.Router,
                        peerId,
                        joinRequest.DisplayName,
                        joinRequest.Sources,
                        joinRequest.AppData
                        )
                    {
                        RtpCapabilities = joinRequest.RtpCapabilities,
                        SctpCapabilities = joinRequest.SctpCapabilities,
                    };

                    Peers[peerId] = peer;
                    group.PeerJoinGroup(peer);

                    return true;
                }
            }
        }

        public void PeerLeaveGroup(string peerId)
        {
            lock (_peerLocker)
            {
                if (!Peers.TryGetValue(peerId, out var peer))
                {
                    return;
                }

                lock (_peerRoomLocker)
                {
                    foreach (var room in peer.Rooms.Values)
                    {
                        room.Peers.Remove(peerId);
                    }

                    peer.Rooms.Clear();
                }

                peer.Close();

                Peers.Remove(peerId);
            }
        }

        public Task<WebRtcTransport> PeerCreateWebRtcTransportAsync(string peerId, CreateWebRtcTransportRequest createWebRtcTransportRequest)
        {
            lock (_peerLocker)
            {
                if (!Peers.TryGetValue(peerId, out var peer))
                {
                    _logger.LogError($"CreateWebRtcTransport() | Peer:{peerId} is not exists.");
                    throw new Exception($"Peer:{peerId} is not exists");
                }

                return peer.CreateWebRtcTransportAsync(createWebRtcTransportRequest);
            }
        }

        public Task<bool> PeerConnectWebRtcTransportAsync(string peerId, ConnectWebRtcTransportRequest connectWebRtcTransportRequest)
        {
            lock (_peerLocker)
            {
                if (!Peers.TryGetValue(peerId, out var peer))
                {
                    _logger.LogError($"CreateWebRtcTransport() | Peer:{peerId} is not exists.");
                    throw new Exception($"Peer:{peerId} is not exists");
                }

                return peer.ConnectWebRtcTransportAsync(connectWebRtcTransportRequest);
            }
        }

        public async Task<JoinRoomResult> PeerJoinRoomAsync(string peerId, JoinRoomRequest joinRoomRequest)
        {
            lock (_peerLocker)
            {
                if (!Peers.TryGetValue(peerId, out var peer))
                {
                    _logger.LogError($"PeerJoinRoomAsync() | Peer:{peerId} is not exists.");
                    throw new Exception($"Peer:{peerId} is not exists.");
                }
                using(_groupLocker.Lock())
                {
                    if (!Groups.TryGetValue(peer.GroupId, out var group))
                    {
                        _logger.LogError($"PeerJoinRoomAsync() | Group:{peer.GroupId} is not exists.");
                        throw new Exception($"Group:{peer.GroupId} is not exists.");
                    }

                    lock (_roomLocker)
                    {
                        if (!group.Rooms.TryGetValue(joinRoomRequest.RoomId, out var room))
                        {
                            room = group.CreateRoom(joinRoomRequest.RoomId, "Default");
                            Rooms[room.RoomId] = room;
                        }

                        lock (_peerRoomLocker)
                        {
                            room.Peers[peerId] = peer;
                            peer.Rooms[joinRoomRequest.RoomId] = room;

                            var otherPeers = room.Peers.Values.Where(m => m.PeerId != peerId).ToArray();
                            return new JoinRoomResult
                            {
                                Peer = peer,
                                OtherPeers = otherPeers,
                            };
                        }
                    }
                }
            }
        }

        public LeaveRoomResult PeerLeaveRoom(string peerId, string roomId)
        {
            lock (_peerLocker)
            {
                if (!Peers.TryGetValue(peerId, out var peer))
                {
                    _logger.LogError($"PeerLeaveRoom() | Peer:{peerId} is not exists.");
                    throw new Exception($"Peer:{peerId} is not exists.");
                }

                lock (_peerRoomLocker)
                {
                    if (!peer.Rooms.TryGetValue(roomId, out var room))
                    {
                        _logger.LogError($"PeerLeaveRoom() | Peer:{peerId} is not exists in Room:{roomId}.");
                        throw new Exception($"Peer:{peerId} is not exists in Room:{roomId}.");
                    }

                    // 离开 Room 之前，先关闭将无人消费的 Producer
                    peer.CloseProducersNoConsumers(roomId);

                    var otherPeers = room.Peers.Values.Where(m => m.PeerId != peerId).ToArray();
                    foreach (var otherPeer in otherPeers)
                    {
                        otherPeer.CloseProducersNoConsumers(roomId, peer.PeerId);
                    }

                    room.Peers.Remove(peerId);
                    peer.Rooms.Remove(roomId);

                    return new LeaveRoomResult
                    {
                        Peer = peer,
                        OtherPeers = otherPeers,
                    };
                }
            }
        }

        public InviteProduceResult PeerInviteProduce(string peerId, InviteProduceRequest inviteProduceRequest)
        {
            lock (_peerLocker)
            {
                if (!Peers.TryGetValue(peerId, out var peer))
                {
                    _logger.LogError($"PeerInviteProduce() | Peer:{peerId} is not exists.");
                    throw new Exception($"Peer:{peerId} is not exists.");
                }

                if (!Peers.TryGetValue(inviteProduceRequest.PeerId, out var otherPeer))
                {
                    _logger.LogError($"PeerInviteProduce() | Peer:{inviteProduceRequest.PeerId} is not exists.");
                    throw new Exception($"Peer:{inviteProduceRequest.PeerId} is not exists.");
                }

                lock (_peerRoomLocker)
                {
                    if (!peer.Rooms.TryGetValue(inviteProduceRequest.RoomId, out var room))
                    {
                        _logger.LogError($"PeerInviteProduce() | Peer:{peerId} is not exists in Room:{inviteProduceRequest.RoomId}.");
                        throw new Exception($"Peer:{peerId} is not exists in Room:{inviteProduceRequest.RoomId}.");
                    }

                    if (!room.Peers.ContainsKey(inviteProduceRequest.PeerId))
                    {
                        _logger.LogError($"PeerInviteProduce() | Peer:{inviteProduceRequest.PeerId} is not exists in Room:{inviteProduceRequest.RoomId}.");
                        throw new Exception($"Peer:{inviteProduceRequest.PeerId} is not exists in Room:{inviteProduceRequest.RoomId}.");
                    }

                    var existsProducers = new List<PeerProducer>();
                    var inviteProduceSources = new List<string>();
                    foreach (var source in inviteProduceRequest.Sources)
                    {
                        var producer = otherPeer.Producers.Values.Where(m => m.Source == source).FirstOrDefault();
                        // 如果 Source 有对应的 Producer，直接消费。
                        if (source != null)
                        {
                            var consumer = peer.Consumers.Values.Where(m => m.Internal.ProducerId == producer.ProducerId).FirstOrDefault();
                            if (consumer != null)
                            {
                                // 如果本 Peer 已经消费了对应 Producer，忽略。
                                continue;
                            }
                            existsProducers.Add(new PeerProducer
                            {
                                Peer = otherPeer,
                                Producer = producer,
                            });
                            continue;
                        }
                        // 如果 Source 没有对应的 Producer，通知 otherPeer 生产；生产成功后又要通知本 Peer 去对应的 Room 消费。
                        inviteProduceSources.Add(source!);
                        otherPeer.ConsumerPaddings[source!] = new PeerRoom
                        {
                            Room = room,
                            Peer = peer,
                        };
                    }

                    return new InviteProduceResult
                    {
                        Peer = peer,
                        ExistsProducers = existsProducers.ToArray(),
                        InviteProduceSources = inviteProduceSources.ToArray(),
                    };
                }
            }
        }

        public Task<Producer> ProduceAsync(string peerId,ProduceRequest produceRequest)
        {
            lock (_peerLocker)
            {
                if (!Peers.TryGetValue(peerId, out var peer))
                {
                    _logger.LogError($"ProduceAsync() | Peer:{peerId} is not exists.");
                    throw new Exception($"Peer:{peerId} is not exists.");
                }
                return peer.ProduceAsync(produceRequest);
            }
        }

        #region Private Methods

        private async Task<Group> CreateGroupAsync(Guid groupId, string name)
        {
            // Router media codecs.
            var mediaCodecs = _mediasoupOptions.MediasoupSettings.RouterSettings.RtpCodecCapabilities;

            // Create a mediasoup Router.
            var worker = _mediasoupServer.GetWorker();
            var router = await worker.CreateRouterAsync(new RouterOptions
            {
                MediaCodecs = mediaCodecs
            });

            var group = new Group(_loggerFactory, router, groupId, name);
            Groups[groupId] = group;
            return group;
        }

        #endregion
    }
}
