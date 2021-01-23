﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.Threading;
using Newtonsoft.Json;
using Tubumu.Core.Extensions;
using Tubumu.Mediasoup;
using Tubumu.Mediasoup.Extensions;

namespace TubumuMeeting.Meeting.Server
{
    public partial class Peer : IEquatable<Peer>
    {
        public string PeerId { get; }

        [JsonIgnore]
        public string ConnectionId { get; }

        public string DisplayName { get; }

        public string[] Sources { get; private set; }

        public ConcurrentDictionary<string, object> AppData { get; set; }

        public bool Equals(Peer other)
        {
            if (other == null)
                return false;

            return PeerId == other.PeerId;
        }

        public override bool Equals(object obj)
        {
            if (obj == null)
                return false;

            if (!(obj is Peer tObj))
                return false;
            else
                return PeerId == tObj.PeerId;
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

        private bool _joined;

        private readonly AsyncReaderWriterLock _joinedLock = new AsyncReaderWriterLock();

        private readonly WebRtcTransportSettings _webRtcTransportSettings;

        private readonly PlainTransportSettings _plainTransportSettings;

        private readonly RtpCapabilities _rtpCapabilities;

        private readonly SctpCapabilities? _sctpCapabilities;

        private readonly Dictionary<string, Transport> _transports = new Dictionary<string, Transport>();

        private readonly AsyncReaderWriterLock _transportsLock = new AsyncReaderWriterLock();

        private readonly Dictionary<string, Consumer> _consumers = new Dictionary<string, Consumer>();

        private readonly AsyncReaderWriterLock _consumersLock = new AsyncReaderWriterLock();

        private readonly Dictionary<string, Producer> _producers = new Dictionary<string, Producer>();

        private readonly AsyncReaderWriterLock _producersLock = new AsyncReaderWriterLock();

        private readonly Dictionary<string, DataConsumer> _dataConsumers = new Dictionary<string, DataConsumer>();

        private readonly AsyncReaderWriterLock _dataConsumersLock = new AsyncReaderWriterLock();

        private readonly Dictionary<string, DataProducer> _dataProducers = new Dictionary<string, DataProducer>();

        private readonly AsyncReaderWriterLock _dataProducersLock = new AsyncReaderWriterLock();

        private readonly List<PullPadding> _pullPaddings = new List<PullPadding>();

        private readonly AsyncAutoResetEvent _pullPaddingsLock = new AsyncAutoResetEvent();

        private Room? _room;

        private readonly AsyncReaderWriterLock _roomLock = new AsyncReaderWriterLock();

        public Peer(ILoggerFactory loggerFactory,
            WebRtcTransportSettings webRtcTransportSettings,
            PlainTransportSettings plainTransportSettings,
            RtpCapabilities rtpCapabilities,
            SctpCapabilities? sctpCapabilities,
            string peerId,
            string connectionId,
            string displayName,
            string[]? sources,
            Dictionary<string, object>? appData)
        {
            _loggerFactory = loggerFactory;
            _logger = loggerFactory.CreateLogger<Peer>();
            _webRtcTransportSettings = webRtcTransportSettings;
            _plainTransportSettings = plainTransportSettings;
            _rtpCapabilities = rtpCapabilities;
            _sctpCapabilities = sctpCapabilities;
            PeerId = peerId;
            ConnectionId = connectionId;
            DisplayName = displayName.NullOrWhiteSpaceReplace("Guest" + peerId.ToString().PadLeft(8, '0'));
            Sources = sources ?? Array.Empty<string>();
            AppData = new ConcurrentDictionary<string, object>(appData ?? new Dictionary<string, object>());
            _pullPaddingsLock.Set();
            _joined = true;
        }

        /// <summary>
        /// 创建 WebRtcTransport
        /// </summary>
        /// <param name="createWebRtcTransportRequest"></param>
        /// <returns></returns>
        public async Task<WebRtcTransport> CreateWebRtcTransportAsync(CreateWebRtcTransportRequest createWebRtcTransportRequest)
        {
            if (!(createWebRtcTransportRequest.Consuming ^ createWebRtcTransportRequest.Producing))
            {
                throw new Exception("CreateWebRtcTransportAsync() | Consumer or Producing");
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

            using (await _joinedLock.ReadLockAsync())
            {
                CheckJoined();

                using (await _roomLock.ReadLockAsync())
                {
                    CheckRoom();

                    var transport = await _room!.Router.CreateWebRtcTransportAsync(webRtcTransportOptions);
                    if (transport == null)
                    {
                        throw new Exception("CreateWebRtcTransportAsync() | Router.CreateWebRtcTransport faild");
                    }

                    using (await _transportsLock.WriteLockAsync())
                    {
                        if (createWebRtcTransportRequest.Consuming && HasConsumingTransport())
                        {
                            throw new Exception("CreateWebRtcTransportAsync() | Consuming transport exists");
                        }

                        if (createWebRtcTransportRequest.Producing && HasProducingTransport())
                        {
                            throw new Exception("CreateWebRtcTransportAsync() | Producing transport exists");
                        }

                        // Store the WebRtcTransport into the Peer data Object.
                        _transports[transport.TransportId] = transport;
                    }

                    transport.Observer.On("close", async _ =>
                    {
                        using (await _transportsLock.WriteLockAsync())
                        {
                            _transports.Remove(transport.TransportId);
                        }
                    });

                    // If set, apply max incoming bitrate limit.
                    if (_webRtcTransportSettings.MaximumIncomingBitrate.HasValue && _webRtcTransportSettings.MaximumIncomingBitrate.Value > 0)
                    {
                        // Fire and forget
                        transport.SetMaxIncomingBitrateAsync(_webRtcTransportSettings.MaximumIncomingBitrate.Value).ContinueWithOnFaultedHandleLog(_logger);
                    }

                    return transport;
                }
            }
        }

        /// <summary>
        /// 连接 WebRtcTransport
        /// </summary>
        /// <param name="connectWebRtcTransportRequest"></param>
        /// <returns></returns>
        public async Task<bool> ConnectWebRtcTransportAsync(ConnectWebRtcTransportRequest connectWebRtcTransportRequest)
        {
            using (await _joinedLock.ReadLockAsync())
            {
                CheckJoined();

                using (await _roomLock.ReadLockAsync())
                {
                    CheckRoom();
                    using (await _transportsLock.ReadLockAsync())
                    {
                        if (!_transports.TryGetValue(connectWebRtcTransportRequest.TransportId, out var transport))
                        {
                            throw new Exception($"ConnectWebRtcTransportAsync() | Transport:{connectWebRtcTransportRequest.TransportId} is not exists");
                        }

                        await transport.ConnectAsync(connectWebRtcTransportRequest.DtlsParameters);
                        return true;
                    }
                }
            }
        }

        /// <summary>
        /// 创建 PlainTransport
        /// </summary>
        /// <param name="createPlainTransportRequest"></param>
        /// <returns></returns>
        public async Task<PlainTransport> CreatePlainTransportAsync(CreatePlainTransportRequest createPlainTransportRequest)
        {
            var plainTransportOptions = new PlainTransportOptions
            {
                ListenIp = _plainTransportSettings.ListenIp,
                MaxSctpMessageSize = _plainTransportSettings.MaxSctpMessageSize,
                RtcpMux = createPlainTransportRequest.RtcpMux, // 一般为 false
                Comedia = createPlainTransportRequest.Comedia, // 一般为 true
                AppData = new Dictionary<string, object>
                    {
                        { "Consuming", createPlainTransportRequest.Consuming },
                        { "Producing", createPlainTransportRequest.Producing },
                    },
            };

            using (await _joinedLock.ReadLockAsync())
            {
                CheckJoined();

                using (await _roomLock.ReadLockAsync())
                {
                    CheckRoom();

                    var transport = await _room!.Router.CreatePlainTransportAsync(plainTransportOptions);
                    if (transport == null)
                    {
                        throw new Exception("CreatePlainTransportAsync() | Router.CreatePlainTransport faild");
                    }

                    using (await _transportsLock.WriteLockAsync())
                    {
                        // Store the WebRtcTransport into the Peer data Object.
                        _transports[transport.TransportId] = transport;
                    }

                    transport.Observer.On("close", async _ =>
                    {
                        using (await _transportsLock.WriteLockAsync())
                        {
                            _transports.Remove(transport.TransportId);
                        }
                    });

                    return transport;
                }
            }
        }

        /// <summary>
        /// 拉取
        /// </summary>
        /// <param name="producerPeer"></param>
        /// <param name="roomId"></param>
        /// <param name="sources"></param>
        /// <returns></returns>
        public async Task<PeerPullResult> PullAsync(Peer producerPeer, IEnumerable<string> sources)
        {
            using (await _joinedLock.ReadLockAsync())
            {
                CheckJoined();

                using (await _roomLock.ReadLockAsync())
                {
                    CheckRoom();

                    var consumerRoom = _room!;

                    using (await _consumersLock.ReadLockAsync())
                    {
                        // producerPeer 也有可能是本 Peer
                        if (producerPeer.PeerId == PeerId)
                        {
                            return await PullInternalAsync(producerPeer, sources);
                        }
                        else
                        {
                            using (await producerPeer._joinedLock.ReadLockAsync())
                            {
                                producerPeer.CheckJoined();

                                using (await producerPeer._roomLock.ReadLockAsync())
                                {
                                    producerPeer.CheckRoom();

                                    var producerRoom = producerPeer._room!;

                                    if (producerRoom.RoomId != consumerRoom.RoomId)
                                    {
                                        throw new Exception($"PullAsync() | Peer:{producerPeer.PeerId} and Peer:{PeerId} are not in the same room.");
                                    }

                                    if (sources.Except(producerPeer.Sources).Any())
                                    {
                                        throw new Exception($"PullAsync() | Peer:{producerPeer.PeerId} can't produce some sources in Room.");
                                    }

                                    return await PullInternalAsync(producerPeer, sources);
                                }
                            }
                        }
                    }
                }
            }
        }

        public async Task<PeerPullResult> PullInternalAsync(Peer producerPeer, IEnumerable<string> sources)
        {
            var consumerActiveRoom = _room!;
            var producerActiveRoom = producerPeer._room!;

            var roomId = _room!.RoomId;

            using (await producerPeer._producersLock.ReadLockAsync())
            {
                var producerProducers = producerPeer._producers.Values.Where(m => sources.Contains(m.Source)).ToArray();

                var existsProducers = new HashSet<Producer>();
                var produceSources = new HashSet<string>();
                foreach (var source in sources)
                {
                    foreach (var existsProducer in producerProducers)
                    {
                        // 忽略重复消费
                        if (_consumers.Values.Any(m => m.ProducerId == existsProducer.ProducerId))
                        {
                            continue;
                        }
                        existsProducers.Add(existsProducer);
                        continue;
                    }

                    await producerPeer._pullPaddingsLock.WaitAsync();
                    // 如果 Source 没有对应的 Producer，通知 otherPeer 生产；生产成功后又要通知本 Peer 去对应的 Room 消费。
                    if (!producerPeer._pullPaddings.Any(m => m.Source == source))
                    {
                        produceSources.Add(source!);
                    }
                    if (!producerPeer._pullPaddings.Any(m => m.Source == source && m.RoomId == roomId && m.ConsumerPeerId == PeerId))
                    {
                        producerPeer._pullPaddings.Add(new PullPadding
                        {
                            RoomId = roomId,
                            ConsumerPeerId = PeerId,
                            Source = source!,
                        });
                    }
                    producerPeer._pullPaddingsLock.Set();
                }

                return new PeerPullResult
                {
                    ExistsProducers = existsProducers.ToArray(),
                    ProduceSources = produceSources.ToArray(),
                };
            }
        }

        /// <summary>
        /// 生产
        /// </summary>
        /// <param name="produceRequest"></param>
        /// <returns></returns>
        public async Task<PeerProduceResult> ProduceAsync(ProduceRequest produceRequest)
        {
            if (produceRequest.Source.IsNullOrWhiteSpace())
            {
                throw new Exception($"ProduceAsync() | Peer:{PeerId} AppData[\"source\"] is null or white space.");
            }

            if (Sources == null || !Sources.Contains(produceRequest.Source))
            {
                throw new Exception($"ProduceAsync() | Source:\"{ produceRequest.Source }\" cannot be produce.");
            }

            // Add peerId into appData to later get the associated Peer during
            // the 'loudest' event of the audioLevelObserver.
            produceRequest.AppData["peerId"] = PeerId;

            using (await _joinedLock.ReadLockAsync())
            {
                CheckJoined();

                using (await _roomLock.ReadLockAsync())
                {
                    CheckRoom();

                    using (await _transportsLock.ReadLockAsync())
                    {
                        var transport = GetProducingTransport();
                        if (transport == null)
                        {
                            throw new Exception($"ProduceAsync() | Transport:Producing is not exists.");
                        }

                        using (await _producersLock.WriteLockAsync())
                        {
                            var producer = _producers.Values.FirstOrDefault(m => m.Source == produceRequest.Source);
                            if (producer != null)
                            {
                                //throw new Exception($"ProduceAsync() | Source:\"{ produceRequest.Source }\" is exists.");
                                _logger.LogWarning($"ProduceAsync() | Source:\"{ produceRequest.Source }\" is exists.");
                                return new PeerProduceResult
                                {
                                    Producer = producer,
                                    PullPaddings = Array.Empty<PullPadding>(),
                                };
                            }

                            producer = await transport.ProduceAsync(new ProducerOptions
                            {
                                Kind = produceRequest.Kind,
                                RtpParameters = produceRequest.RtpParameters,
                                AppData = produceRequest.AppData,
                            });

                            // Store producer source
                            producer.Source = produceRequest.Source;

                            //producer.On("@close", _ => ...);
                            //producer.On("transportclose", _ => ...);
                            producer.Observer.On("close", async _ =>
                            {
                                using (await _producersLock.WriteLockAsync())
                                {
                                    _producers.Remove(producer.ProducerId);

                                    await _pullPaddingsLock.WaitAsync();
                                    _pullPaddings.Clear();
                                    _pullPaddingsLock.Set();
                                }
                            });

                            await _pullPaddingsLock.WaitAsync();
                            var matchedPullPaddings = _pullPaddings.Where(m => m.Source == producer.Source).ToArray();
                            foreach (var item in matchedPullPaddings)
                            {
                                _pullPaddings.Remove(item);
                            }
                            _pullPaddingsLock.Set();

                            // Store the Producer into the Peer data Object.
                            _producers[producer.ProducerId] = producer;

                            return new PeerProduceResult
                            {
                                Producer = producer,
                                PullPaddings = matchedPullPaddings,
                            };
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 消费
        /// </summary>
        /// <param name="producer"></param>
        /// <param name="roomId"></param>
        /// <returns></returns>
        public async Task<Consumer> ConsumeAsync(Peer producerPeer, string producerId)
        {
            using (await _joinedLock.ReadLockAsync())
            {
                CheckJoined();

                using (await _roomLock.ReadLockAsync())
                {
                    CheckRoom();

                    using (await _transportsLock.ReadLockAsync())
                    {
                        var transport = GetConsumingTransport();

                        // This should not happen.
                        if (transport == null)
                        {
                            throw new Exception($"ConsumeAsync() | Peer:{PeerId} Transport for consuming not found.");
                        }

                        using (await _consumersLock.WriteLockAsync())
                        {
                            using (await producerPeer._producersLock.ReadLockAsync())
                            {
                                if (!producerPeer._producers.TryGetValue(producerId, out var producer))
                                {
                                    throw new Exception($"ConsumeAsync() | Peer:{PeerId} - ProducerPeer:{producerPeer.PeerId} has no Producer:{producerId}");
                                }

                                if (_rtpCapabilities == null || !_room!.Router.CanConsume(producer.ProducerId, _rtpCapabilities))
                                {
                                    throw new Exception($"ConsumeAsync() | Peer:{PeerId} Can not consume.");
                                }

                                // Create the Consumer in paused mode.
                                var consumer = await transport.ConsumeAsync(new ConsumerOptions
                                {
                                    ProducerId = producer.ProducerId,
                                    RtpCapabilities = _rtpCapabilities,
                                    Paused = true // Or: producer.Kind == MediaKind.Video
                                });

                                consumer.Source = producer.Source;

                                //consumer.On("@close", _ => ...);
                                //consumer.On("producerclose", _ => ...);
                                //consumer.On("transportclose", _ => ...);
                                consumer.Observer.On("close", async _ =>
                                {
                                    using (await _consumersLock.WriteLockAsync())
                                    {
                                        _consumers.Remove(consumer.ConsumerId);
                                        producer.RemoveConsumer(consumer.ConsumerId);
                                    }
                                });

                                // Store the Consumer into the consumerPeer data Object.
                                _consumers[consumer.ConsumerId] = consumer;

                                producer.AddConsumer(consumer);

                                return consumer;
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 停止生产
        /// </summary>
        /// <param name="producerId"></param>
        /// <returns></returns>
        public async Task<bool> CloseProducerAsync(string producerId)
        {
            using (await _joinedLock.ReadLockAsync())
            {
                CheckJoined();

                using (await _roomLock.ReadLockAsync())
                {
                    CheckRoom();

                    using (await _producersLock.ReadLockAsync())
                    {
                        if (!_producers.TryGetValue(producerId, out var producer))
                        {
                            throw new Exception($"CloseProducerAsync() | Peer:{PeerId} has no Producer:{producerId}.");
                        }

                        producer.Close();
                        return true;
                    }
                }
            }
        }

        /// <summary>
        /// 暂停生产
        /// </summary>
        /// <param name="producerId"></param>
        /// <returns></returns>
        public async Task<bool> PauseProducerAsync(string producerId)
        {
            using (await _joinedLock.ReadLockAsync())
            {
                CheckJoined();

                using (await _roomLock.ReadLockAsync())
                {
                    CheckRoom();

                    using (await _producersLock.ReadLockAsync())
                    {
                        if (!_producers.TryGetValue(producerId, out var producer))
                        {
                            throw new Exception($"PauseProducerAsync() | Peer:{PeerId} has no Producer:{producerId}.");
                        }

                        await producer.PauseAsync();
                        return true;
                    }
                }
            }
        }

        /// <summary>
        /// 恢复生产
        /// </summary>
        /// <param name="producerId"></param>
        /// <returns></returns>
        public async Task<bool> ResumeProducerAsync(string producerId)
        {
            using (await _joinedLock.ReadLockAsync())
            {
                CheckJoined();

                using (await _roomLock.ReadLockAsync())
                {
                    CheckRoom();

                    using (await _producersLock.ReadLockAsync())
                    {
                        if (!_producers.TryGetValue(producerId, out var producer))
                        {
                            throw new Exception($"ResumeProducerAsync() | Peer:{PeerId} has no Producer:{producerId}.");
                        }

                        await producer.ResumeAsync();
                        return true;
                    }
                }
            }
        }

        /// <summary>
        /// 停止消费
        /// </summary>
        /// <param name="consumerId"></param>
        /// <returns></returns>
        public async Task<bool> CloseConsumerAsync(string consumerId)
        {
            using (await _joinedLock.ReadLockAsync())
            {
                CheckJoined();

                using (await _roomLock.ReadLockAsync())
                {
                    CheckRoom();

                    using (await _consumersLock.ReadLockAsync())
                    {
                        if (!_consumers.TryGetValue(consumerId, out var consumer))
                        {
                            throw new Exception($"CloseConsumerAsync() | Peer:{PeerId} has no Cmonsumer:{consumerId}.");
                        }

                        await consumer.CloseAsync();
                        return true;
                    }
                }
            }
        }

        /// <summary>
        /// 暂停消费
        /// </summary>
        /// <param name="consumerId"></param>
        /// <returns></returns>
        public async Task<bool> PauseConsumerAsync(string consumerId)
        {
            using (await _joinedLock.ReadLockAsync())
            {
                CheckJoined();

                using (await _roomLock.ReadLockAsync())
                {
                    CheckRoom();

                    using (await _consumersLock.ReadLockAsync())
                    {
                        if (!_consumers.TryGetValue(consumerId, out var consumer))
                        {
                            throw new Exception($"PauseConsumerAsync() | Peer:{PeerId} has no Consumer:{consumerId}.");
                        }

                        await consumer.PauseAsync();
                        return true;
                    }
                }
            }
        }

        /// <summary>
        /// 恢复消费
        /// </summary>
        /// <param name="consumerId"></param>
        /// <returns></returns>
        public async Task<Consumer> ResumeConsumerAsync(string consumerId)
        {
            using (await _joinedLock.ReadLockAsync())
            {
                CheckJoined();

                using (await _roomLock.ReadLockAsync())
                {
                    CheckRoom();

                    using (await _consumersLock.ReadLockAsync())
                    {
                        if (!_consumers.TryGetValue(consumerId, out var consumer))
                        {
                            throw new Exception($"ResumeConsumerAsync() | Peer:{PeerId} has no Consumer:{consumerId}.");
                        }

                        await consumer.ResumeAsync();
                        return consumer;
                    }
                }
            }
        }

        /// <summary>
        /// 设置消费建议 Layers
        /// </summary>
        /// <param name="setConsumerPreferedLayersRequest"></param>
        /// <returns></returns>
        public async Task<bool> SetConsumerPreferedLayersAsync(SetConsumerPreferedLayersRequest setConsumerPreferedLayersRequest)
        {
            using (await _joinedLock.ReadLockAsync())
            {
                CheckJoined();

                using (await _roomLock.ReadLockAsync())
                {
                    CheckRoom();

                    using (await _consumersLock.ReadLockAsync())
                    {
                        if (!_consumers.TryGetValue(setConsumerPreferedLayersRequest.ConsumerId, out var consumer))
                        {
                            throw new Exception($"SetConsumerPreferedLayersAsync() | Peer:{PeerId} has no Consumer:{setConsumerPreferedLayersRequest.ConsumerId}.");
                        }

                        await consumer.SetPreferredLayersAsync(setConsumerPreferedLayersRequest);
                        return true;
                    }
                }
            }
        }

        /// <summary>
        /// 设置消费 Priority
        /// </summary>
        /// <param name="setConsumerPriorityRequest"></param>
        /// <returns></returns>
        public async Task<bool> SetConsumerPriorityAsync(SetConsumerPriorityRequest setConsumerPriorityRequest)
        {
            using (await _joinedLock.ReadLockAsync())
            {
                CheckJoined();

                using (await _roomLock.ReadLockAsync())
                {
                    CheckRoom();

                    using (await _consumersLock.ReadLockAsync())
                    {
                        if (!_consumers.TryGetValue(setConsumerPriorityRequest.ConsumerId, out var consumer))
                        {
                            throw new Exception($"SetConsumerPriorityAsync() | Peer:{PeerId} has no Consumer:{setConsumerPriorityRequest.ConsumerId}.");
                        }

                        await consumer.SetPriorityAsync(setConsumerPriorityRequest.Priority);
                        return true;
                    }
                }
            }
        }

        /// <summary>
        /// 请求关键帧
        /// </summary>
        /// <param name="consumerId"></param>
        /// <returns></returns>
        public async Task<bool> RequestConsumerKeyFrameAsync(string consumerId)
        {
            using (await _joinedLock.ReadLockAsync())
            {
                CheckJoined();

                using (await _roomLock.ReadLockAsync())
                {
                    CheckRoom();

                    using (await _consumersLock.ReadLockAsync())
                    {
                        if (!_consumers.TryGetValue(consumerId, out var consumer))
                        {
                            throw new Exception($"RequestConsumerKeyFrameAsync() | Peer:{PeerId} has no Producer:{consumerId}.");
                        }

                        await consumer.RequestKeyFrameAsync();
                        return true;
                    }
                }
            }
        }

        /// <summary>
        /// 获取 Transport 状态
        /// </summary>
        /// <param name="transportId"></param>
        /// <returns></returns>
        public async Task<TransportStat> GetTransportStatsAsync(string transportId)
        {
            using (await _joinedLock.ReadLockAsync())
            {
                CheckJoined();

                using (await _roomLock.ReadLockAsync())
                {
                    CheckRoom();

                    using (await _transportsLock.ReadLockAsync())
                    {
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
            }
        }

        /// <summary>
        /// 获取生产者状态
        /// </summary>
        /// <param name="producerId"></param>
        /// <returns></returns>
        public async Task<ProducerStat> GetProducerStatsAsync(string producerId)
        {
            using (await _joinedLock.ReadLockAsync())
            {
                CheckJoined();

                using (await _roomLock.ReadLockAsync())
                {
                    CheckRoom();

                    using (await _producersLock.ReadLockAsync())
                    {
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
            }
        }

        /// <summary>
        /// 获取消费者状态
        /// </summary>
        /// <param name="consumerId"></param>
        /// <returns></returns>
        public async Task<ConsumerStat> GetConsumerStatsAsync(string consumerId)
        {
            using (await _joinedLock.ReadLockAsync())
            {
                CheckJoined();

                using (await _roomLock.ReadLockAsync())
                {
                    CheckRoom();

                    using (await _consumersLock.ReadLockAsync())
                    {
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
            }
        }

        /// <summary>
        /// 重置 Ice
        /// </summary>
        /// <param name="transportId"></param>
        /// <returns></returns>
        public async Task<IceParameters?> RestartIceAsync(string transportId)
        {
            using (await _joinedLock.ReadLockAsync())
            {
                CheckJoined();

                using (await _roomLock.ReadLockAsync())
                {
                    CheckRoom();

                    using (await _transportsLock.ReadLockAsync())
                    {
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
            }
        }

        public async Task<JoinRoomResult> JoinRoomAsync(Room room)
        {
            using (await _joinedLock.ReadLockAsync())
            {
                CheckJoined();

                using (await _roomLock.WriteLockAsync())
                {
                    if (_room != null)
                    {
                        throw new Exception($"JoinRoomAsync() | Peer:{PeerId} was in Room:{room.RoomId} already.");
                    }

                    _room = room;

                    return await _room.PeerJoinAsync(this);
                }
            }
        }

        /// <summary>
        /// 离开房间
        /// </summary>
        /// <param name="roomId"></param>
        public async Task<LeaveRoomResult> LeaveRoomAsync()
        {
            using (await _joinedLock.ReadLockAsync())
            {
                CheckJoined();

                using (await _roomLock.WriteLockAsync())
                {
                    CheckRoom();

                    // !!!注意：这里要用 WriteLockAsync，否则 Transport 的 close 事件将报异常：不可升级的读取锁由调用方持有，无法升级。
                    using (await _transportsLock.WriteLockAsync())
                    {
                        // Iterate and close all mediasoup Transport associated to this Peer, so all
                        // its Producers and Consumers will also be closed.
                        foreach (var transport in _transports.Values)
                        {
                            transport.CloseAsync().ContinueWithOnFaultedHandleLog(_logger);
                        }
                    }

                    var result = await _room!.PeerLeaveAsync(PeerId);
                    _room = null;
                    return result;
                }
            }
        }

        /// <summary>
        /// 离开
        /// </summary>
        public async Task<LeaveResult> LeaveAsync()
        {
            var leaveResult = new LeaveResult
            {
                SelfPeer = this,
                OtherPeerIds = Array.Empty<string>(),
            };

            if (!_joined)
            {
                return leaveResult;
            }

            using (await _joinedLock.WriteLockAsync())
            {
                if (!_joined)
                {
                    return leaveResult;
                }

                _joined = false;

                using (await _roomLock.WriteLockAsync())
                {
                    // !!!注意：这里要用 WriteLockAsync，否则 Transport 的 close 事件将报异常：不可升级的读取锁由调用方持有，无法升级。
                    using (await _transportsLock.WriteLockAsync())
                    {
                        // Iterate and close all mediasoup Transport associated to this Peer, so all
                        // its Producers and Consumers will also be closed.
                        foreach (var transport in _transports.Values)
                        {
                            transport.CloseAsync().ContinueWithOnFaultedHandleLog(_logger);
                        }
                    }

                    if (_room != null)
                    {
                        var leaveRoomResult = await _room.PeerLeaveAsync(PeerId);
                        leaveResult.OtherPeerIds = leaveRoomResult.OtherPeerIds;
                        _room = null;
                    }

                    return leaveResult;
                }
            }
        }

        /// <summary>
        /// 设置 AppData
        /// </summary>
        /// <param name="peerId"></param>
        /// <param name="connectionId"></param>
        /// <param name="setPeerAppDataRequest"></param>
        /// <returns></returns>
        public async Task<PeerAppDataResult> SetPeerAppDataAsync(SetPeerAppDataRequest setPeerAppDataRequest)
        {
            var peerAppDataResult = new PeerAppDataResult
            {
                SelfPeerId = PeerId,
                OtherPeerIds = Array.Empty<string>(),
            };

            using (await _joinedLock.ReadLockAsync())
            {
                CheckJoined();

                foreach (var item in setPeerAppDataRequest.PeerAppData)
                {
                    AppData.AddOrUpdate(item.Key, item.Value, (oldKey, oldValue) => item.Value);
                }

                peerAppDataResult.AppData = AppData.ToDictionary(x => x.Key, x => x.Value);

                using (await _roomLock.ReadLockAsync())
                {
                    var allPeerIds = await GetOtherPeerIdsInteralAsync();
                    peerAppDataResult.OtherPeerIds = allPeerIds.Where(m => m != PeerId).ToArray();
                    return peerAppDataResult;
                }
            }
        }

        /// <summary>
        /// 移除 AppData
        /// </summary>
        /// <param name="unsetPeerAppDataRequest"></param>
        /// <returns></returns>
        public async Task<PeerAppDataResult> UnsetPeerAppDataAsync(UnsetPeerAppDataRequest unsetPeerAppDataRequest)
        {
            var peerAppDataResult = new PeerAppDataResult
            {
                SelfPeerId = PeerId,
                OtherPeerIds = Array.Empty<string>(),
            };

            using (await _joinedLock.ReadLockAsync())
            {
                CheckJoined();

                foreach (var item in unsetPeerAppDataRequest.Keys)
                {
                    AppData.TryRemove(item, out var _);
                }

                peerAppDataResult.AppData = AppData.ToDictionary(x => x.Key, x => x.Value);

                using (await _roomLock.ReadLockAsync())
                {
                    var allPeerIds = await GetOtherPeerIdsInteralAsync();
                    peerAppDataResult.OtherPeerIds = allPeerIds.Where(m => m != PeerId).ToArray();
                    return peerAppDataResult;
                }
            }
        }

        /// <summary>
        /// 清空 AppData
        /// </summary>
        /// <param name="unsetPeerAppDataRequest"></param>
        /// <returns></returns>
        public async Task<PeerAppDataResult> ClearPeerAppDataAsync()
        {
            var peerAppDataResult = new PeerAppDataResult
            {
                SelfPeerId = PeerId,
                OtherPeerIds = Array.Empty<string>(),
                AppData = new Dictionary<string, object>(),
            };

            using (await _joinedLock.ReadLockAsync())
            {
                CheckJoined();

                AppData.Clear();

                using (await _roomLock.ReadLockAsync())
                {
                    var allPeerIds = await GetOtherPeerIdsInteralAsync();
                    peerAppDataResult.OtherPeerIds = allPeerIds.Where(m => m != PeerId).ToArray();
                    return peerAppDataResult;
                }
            }
        }

        /// <summary>
        /// 获取 Room 内其他 Peer 的 Id
        /// </summary>
        public async Task<string[]> GetOtherPeerIdsAsync()
        {
            using (await _joinedLock.ReadLockAsync())
            {
                CheckJoined();

                using (await _roomLock.ReadLockAsync())
                {
                    CheckRoom();

                    return await _room!.GetPeerIdsAsync();
                }
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
            if (!_joined)
            {
                throw new Exception($"CheckClosed() | Peer:{PeerId} is not joined.");
            }
        }

        private void CheckRoom()
        {
            if (_room == null)
            {
                throw new Exception($"CheckActiveRoom() | Peer:{PeerId} is not in any room.");
            }
        }

        public async Task<string[]> GetOtherPeerIdsInteralAsync()
        {
            return _room != null ? await _room!.GetPeerIdsAsync() : Array.Empty<string>();
        }

        #endregion Private Methods
    }
}
