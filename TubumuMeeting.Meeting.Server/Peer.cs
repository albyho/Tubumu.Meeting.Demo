using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
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

        private readonly Router _router;

        private readonly RtpCapabilities _rtpCapabilities;

        private readonly SctpCapabilities? _sctpCapabilities;

        public Dictionary<string, Room> Rooms { get; } = new Dictionary<string, Room>();

        private readonly Dictionary<string, Transport> _transports = new Dictionary<string, Transport>();

        private readonly Dictionary<string, Producer> _producers = new Dictionary<string, Producer>();

        private readonly Dictionary<string, Consumer> _consumers = new Dictionary<string, Consumer>();

        private readonly Dictionary<string, DataProducer> _dataProducers = new Dictionary<string, DataProducer>();

        private readonly Dictionary<string, DataConsumer> _dataConsumers = new Dictionary<string, DataConsumer>();

        public List<PullPaddingConsume> PullPaddingConsumes { get; set; } = new List<PullPaddingConsume>();  // TODO: (alby)只在 Scheduler 的 _peerRoomLocker 之内使用，考虑改为私有。

        public string[] Sources { get; private set; }

        public Dictionary<string, object> AppData { get; private set; }

        public Peer(ILoggerFactory loggerFactory, WebRtcTransportSettings webRtcTransportSettings, Router router, RtpCapabilities rtpCapabilities, SctpCapabilities? sctpCapabilities, string peerId, string displayName, string[]? sources, Dictionary<string, object>? appData)
        {
            _loggerFactory = loggerFactory;
            _logger = loggerFactory.CreateLogger<Peer>();
            _webRtcTransportSettings = webRtcTransportSettings;
            _router = router;
            _rtpCapabilities = rtpCapabilities;
            _sctpCapabilities = sctpCapabilities;
            PeerId = peerId;
            DisplayName = displayName.NullOrWhiteSpaceReplace("Guest");
            Sources = sources ?? new string[0];
            AppData = appData ?? new Dictionary<string, object>();
            Closed = false;
        }

        /// <summary>
        /// Close
        /// </summary>
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

                // Iterate and close all mediasoup Transport associated to this Peer, so all
                // its Producers and Consumers will also be closed.
                _transports.Values.ForEach(m => m.Close());
                _transports.Clear();
            }
        }

        /// <summary>
        /// 创建 WebRtcTransport
        /// </summary>
        /// <param name="createWebRtcTransportRequest"></param>
        /// <returns></returns>
        public async Task<WebRtcTransport> CreateWebRtcTransportAsync(CreateWebRtcTransportRequest createWebRtcTransportRequest)
        {
            CheckClosed();
            using (await _locker.LockAsync())
            {
                CheckClosed();

                if (!(createWebRtcTransportRequest.Consuming ^ createWebRtcTransportRequest.Producing))
                {
                    throw new Exception("CreateWebRtcTransportAsync() | Consumer or Producing");
                }

                if (createWebRtcTransportRequest.Consuming && HasConsumingTransport())
                {
                    throw new Exception("CreateWebRtcTransportAsync() | Consuming transport exists");
                }

                if (createWebRtcTransportRequest.Producing && HasProducingTransport())
                {
                    throw new Exception("CreateWebRtcTransportAsync() | Producing transport exists");
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

                var transport = await _router.CreateWebRtcTransportAsync(webRtcTransportOptions);

                if (transport == null)
                {
                    throw new Exception("CreateWebRtcTransportAsync() | Router.CreateWebRtcTransport faild");
                }
                // Store the WebRtcTransport into the Peer data Object.
                _transports[transport.TransportId] = transport;

                // If set, apply max incoming bitrate limit.
                if (_webRtcTransportSettings.MaximumIncomingBitrate.HasValue && _webRtcTransportSettings.MaximumIncomingBitrate.Value > 0)
                {
                    // Fire and forget
                    transport.SetMaxIncomingBitrateAsync(_webRtcTransportSettings.MaximumIncomingBitrate.Value).ContinueWithOnFaultedHandleLog(_logger);
                }

                return transport;
            }
        }

        /// <summary>
        /// 连接 WebRtcTransport
        /// </summary>
        /// <param name="connectWebRtcTransportRequest"></param>
        /// <returns></returns>
        public async Task<bool> ConnectWebRtcTransportAsync(ConnectWebRtcTransportRequest connectWebRtcTransportRequest)
        {
            CheckClosed();
            using (await _locker.LockAsync())
            {
                CheckClosed();

                if (!_transports.TryGetValue(connectWebRtcTransportRequest.TransportId, out var transport))
                {
                    throw new Exception($"ConnectWebRtcTransportAsync() | Transport:{connectWebRtcTransportRequest.TransportId} is not exists");
                }

                await transport.ConnectAsync(connectWebRtcTransportRequest.DtlsParameters);
                return true;
            }
        }

        /// <summary>
        /// 生产
        /// </summary>
        /// <param name="produceRequest"></param>
        /// <returns></returns>
        public async Task<Producer> ProduceAsync(ProduceRequest produceRequest)
        {
            CheckClosed();
            using (await _locker.LockAsync())
            {
                CheckClosed();

                if (produceRequest.AppData == null || !produceRequest.AppData.TryGetValue("source", out var sourceObj))
                {
                    throw new Exception($"ProduceAsync() | Peer:{PeerId} AppData[\"source\"] is null.");
                }
                var source = sourceObj.ToString();

                if (produceRequest.AppData == null || !produceRequest.AppData.TryGetValue("roomId", out var roomIdObj))
                {
                    throw new Exception($"ProduceAsync() | Peer:{ PeerId} AppData[\"roomId\"] is null.");
                }
                var roomId = roomIdObj.ToString();

                if (!Rooms.TryGetValue(roomId, out var room))
                {
                    throw new Exception($"ProduceAsync() | Peer:{ PeerId} is not in Room:{roomId}.");
                }

                var transport = GetProducingTransport();
                if (transport == null)
                {
                    throw new Exception($"ProduceAsync() | Transport:Producing is not exists.");
                }

                if (Sources == null || !Sources.Contains(source))
                {
                    throw new Exception($"ProduceAsync() | Source:\"{ source }\" cannot be produce.");
                }

                // TODO: (alby)线程安全：避免重复 Produce 相同的 Sources
                var producer = _producers.Values.FirstOrDefault(m => m.Source == source);
                if (producer != null)
                {
                    throw new Exception($"ProduceAsync() | Source:\"{ source }\" is exists, close it.");
                }

                // Add peerId into appData to later get the associated Peer during
                // the 'loudest' event of the audioLevelObserver.
                produceRequest.AppData["peerId"] = PeerId;

                producer = await transport.ProduceAsync(new ProducerOptions
                {
                    Kind = produceRequest.Kind,
                    RtpParameters = produceRequest.RtpParameters,
                    AppData = produceRequest.AppData,
                });

                // Store producer source
                producer.Source = source;

                // Store the Producer into the Peer data Object.
                _producers[producer.ProducerId] = producer;

                return producer;
            }
        }

        public async Task<Consumer> ConsumeAsync(Producer producer, string roomId)
        {
            CheckClosed();
            using (_locker.Lock())
            {
                CheckClosed();

                if (_rtpCapabilities == null || !_router.CanConsume(producer.ProducerId, _rtpCapabilities))
                {
                    throw new Exception($"ConsumeAsync() | Peer:{PeerId} Can not consume.");
                }

                var transport = GetConsumingTransport();

                // This should not happen.
                if (transport == null)
                {
                    throw new Exception($"ConsumeAsync() | Peer:{PeerId} Transport for consuming not found.");
                }

                // Create the Consumer in paused mode.
                var consumer = await transport.ConsumeAsync(new ConsumerOptions
                {
                    ProducerId = producer.ProducerId,
                    RtpCapabilities = _rtpCapabilities,
                    Paused = true // Or: producer.Kind == MediaKind.Video
                });

                // Store RoomId
                consumer.RoomId = roomId;

                // Store producer source
                consumer.Source = producer.Source;

                // Store the Consumer into the consumerPeer data Object.
                _consumers[consumer.ConsumerId] = consumer;

                return consumer;
            }
        }

        public async Task<bool> CloseProducerAsync(string producerId)
        {
            CheckClosed();
            using (await _locker.LockAsync())
            {
                CheckClosed();

                if (_producers.TryGetValue(producerId, out var producer))
                {
                    throw new Exception($"CloseProducerAsync() | Peer:{PeerId} has no Producer:{producerId}.");
                }

                producer.Close();
                _producers.Remove(producerId);
                return true;
            }
        }

        public async Task<bool> PauseProducerAsync(string producerId)
        {
            CheckClosed();
            using (await _locker.LockAsync())
            {
                CheckClosed();

                if (_producers.TryGetValue(producerId, out var producer))
                {
                    throw new Exception($"PauseProducerAsync() | Peer:{PeerId} has no Producer:{producerId}.");
                }

                await producer.PauseAsync();
                return true;
            }
        }

        public async Task<bool> ResumeProducerAsync(string producerId)
        {
            CheckClosed();
            using (await _locker.LockAsync())
            {
                if (_producers.TryGetValue(producerId, out var producer))
                {
                    throw new Exception($"ResumeProducerAsync() | Peer:{PeerId} has no Producer:{producerId}.");
                }

                await producer.ResumeAsync();
                return true;
            }
        }

        public async Task<bool> CloseConsumerAsync(string consumerId)
        {
            CheckClosed();
            using (await _locker.LockAsync())
            {
                CheckClosed();

                if (_consumers.TryGetValue(consumerId, out var consumer))
                {
                    throw new Exception($"CloseConsumerAsync() | Peer:{PeerId} has no Cmonsumer:{consumerId}.");
                }

                consumer.Close();
                _consumers.Remove(consumerId);
                return true;
            }
        }

        public async Task<bool> PauseConsumerAsync(string consumerId)
        {
            CheckClosed();
            using (await _locker.LockAsync())
            {
                CheckClosed();

                if (_consumers.TryGetValue(consumerId, out var consumer))
                {
                    throw new Exception($"PauseConsumerAsync() | Peer:{PeerId} has no Consumer:{consumerId}.");
                }

                await consumer.PauseAsync();
                return true;
            }
        }

        public async Task<Consumer> ResumeConsumerAsync(string consumerId)
        {
            CheckClosed();
            using (await _locker.LockAsync())
            {
                CheckClosed();

                if (!_consumers.TryGetValue(consumerId, out var consumer))
                {
                    throw new Exception($"ResumeConsumerAsync() | Peer:{PeerId} has no Consumer:{consumerId}.");
                }

                await consumer.ResumeAsync();
                return consumer;
            }
        }

        public async Task<bool> SetConsumerPreferedLayersAsync(SetConsumerPreferedLayersRequest setConsumerPreferedLayersRequest)
        {
            CheckClosed();
            using (await _locker.LockAsync())
            {
                CheckClosed();

                if (_consumers.TryGetValue(setConsumerPreferedLayersRequest.ConsumerId, out var consumer))
                {
                    throw new Exception($"SetConsumerPreferedLayersAsync() | Peer:{PeerId} has no Consumer:{setConsumerPreferedLayersRequest.ConsumerId}.");
                }

                await consumer.SetPreferredLayersAsync(setConsumerPreferedLayersRequest);
                return true;
            }
        }

        public async Task<bool> SetConsumerPriorityAsync(SetConsumerPriorityRequest setConsumerPriorityRequest)
        {
            CheckClosed();
            using (await _locker.LockAsync())
            {
                CheckClosed();

                if (_consumers.TryGetValue(setConsumerPriorityRequest.ConsumerId, out var consumer))
                {
                    throw new Exception($"SetConsumerPriorityAsync() | Peer:{PeerId} has no Consumer:{setConsumerPriorityRequest.ConsumerId}.");
                }

                await consumer.SetPriorityAsync(setConsumerPriorityRequest.Priority);
                return true;
            }
        }

        public async Task<bool> RequestConsumerKeyFrameAsync(string consumerId)
        {
            CheckClosed();
            using (await _locker.LockAsync())
            {
                CheckClosed();

                if (_consumers.TryGetValue(consumerId, out var consumer))
                {
                    throw new Exception($"RequestConsumerKeyFrameAsync() | Peer:{PeerId} has no Producer:{consumerId}.");
                }

                await consumer.RequestKeyFrameAsync();
                return true;
            }
        }

        public async Task<TransportStat> GetTransportStatsAsync(string transportId)
        {
            CheckClosed();
            using (await _locker.LockAsync())
            {
                CheckClosed();

                if (_transports.TryGetValue(transportId, out var transport))
                {
                    throw new Exception($"GetTransportStatsAsync() | Peer:{PeerId} has no Transport:{transportId}.");
                }

                var status = await transport.GetStatsAsync();
                // TODO: (alby)考虑不进行反序列化
                // TODO: (alby)实际上有 WebTransportStat、PlainTransportStat、PipeTransportStat 和 DirectTransportStat。这里反序列化后会丢失数据。
                var data = JsonConvert.DeserializeObject<TransportStat>(status!);
                return data;
            }
        }

        public async Task<ProducerStat> GetProducerStatsAsync(string producerId)
        {
            CheckClosed();
            using (await _locker.LockAsync())
            {
                CheckClosed();

                if (_producers.TryGetValue(producerId, out var producer))
                {
                    throw new Exception($"GetProducerStatsAsync() | Peer:{PeerId} has no Producer:{producerId}.");
                }

                var status = await producer.GetStatsAsync();
                // TODO: (alby)考虑不进行反序列化
                var data = JsonConvert.DeserializeObject<ProducerStat>(status!);
                return data;
            }
        }

        public async Task<ConsumerStat> GetConsumerStatsAsync(string consumerId)
        {
            CheckClosed();
            using (await _locker.LockAsync())
            {
                CheckClosed();

                if (_consumers.TryGetValue(consumerId, out var consumer))
                {
                    throw new Exception($"GetConsumerStatsAsync() | Peer:{PeerId} has no Consumer:{consumerId}.");
                }

                var status = await consumer.GetStatsAsync();
                // TODO: (alby)考虑不进行反序列化
                var data = JsonConvert.DeserializeObject<ConsumerStat>(status!);
                return data;
            }
        }

        public async Task<IceParameters?> RestartIceAsync(string transportId)
        {
            CheckClosed();
            using (await _locker.LockAsync())
            {
                CheckClosed();

                if (_transports.TryGetValue(transportId, out var transport))
                {
                    throw new Exception($"RestartIceAsync() | Peer:{PeerId} has no Transport:{transportId}.");
                }

                if (!(transport is WebRtcTransport webRtcTransport))
                {
                    throw new Exception($"RestartIceAsync() | Peer:{PeerId} Transport:{transportId} is not WebRtcTransport.");
                }

                var iceParameters = await webRtcTransport.RestartIceAsync();
                return iceParameters;
            }
        }

        /// <summary>
        /// 关闭其他房间无人消费的 Producer
        /// </summary>
        /// <param name="excludeRoomId"></param>
        public void CloseProducersNoConsumers(string excludeRoomId)
        {
            using (_locker.Lock())
            {
                var producersToClose = new HashSet<Producer>();
                var consumers = from ri in Rooms.Values             // Peer 所在的所有房间
                                from p in ri.Peers.Values           // 的包括本 Peer 在内的所有 Peer
                                from pc in p._consumers.Values      // 的 Consumer
                                where ri.RoomId != excludeRoomId    // 排除房间
                                select pc;

                foreach (var producer in _producers.Values)
                {
                    // 如果其他 Room 中没有消费 producer，则关闭。
                    if (!consumers.Any(m => m.Internal.ProducerId == producer.ProducerId && m.RoomId == excludeRoomId))
                    {
                        producersToClose.Add(producer);
                    }
                }

                // Producer 关闭后会触发相应的 Consumer `producerclose` 事件，从而拥有 Consumer 的 Peer 能够关闭该 Consumer 并通知客户端。
                foreach (var producerToClose in producersToClose)
                {
                    producerToClose.Close();
                    _producers.Remove(producerToClose.ProducerId);
                }
            }
        }

        /// <summary>
        /// 关闭除指定 Room 里的指定 Peer 外无人消费的 Producer
        /// </summary>
        /// <param name="excludeRoomId"></param>
        /// <param name="excludePeerId"></param>
        public void CloseProducersNoConsumers(string excludeRoomId, string excludePeerId)
        {
            using (_locker.Lock())
            {
                // 关闭无人消费的本 Peer 的 Producer
                var producersToClose = new HashSet<Producer>();
                var otherPeers = from ri in Rooms.Values        // Peer 所在的所有房间
                                 from p in ri.Peers.Values      // 的包括本 Peer 在内的所有 Peer
                                 where !(ri.RoomId == excludeRoomId && p.PeerId == excludePeerId)   // 除指定 Room 里的指定 Peer
                                 select p;

                foreach (var otherPeer in otherPeers)
                {
                    foreach (var producer in _producers.Values)
                    {
                        // 如果没有消费 producer，则关闭。
                        if (!otherPeer._consumers.Values.Any(m => m.Internal.ProducerId == producer.ProducerId && !(m.RoomId == excludeRoomId && otherPeer.PeerId == excludePeerId)))
                        {
                            producersToClose.Add(producer);
                        }
                    }
                }

                // Producer 关闭后会触发相应的 Consumer `producerclose` 事件，从而拥有 Consumer 的 Peer 能够关闭该 Consumer 并通知客户端。
                foreach (var producerToClose in producersToClose)
                {
                    producerToClose.Close();
                    _producers.Remove(producerToClose.ProducerId);
                }
            }
        }

        public void RemoveConsumer(string consumerId)
        {
            // 在 Closed 的情况下也允许删除
            // 不加锁。因为 Peer.Close() -> Trasport.Close() -> Transport.Closed() -> consumer.TransportClosed() -> Event: transportclosed -> Peer.RemoveConsumer()
            _consumers.Remove(consumerId);
        }

        public Producer? GetProducerBySource(string source)
        {
            // 在 Closed 的情况下也允许获取
            using (_locker.Lock())
            {
                return _producers.Values.Where(m => m.Source == source).FirstOrDefault();
            }
        }

        public Consumer? GetConsumerByProducerId(string producerId)
        {
            // 在 Closed 的情况下也允许获取
            using (_locker.Lock())
            {
                return _consumers.Values.Where(m => m.Internal.ProducerId == producerId).FirstOrDefault();
            }
        }

        #region Private Methods

        private Transport GetProducingTransport()
        {
            return _transports.Values.Where(m => m.AppData != null && m.AppData.TryGetValue("Producing", out var value) && (bool)value).FirstOrDefault();
        }

        private Transport GetConsumingTransport()
        {
            return _transports.Values.Where(m => m.AppData != null && m.AppData.TryGetValue("Consuming", out var value) && (bool)value).FirstOrDefault();
        }

        private bool HasProducingTransport()
        {
            return _transports.Values.Any(m => m.AppData != null && m.AppData.TryGetValue("Producing", out var value) && (bool)value);
        }

        private bool HasConsumingTransport()
        {
            return _transports.Values.Any(m => m.AppData != null && m.AppData.TryGetValue("Consuming", out var value) && (bool)value);
        }

        private void CheckClosed()
        {
            if (Closed)
            {
                throw new Exception($"CheckClosed() | Peer:{PeerId} was closed");
            }
        }

        #endregion
    }
}
