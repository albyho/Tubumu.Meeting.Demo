using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Nito.AsyncEx;
using Tubumu.Core.Extensions;
using TubumuMeeting.Mediasoup.Extensions;

namespace TubumuMeeting.Mediasoup
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

        private readonly AsyncReaderWriterLock _locker = new AsyncReaderWriterLock();

        public bool Joined { get; private set; }

        private readonly WebRtcTransportSettings _webRtcTransportSettings;

        private readonly Router _router;

        private readonly RtpCapabilities _rtpCapabilities;

        private readonly SctpCapabilities? _sctpCapabilities;

        private readonly ConcurrentDictionary<string, Transport> _transports = new ConcurrentDictionary<string, Transport>();

        private readonly ConcurrentDictionary<string, Producer> _producers = new ConcurrentDictionary<string, Producer>();

        private readonly ConcurrentDictionary<string, Consumer> _consumers = new ConcurrentDictionary<string, Consumer>();

        private readonly ConcurrentDictionary<string, DataProducer> _dataProducers = new ConcurrentDictionary<string, DataProducer>();

        private readonly ConcurrentDictionary<string, DataConsumer> _dataConsumers = new ConcurrentDictionary<string, DataConsumer>();

        private readonly List<PullPadding> _pullPaddings = new List<PullPadding>();

        /// <summary>
        /// Rooms 只允许 Scheduler 访问，由后者的 _peerRoomLocker 保护。
        /// </summary>
        public Dictionary<string, Room> Rooms { get; } = new Dictionary<string, Room>();

        public string[] Sources { get; private set; }

        public Dictionary<string, object>? AppData { get; set; }

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
            Joined = true;
        }

        /// <summary>
        /// 创建 WebRtcTransport
        /// </summary>
        /// <param name="createWebRtcTransportRequest"></param>
        /// <returns></returns>
        public async Task<WebRtcTransport> CreateWebRtcTransportAsync(CreateWebRtcTransportRequest createWebRtcTransportRequest)
        {
            CheckJoined();
            using (await _locker.WriterLockAsync())
            {
                CheckJoined();

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
                transport.On("@close", _ => _transports.TryRemove(transport.TransportId, out var _));

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
            CheckJoined();
            using (await _locker.ReaderLockAsync())
            {
                CheckJoined();

                if (!_transports.TryGetValue(connectWebRtcTransportRequest.TransportId, out var transport))
                {
                    throw new Exception($"ConnectWebRtcTransportAsync() | Transport:{connectWebRtcTransportRequest.TransportId} is not exists");
                }

                await transport.ConnectAsync(connectWebRtcTransportRequest.DtlsParameters);
                return true;
            }
        }

        /// <summary>
        /// 拉取
        /// </summary>
        /// <param name="producerPeer"></param>
        /// <param name="roomId"></param>
        /// <param name="sources"></param>
        /// <returns></returns>
        public PeerPullResult Pull(Peer producerPeer, string roomId, IEnumerable<string> sources)
        {
            CheckJoined();
            using (_locker.ReaderLock())
            {
                CheckJoined();

                if (producerPeer.PeerId != PeerId)
                {
                    using (producerPeer._locker.WriterLock())
                    {
                        return PullInternal(producerPeer, roomId, sources);
                    }
                }
                else
                {
                    return PullInternal(producerPeer, roomId, sources);
                }
            }
        }

        private PeerPullResult PullInternal(Peer producerPeer, string roomId, IEnumerable<string> sources)
        {
            var producerProducers = producerPeer._producers.Values.Where(m => sources.Contains(m.Source)).ToArray();

            var existsProducers = new HashSet<Producer>();
            var produceSources = new HashSet<string>();
            foreach (var source in sources)
            {
                foreach (var existsProducer in producerProducers)
                {
                    // 忽略在同一 Room 的重复消费？
                    if (_consumers.Values.Any(m => m.RoomId == roomId && m.Internal.ProducerId == existsProducer.ProducerId))
                    {
                        continue;
                    }
                    existsProducers.Add(existsProducer);
                    continue;
                }

                // 如果 Source 没有对应的 Producer，通知 otherPeer 生产；生产成功后又要通知本 Peer 去对应的 Room 消费。
                if (!producerPeer._pullPaddings.Any(m => m.Source == source))
                {
                    produceSources.Add(source!);
                }
                producerPeer._pullPaddings.Add(new PullPadding
                {
                    RoomId = roomId,
                    ConsumerPeerId = PeerId,
                    Source = source!,
                });
            }

            return new PeerPullResult
            {
                ExistsProducers = existsProducers.ToArray(),
                ProduceSources = produceSources.ToArray(),
            };
        }

        /// <summary>
        /// 生产
        /// </summary>
        /// <param name="produceRequest"></param>
        /// <returns></returns>
        public async Task<PeerProduceResult> ProduceAsync(ProduceRequest produceRequest)
        {
            CheckJoined();
            using (await _locker.WriterLockAsync())
            {
                CheckJoined();

                if (produceRequest.AppData == null || !produceRequest.AppData.TryGetValue("source", out var sourceObj))
                {
                    throw new Exception($"ProduceAsync() | Peer:{PeerId} AppData[\"source\"] is null.");
                }
                var source = sourceObj.ToString();

                var transport = GetProducingTransport();
                if (transport == null)
                {
                    throw new Exception($"ProduceAsync() | Transport:Producing is not exists.");
                }

                if (Sources == null || !Sources.Contains(source))
                {
                    throw new Exception($"ProduceAsync() | Source:\"{ source }\" cannot be produce.");
                }

                var producer = _producers.Values.FirstOrDefault(m => m.Source == source);
                if (producer != null)
                {
                    //throw new Exception($"ProduceAsync() | Source:\"{ source }\" is exists.");
                    _logger.LogWarning($"ProduceAsync() | Source:\"{ source }\" is exists.");
                    return new PeerProduceResult
                    {
                        Producer = producer,
                        PullPaddings = new PullPadding[0],
                    };
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
                producer.ProducerPeer = this;
                producer.Source = source;

                // Store the Producer into the Peer data Object.
                _producers[producer.ProducerId] = producer;

                //producer.On("@close", _ => _producers.Remove(producer.ProducerId));
                //producer.On("transportclose", _ => _producers.Remove(producer.ProducerId));
                producer.Observer.On("close", _ => _producers.TryRemove(producer.ProducerId, out var _));

                var matchedPullPaddings = _pullPaddings.Where(m => m.Source == producer.Source).ToArray();
                foreach (var item in matchedPullPaddings)
                {
                    _pullPaddings.Remove(item);
                }

                return new PeerProduceResult
                {
                    Producer = producer,
                    PullPaddings = matchedPullPaddings,
                };
            }
        }

        /// <summary>
        /// 消费
        /// </summary>
        /// <param name="producer"></param>
        /// <param name="roomId"></param>
        /// <returns></returns>
        public async Task<Consumer> ConsumeAsync(Peer producerPeer, string producerId, string roomId)
        {
            CheckJoined();
            using (await _locker.WriterLockAsync())
            {
                CheckJoined();

                if (producerPeer.PeerId != PeerId)
                {
                    using (await producerPeer._locker.WriterLockAsync())
                    {
                        return await ConsumeInternalAsync(producerPeer, producerId, roomId);
                    }
                }
                else
                {
                    return await ConsumeInternalAsync(producerPeer, producerId, roomId);
                }
            }
        }

        private async Task<Consumer> ConsumeInternalAsync(Peer producerPeer, string producerId, string roomId)
        {
            if (!producerPeer._producers.TryGetValue(producerId, out var producer))
            {
                throw new Exception($"ConsumeAsync() | Peer:{PeerId} - ProducerPeer:{producerPeer.PeerId} has no Producer:{producerId}");
            }
            if (producer.Closed)
            {
                throw new Exception($"ConsumeAsync() | Peer:{PeerId} - ProducerPeer:{producerPeer.PeerId} Producer:{producerId} was closed.");
            }

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

            consumer.RoomId = roomId;
            consumer.ProducerPeer = producerPeer;
            consumer.ConsumerPeer = this;
            consumer.Producer = producer;
            consumer.Source = producer.Source;

            // Store the Consumer into the consumerPeer data Object.
            _consumers[consumer.ConsumerId] = consumer;
            producer.Consumers[consumer.ConsumerId] = consumer;

            //consumer.On("@close", _ => _consumers.Remove(consumer.ConsumerId));
            //consumer.On("producerclose", _ => _consumers.Remove(consumer.ConsumerId));
            //consumer.On("transportclose", _ => _consumers.Remove(consumer.ConsumerId));
            consumer.Observer.On("close", _ =>
            {
                _consumers.TryRemove(consumer.ConsumerId, out var _);
                producer.Consumers.TryRemove(consumer.ConsumerId, out var _);
            });

            return consumer;
        }

        /// <summary>
        /// 停止生产
        /// </summary>
        /// <param name="producerId"></param>
        /// <returns></returns>
        public async Task<bool> CloseProducerAsync(string producerId)
        {
            CheckJoined();
            using (await _locker.WriterLockAsync())
            {
                CheckJoined();

                if (!_producers.TryGetValue(producerId, out var producer))
                {
                    throw new Exception($"CloseProducerAsync() | Peer:{PeerId} has no Producer:{producerId}.");
                }

                producer.Close();
                _producers.TryRemove(producerId, out var _);
                return true;
            }
        }

        /// <summary>
        /// 暂停生产
        /// </summary>
        /// <param name="producerId"></param>
        /// <returns></returns>
        public async Task<bool> PauseProducerAsync(string producerId)
        {
            CheckJoined();
            using (await _locker.ReaderLockAsync())
            {
                CheckJoined();

                if (!_producers.TryGetValue(producerId, out var producer))
                {
                    throw new Exception($"PauseProducerAsync() | Peer:{PeerId} has no Producer:{producerId}.");
                }

                await producer.PauseAsync();
                return true;
            }
        }

        /// <summary>
        /// 恢复生产
        /// </summary>
        /// <param name="producerId"></param>
        /// <returns></returns>
        public async Task<bool> ResumeProducerAsync(string producerId)
        {
            CheckJoined();
            using (await _locker.ReaderLockAsync())
            {
                if (!_producers.TryGetValue(producerId, out var producer))
                {
                    throw new Exception($"ResumeProducerAsync() | Peer:{PeerId} has no Producer:{producerId}.");
                }

                await producer.ResumeAsync();
                return true;
            }
        }

        /// <summary>
        /// 停止消费
        /// </summary>
        /// <param name="consumerId"></param>
        /// <returns></returns>
        public async Task<bool> CloseConsumerAsync(string consumerId)
        {
            CheckJoined();
            using (await _locker.WriterLockAsync())
            {
                CheckJoined();

                if (!_consumers.TryGetValue(consumerId, out var consumer))
                {
                    throw new Exception($"CloseConsumerAsync() | Peer:{PeerId} has no Cmonsumer:{consumerId}.");
                }

                consumer.Close();
                _consumers.TryRemove(consumerId, out var _);
                return true;
            }
        }

        /// <summary>
        /// 暂停消费
        /// </summary>
        /// <param name="consumerId"></param>
        /// <returns></returns>
        public async Task<bool> PauseConsumerAsync(string consumerId)
        {
            CheckJoined();
            using (await _locker.ReaderLockAsync())
            {
                CheckJoined();

                if (!_consumers.TryGetValue(consumerId, out var consumer))
                {
                    throw new Exception($"PauseConsumerAsync() | Peer:{PeerId} has no Consumer:{consumerId}.");
                }

                await consumer.PauseAsync();
                return true;
            }
        }

        /// <summary>
        /// 恢复消费
        /// </summary>
        /// <param name="consumerId"></param>
        /// <returns></returns>
        public async Task<Consumer> ResumeConsumerAsync(string consumerId)
        {
            CheckJoined();
            using (await _locker.ReaderLockAsync())
            {
                CheckJoined();

                if (!_consumers.TryGetValue(consumerId, out var consumer))
                {
                    throw new Exception($"ResumeConsumerAsync() | Peer:{PeerId} has no Consumer:{consumerId}.");
                }

                await consumer.ResumeAsync();
                return consumer;
            }
        }

        /// <summary>
        /// 设置消费建议 Layers
        /// </summary>
        /// <param name="setConsumerPreferedLayersRequest"></param>
        /// <returns></returns>
        public async Task<bool> SetConsumerPreferedLayersAsync(SetConsumerPreferedLayersRequest setConsumerPreferedLayersRequest)
        {
            CheckJoined();
            using (await _locker.ReaderLockAsync())
            {
                CheckJoined();

                if (!_consumers.TryGetValue(setConsumerPreferedLayersRequest.ConsumerId, out var consumer))
                {
                    throw new Exception($"SetConsumerPreferedLayersAsync() | Peer:{PeerId} has no Consumer:{setConsumerPreferedLayersRequest.ConsumerId}.");
                }

                await consumer.SetPreferredLayersAsync(setConsumerPreferedLayersRequest);
                return true;
            }
        }

        /// <summary>
        /// 设置消费 Priority
        /// </summary>
        /// <param name="setConsumerPriorityRequest"></param>
        /// <returns></returns>
        public async Task<bool> SetConsumerPriorityAsync(SetConsumerPriorityRequest setConsumerPriorityRequest)
        {
            CheckJoined();
            using (await _locker.ReaderLockAsync())
            {
                CheckJoined();

                if (!_consumers.TryGetValue(setConsumerPriorityRequest.ConsumerId, out var consumer))
                {
                    throw new Exception($"SetConsumerPriorityAsync() | Peer:{PeerId} has no Consumer:{setConsumerPriorityRequest.ConsumerId}.");
                }

                await consumer.SetPriorityAsync(setConsumerPriorityRequest.Priority);
                return true;
            }
        }

        /// <summary>
        /// 请求关键帧
        /// </summary>
        /// <param name="consumerId"></param>
        /// <returns></returns>
        public async Task<bool> RequestConsumerKeyFrameAsync(string consumerId)
        {
            CheckJoined();
            using (await _locker.ReaderLockAsync())
            {
                CheckJoined();

                if (!_consumers.TryGetValue(consumerId, out var consumer))
                {
                    throw new Exception($"RequestConsumerKeyFrameAsync() | Peer:{PeerId} has no Producer:{consumerId}.");
                }

                await consumer.RequestKeyFrameAsync();
                return true;
            }
        }

        /// <summary>
        /// 获取 Transport 状态
        /// </summary>
        /// <param name="transportId"></param>
        /// <returns></returns>
        public async Task<TransportStat> GetTransportStatsAsync(string transportId)
        {
            CheckJoined();
            using (await _locker.ReaderLockAsync())
            {
                CheckJoined();

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

        /// <summary>
        /// 获取生产者状态
        /// </summary>
        /// <param name="producerId"></param>
        /// <returns></returns>
        public async Task<ProducerStat> GetProducerStatsAsync(string producerId)
        {
            CheckJoined();
            using (await _locker.ReaderLockAsync())
            {
                CheckJoined();

                if (!_producers.TryGetValue(producerId, out var producer))
                {
                    throw new Exception($"GetProducerStatsAsync() | Peer:{PeerId} has no Producer:{producerId}.");
                }

                var status = await producer.GetStatsAsync();
                // TODO: (alby)考虑不进行反序列化
                var data = JsonConvert.DeserializeObject<ProducerStat>(status!);
                return data;
            }
        }

        /// <summary>
        /// 获取消费者状态
        /// </summary>
        /// <param name="consumerId"></param>
        /// <returns></returns>
        public async Task<ConsumerStat> GetConsumerStatsAsync(string consumerId)
        {
            CheckJoined();
            using (await _locker.ReaderLockAsync())
            {
                CheckJoined();

                if (!_consumers.TryGetValue(consumerId, out var consumer))
                {
                    throw new Exception($"GetConsumerStatsAsync() | Peer:{PeerId} has no Consumer:{consumerId}.");
                }

                var status = await consumer.GetStatsAsync();
                // TODO: (alby)考虑不进行反序列化
                var data = JsonConvert.DeserializeObject<ConsumerStat>(status!);
                return data;
            }
        }

        /// <summary>
        /// 重置 Ice
        /// </summary>
        /// <param name="transportId"></param>
        /// <returns></returns>
        public async Task<IceParameters?> RestartIceAsync(string transportId)
        {
            CheckJoined();
            using (await _locker.ReaderLockAsync())
            {
                CheckJoined();

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
        /// 离开房间
        /// </summary>
        /// <param name="roomId"></param>
        public void LeaveRoom(string roomId)
        {
            // 1、Producer 关闭后会触发相应的 Consumer 内部的 `producerclose` 事件
            // 2、拥有 Consumer 的 Peer 能够关闭该 Consumer 并通知客户端。

            using (_locker.WriterLock())
            {
                foreach (var producer in _producers.Values)
                {
                    // Producer 在其他房间被消费，则继续生产。
                    if (!producer.Consumers.Values.Any(m => m.RoomId != roomId))
                    {
                        producer.Close();
                    }
                }
                foreach (var producer in _consumers.Values.Where(m => m.RoomId == roomId).Select(m => m.Producer!))
                {
                    // Producer 在其他房间被消费，或者在房间被其他人消费，则继续生产。
                    if (!producer.Consumers.Values.Any(m => m.RoomId != roomId || m.ConsumerPeer!.PeerId != PeerId))
                    {
                        if (producer.ProducerPeer != null && producer.ProducerPeer.PeerId != PeerId)
                        {
                            using (producer.ProducerPeer._locker.WriterLock())
                            {
                                producer.Close();
                            }
                        }
                        else
                        {
                            producer.Close();
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 离开
        /// </summary>
        public void Leave()
        {
            if (!Joined)
            {
                return;
            }

            using (_locker.WriterLock())
            {
                if (!Joined)
                {
                    return;
                }

                Joined = false;

                foreach (var producer in _producers.Values)
                {
                    producer.Close();
                }

                foreach (var producer in _consumers.Values.Select(m => m.Producer!))
                {
                    // Producer 被其他人消费，则继续生产。
                    if (!producer.Consumers.Values.Any(m => m.ConsumerPeer!.PeerId != PeerId))
                    {
                        if (producer.ProducerPeer != null && producer.ProducerPeer.PeerId != PeerId)
                        {
                            using (producer.ProducerPeer._locker.WriterLock())
                            {
                                producer.Close();
                            }
                        }
                        else
                        {
                            producer.Close();
                        }
                    }
                }

                // Iterate and close all mediasoup Transport associated to this Peer, so all
                // its Producers and Consumers will also be closed.
                _transports.Values.ForEach(m => m.Close());
                _transports.Clear();
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

        private void CheckJoined()
        {
            if (!Joined)
            {
                throw new Exception($"CheckClosed() | Peer:{PeerId} is not joined.");
            }
        }

        #endregion
    }
}
