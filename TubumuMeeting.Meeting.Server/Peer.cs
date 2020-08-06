using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Nito.AsyncEx;
using Tubumu.Core.Extensions;
using TubumuMeeting.Mediasoup;
using TubumuMeeting.Mediasoup.Extensions;

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
        /// <summary>
        /// Logger factory for create logger.
        /// </summary>
        private readonly ILoggerFactory _loggerFactory;

        /// <summary>
        /// Logger
        /// </summary>
        private readonly ILogger<Peer> _logger;

        private readonly AsyncLock _locker = new AsyncLock();

        private readonly WebRtcTransportSettings _webRtcTransportSettings;

        public bool Closed { get; private set; }

        public RtpCapabilities? RtpCapabilities { get; set; }

        public SctpCapabilities? SctpCapabilities { get; set; }

        public Group Group { get; private set; }

        public Dictionary<string, RoomWithInterestedSources> Rooms { get; } = new Dictionary<string, RoomWithInterestedSources>();

        public Dictionary<string, Transport> Transports { get; } = new Dictionary<string, Transport>();

        public Dictionary<string, Producer> Producers { get; } = new Dictionary<string, Producer>();

        public Dictionary<string, Consumer> Consumers { get; } = new Dictionary<string, Consumer>();

        public Dictionary<string, DataProducer> DataProducers { get; } = new Dictionary<string, DataProducer>();

        public Dictionary<string, DataConsumer> DataConsumers { get; } = new Dictionary<string, DataConsumer>();

        public string[]? Sources { get; set; }

        public Dictionary<string, object>? AppData { get; set; }

        public Peer(ILoggerFactory loggerFactory, WebRtcTransportSettings webRtcTransportSettings, Group group, string peerId, string displayName)
        {
            _loggerFactory = loggerFactory;
            _logger = loggerFactory.CreateLogger<Peer>();
            _webRtcTransportSettings = webRtcTransportSettings;
            Group = group;
            PeerId = peerId;
            DisplayName = displayName.IsNullOrWhiteSpace() ? "Guest" : displayName;
            Closed = false;
        }

        public void Close()
        {
            if (Closed)
            {
                return;
            }

            using (_locker.Lock())
            {
                if (Closed)
                {
                    return;
                }

                Closed = true;
                RtpCapabilities = null;
                SctpCapabilities = null;

                // Iterate and close all mediasoup Transport associated to this Peer, so all
                // its Producers and Consumers will also be closed.
                Transports.Values.ForEach(m => m.Close());
            }
        }

        public async Task<WebRtcTransport> CreateWebRtcTransport(CreateWebRtcTransportRequest createWebRtcTransportRequest)
        {
            CheckClosed();
            using (await _locker.LockAsync())
            {
                CheckClosed();

                if (!(createWebRtcTransportRequest.Consuming ^ createWebRtcTransportRequest.Producing))
                {
                    throw new Exception("Consumer or Producing");
                }

                if (createWebRtcTransportRequest.Consuming && Transports.Values.Any(m => m.AppData != null && m.AppData.TryGetValue("Consuming", out var value) && (bool)value))
                {
                    throw new Exception("Consuming transport exists");
                }

                if (createWebRtcTransportRequest.Producing && Transports.Values.Any(m => m.AppData != null && m.AppData.TryGetValue("Producing", out var value) && (bool)value))
                {
                    throw new Exception("Producing transport exists");
                }

                var webRtcTransportOptions = new WebRtcTransportOptions
                {
                    ListenIps = _webRtcTransportSettings.ListenIps,
                    InitialAvailableOutgoingBitrate = _webRtcTransportSettings.InitialAvailableOutgoingBitrate,
                    MaxSctpMessageSize = _webRtcTransportSettings.MaxSctpMessageSize,
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

                var transport = await Group.Router.CreateWebRtcTransportAsync(webRtcTransportOptions);

                if (transport == null)
                {
                    throw new Exception("Router.CreateWebRtcTransport faild");
                }
                // Store the WebRtcTransport into the Peer data Object.
                Transports[transport.TransportId] = transport;

                // If set, apply max incoming bitrate limit.
                if (_webRtcTransportSettings.MaximumIncomingBitrate.HasValue && _webRtcTransportSettings.MaximumIncomingBitrate.Value > 0)
                {
                    // Fire and forget
                    transport.SetMaxIncomingBitrateAsync(_webRtcTransportSettings.MaximumIncomingBitrate.Value).ContinueWithOnFaultedHandleLog(_logger);
                }

                return transport;
            }
        }

        public async Task<bool> ConnectWebRtcTransport(ConnectWebRtcTransportRequest connectWebRtcTransportRequest)
        {
            CheckClosed();
            using (await _locker.LockAsync())
            {
                CheckClosed();

                if (!Transports.TryGetValue(connectWebRtcTransportRequest.TransportId, out var transport))
                {
                    throw new Exception($"Transport:{connectWebRtcTransportRequest.TransportId} is not exists");
                }

                await transport.ConnectAsync(connectWebRtcTransportRequest.DtlsParameters);
                return true;
            }
        }

        public Transport GetProducingTransport()
        {
            CheckClosed();
            using (_locker.Lock())
            {
                CheckClosed();
                return Transports.Values.Where(m => m.AppData != null && m.AppData.TryGetValue("Producing", out var value) && (bool)value).FirstOrDefault();
            }
        }

        public Transport GetConsumingTransport()
        {
            CheckClosed();
            using (_locker.Lock())
            {
                CheckClosed();

                return Transports.Values.Where(m => m.AppData != null && m.AppData.TryGetValue("Consuming", out var value) && (bool)value).FirstOrDefault();
            }
        }

        private void CheckClosed()
        {
            if (Closed)
            {
                throw new Exception("Peer was closed");
            }
        }
    }
}
