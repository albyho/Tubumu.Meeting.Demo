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
    public class Lobby
    {
        #region Private Fields

        /// <summary>
        /// Logger factory for create logger.
        /// </summary>
        private readonly ILoggerFactory _loggerFactory;

        /// <summary>
        /// Logger
        /// </summary>
        private readonly ILogger<Lobby> _logger;

        private readonly MediasoupOptions _mediasoupOptions;

        private readonly MediasoupServer _mediasoupServer;

        private readonly AsyncLock _groupLocker = new AsyncLock();

        private readonly AsyncLock _peerLocker = new AsyncLock();

        #endregion

        public RtpCapabilities DefaultRtpCapabilities { get; private set; }

        private Dictionary<Guid, Group> Groups { get; } = new Dictionary<Guid, Group>();

        public Dictionary<string, Peer> Peers { get; } = new Dictionary<string, Peer>();

        public Lobby(ILoggerFactory loggerFactory, MediasoupOptions mediasoupOptions, MediasoupServer mediasoupServer)
        {
            _loggerFactory = loggerFactory;
            _logger = _loggerFactory.CreateLogger<Lobby>();
            _mediasoupOptions = mediasoupOptions;
            _mediasoupServer = mediasoupServer;

            var rtpCodecCapabilities = mediasoupOptions.MediasoupSettings.RouterSettings.RtpCodecCapabilities;
            // This may throw.
            DefaultRtpCapabilities = ORTC.GenerateRouterRtpCapabilities(rtpCodecCapabilities);
        }

        public async Task<bool> PeerJoinGroupAsync(string peerId, JoinRequest joinRequest)
        {
            PeerLeaveGroup(peerId);

            using (await _groupLocker.LockAsync())
            {
                if (!Groups.TryGetValue(joinRequest.GroupId, out var group))
                {
                    group = await CreateGroupAsync(joinRequest.GroupId, "Default");
                }

                using (_peerLocker.Lock())
                {
                    var peer = new Peer(peerId, joinRequest.DisplayName)
                    {
                        RtpCapabilities = joinRequest.RtpCapabilities,
                        SctpCapabilities = joinRequest.SctpCapabilities,
                        Sources = joinRequest.Sources,
                        AppData = joinRequest.AppData
                    };

                    peer.JoinGroup(group);
                    Peers[peerId] = peer;

                    return true;
                }
            }
        }

        public bool PeerLeaveGroup(string peerId)
        {
            using (_peerLocker.Lock())
            {
                if (!Peers.TryGetValue(peerId, out var peer))
                {
                    return true;
                }

                Peers.Remove(peerId);

                return true;
            }
        }

        public RoomInterestedSources? PeerJoinRoom(string peerId, JoinRoomRequest joinRoomRequest)
        {
            using (_peerLocker.Lock())
            {
                if (!Peers.TryGetValue(peerId, out var peer))
                {
                    _logger.LogError($"PeerJoinRoom() | Peer:{peerId} is not exists.");
                    return null;
                }

                if (peer.Group == null)
                {
                    _logger.LogError($"PeerJoinRoom() | Peer:{peerId} is not in any Group.");
                    return null;
                }

                return peer.Group.PeerJoinRoom(peer, joinRoomRequest);
            }
        }

        public bool PeerLeaveRoom(string peerId, string roomId)
        {
            using (_peerLocker.Lock())
            {
                if (!Peers.TryGetValue(peerId, out var peer))
                {
                    _logger.LogError($"PeerLeaveRoom() | Peer[{peerId}] is not exists.");

                    return false;
                }

                return peer.LeaveRoom(roomId);
            }
        }

        public async Task<CreateWebRtcTransportResult> CreateWebRtcTransportAsync(string peerId, CreateWebRtcTransportRequest createWebRtcTransportRequest)
        {
            using (await _peerLocker.LockAsync())
            {
                if (!Peers.TryGetValue(peerId, out var peer))
                {
                    throw new Exception($"CreateWebRtcTransportAsync() | Peer:{peer.PeerId} is not exists");
                }

                var t = await peer.Group.CreateWebRtcTransportAsync(peer, createWebRtcTransportRequest);
            }


            var webRtcTransportSettings = _mediasoupOptions.MediasoupSettings.WebRtcTransportSettings;
            var webRtcTransportOptions = new WebRtcTransportOptions
            {
                ListenIps = webRtcTransportSettings.ListenIps,
                InitialAvailableOutgoingBitrate = webRtcTransportSettings.InitialAvailableOutgoingBitrate,
                MaxSctpMessageSize = webRtcTransportSettings.MaxSctpMessageSize,
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

            var transport = await Peer!.Group.Router.CreateWebRtcTransportAsync(webRtcTransportOptions);
            if (transport == null)
            {
                throw new Exception($"CreateWebRtcTransportAsync() | Peer:{peer.PeerId} was already in Group:{GroupId}");
            }

            // Store the WebRtcTransport into the Peer data Object.
            Peer!.Transports[transport.TransportId] = transport;

            transport.On("sctpstatechange", sctpState =>
            {
                _logger.LogDebug($"WebRtcTransport \"sctpstatechange\" event [sctpState:{sctpState}]");
            });

            transport.On("dtlsstatechange", value =>
            {
                var dtlsState = (DtlsState)value!;
                if (dtlsState == DtlsState.Failed || dtlsState == DtlsState.Closed)
                {
                    _logger.LogWarning($"WebRtcTransport dtlsstatechange event [dtlsState:{value}]");
                }
            });

            // NOTE: For testing.
            //await transport.EnableTraceEventAsync(new[] { TransportTraceEventType.Probation, TransportTraceEventType.BWE });
            //await transport.EnableTraceEventAsync(new[] { TransportTraceEventType.BWE });

            transport.On("trace", trace =>
            {
                var traceData = (TransportTraceEventData)trace!;
                _logger.LogDebug($"transport \"trace\" event [transportId:{transport.TransportId}, trace:{traceData.Type.GetEnumStringValue()}]");

                if (traceData.Type == TransportTraceEventType.BWE && traceData.Direction == TraceEventDirection.Out)
                {
                    // Message: downlinkBwe
                    var client = _hubContext.Clients.User(peerId);
                    client.ReceiveMessage(new MeetingMessage
                    {
                        Code = 200,
                        InternalCode = "downlinkBwe",
                        Message = "downlinkBwe",
                        Data = new
                        {
                            DesiredBitrate = traceData.Info["desiredBitrate"],
                            EffectiveDesiredBitrate = traceData.Info["effectiveDesiredBitrate"],
                            AvailableBitrate = traceData.Info["availableBitrate"]
                        }
                    }).ContinueWithOnFaultedHandleLog(_logger);
                }
            });

            // If set, apply max incoming bitrate limit.
            if (webRtcTransportSettings.MaximumIncomingBitrate.HasValue && webRtcTransportSettings.MaximumIncomingBitrate.Value > 0)
            {
                // Fire and forget
                transport.SetMaxIncomingBitrateAsync(webRtcTransportSettings.MaximumIncomingBitrate.Value).ContinueWithOnFaultedHandleLog(_logger);
            }

            return new MeetingMessage
            {
                Code = 200,
                Message = "CreateWebRtcTransport 成功",
                Data = new CreateWebRtcTransportResult
                {
                    Id = transport.TransportId,
                    IceParameters = transport.IceParameters,
                    IceCandidates = transport.IceCandidates,
                    DtlsParameters = transport.DtlsParameters,
                    SctpParameters = transport.SctpParameters,
                }
            };
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
