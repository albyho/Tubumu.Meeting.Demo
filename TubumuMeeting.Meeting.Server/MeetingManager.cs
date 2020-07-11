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
    public class MeetingManager
    {
        #region Private Fields

        /// <summary>
        /// Logger factory for create logger.
        /// </summary>
        private readonly ILoggerFactory _loggerFactory;

        /// <summary>
        /// Logger
        /// </summary>
        private readonly ILogger<MeetingManager> _logger;

        private readonly MediasoupOptions _mediasoupOptions;

        private readonly MediasoupServer _mediasoupServer;

        private readonly AsyncLock _groupLocker = new AsyncLock();

        private readonly object _peerLocker = new object();

        private readonly object _peerGroupLocker = new object();

        #endregion

        public RtpCapabilities DefaultRtpCapabilities { get; private set; }

        public Dictionary<Guid, Group> Groups { get; } = new Dictionary<Guid, Group>();

        public Dictionary<string, Peer> Peers { get; } = new Dictionary<string, Peer>();

        public MeetingManager(ILoggerFactory loggerFactory, MediasoupOptions mediasoupOptions, MediasoupServer mediasoupServer)
        {
            _loggerFactory = loggerFactory;
            _logger = _loggerFactory.CreateLogger<MeetingManager>();
            _mediasoupOptions = mediasoupOptions;
            _mediasoupServer = mediasoupServer;

            var rtpCodecCapabilities = mediasoupOptions.MediasoupSettings.RouterSettings.RtpCodecCapabilities;
            // This may throw.
            DefaultRtpCapabilities = ORTC.GenerateRouterRtpCapabilities(rtpCodecCapabilities);
        }

        #region Peer

        public bool PeerHandle(string peerId, string name)
        {
            PeerClose(peerId);

            var peer = new Peer(peerId, name);
            lock (_peerLocker)
            {
                if (Peers.TryGetValue(peerId, out var _))
                {
                    _logger.LogError($"PeerHandle() | Peer[{peerId}] is exists.");
                    return false;
                }

                Peers[peerId] = peer;
            }

            return true;
        }

        public async Task<bool> PeerJoinAsync(string peerId, RtpCapabilities rtpCapabilities, SctpCapabilities? sctpCapabilities, string[]? sources, Guid groupId, Dictionary<string, object>? appData)
        {
            using (await _groupLocker.LockAsync())
            {
                if (!Groups.TryGetValue(groupId, out var group))
                {
                    group = await CreateGroupAsync(groupId, "Default");
                }

                lock (_peerLocker)
                {
                    if (!Peers.TryGetValue(peerId, out var peer))
                    {
                        _logger.LogError($"PeerJoin() | Peer[{peerId}] is not exists.");
                        return false;
                    }

                    if (peer.Joined)
                    {
                        _logger.LogWarning($"PeerJoin() | Peer[{peerId}] is joined.");
                    }

                    peer.RtpCapabilities = rtpCapabilities;
                    peer.SctpCapabilities = sctpCapabilities;
                    peer.Sources = sources;
                    peer.AppData = appData;
                    peer.Joined = true;

                    lock (_peerGroupLocker)
                    {
                        group.Peers[peerId] = peer;
                        peer.Group = group;
                        return true;
                    }
                }
            }
        }

        public void PeerClose(string peerId)
        {
            lock (_peerLocker)
            {
                if (!Peers.TryGetValue(peerId, out var peer))
                {
                    return;
                }

                peer.Close();
                Peers.Remove(peerId);

                lock (_peerGroupLocker)
                {
                    if (peer.Group != null)
                    {
                        peer.Group.Peers.Remove(peerId);
                        peer.Group = null;
                    }
                }
            }
        }

        #endregion

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
            return group;
        }

        #endregion
    }
}
