using Tubumu.Core.Extensions;

namespace TubumuMeeting.Mediasoup
{
    public enum MethodId
    {
        [EnumStringValue("worker.dump")]
        WORKER_DUMP = 1,

        [EnumStringValue("worker.getResourceUsage")]
        WORKER_GET_RESOURCE_USAGE,

        [EnumStringValue("worker.updateSettings")]
        WORKER_UPDATE_SETTINGS,

        [EnumStringValue("worker.createRouter")]
        WORKER_CREATE_ROUTER,

        [EnumStringValue("router.close")]
        ROUTER_CLOSE,

        [EnumStringValue("router.dump")]
        ROUTER_DUMP,

        [EnumStringValue("router.createWebRtcTransport")]
        ROUTER_CREATE_WEBRTC_TRANSPORT,

        [EnumStringValue("router.createPlainTransport")]
        ROUTER_CREATE_PLAIN_TRANSPORT,

        [EnumStringValue("router.createPipeTransport")]
        ROUTER_CREATE_PIPE_TRANSPORT,

        [EnumStringValue("router.createDirectTransport")]
        ROUTER_CREATE_DIRECT_TRANSPORT,

        [EnumStringValue("router.createAudioLevelObserver")]
        ROUTER_CREATE_AUDIO_LEVEL_OBSERVER,

        [EnumStringValue("transport.close")]
        TRANSPORT_CLOSE,

        [EnumStringValue("transport.dump")]
        TRANSPORT_DUMP,

        [EnumStringValue("transport.getStats")]
        TRANSPORT_GET_STATS,

        [EnumStringValue("transport.connect")]
        TRANSPORT_CONNECT,

        [EnumStringValue("transport.setMaxIncomingBitrate")]
        TRANSPORT_SET_MAX_INCOMING_BITRATE,

        [EnumStringValue("transport.restartIce")]
        TRANSPORT_RESTART_ICE,

        [EnumStringValue("transport.produce")]
        TRANSPORT_PRODUCE,

        [EnumStringValue("transport.consume")]
        TRANSPORT_CONSUME,

        [EnumStringValue("transport.produceData")]
        TRANSPORT_PRODUCE_DATA,

        [EnumStringValue("transport.consumeData")]
        TRANSPORT_CONSUME_DATA,

        [EnumStringValue("transport.enableTraceEvent")]
        TRANSPORT_ENABLE_TRACE_EVENT,

        [EnumStringValue("producer.close")]
        PRODUCER_CLOSE,

        [EnumStringValue("producer.dump")]
        PRODUCER_DUMP,

        [EnumStringValue("producer.getStats")]
        PRODUCER_GET_STATS,

        [EnumStringValue("producer.pause")]
        PRODUCER_PAUSE,

        [EnumStringValue("producer.resume")]
        PRODUCER_RESUME,

        [EnumStringValue("producer.enableTraceEvent")]
        PRODUCER_ENABLE_TRACE_EVENT,

        [EnumStringValue("consumer.close")]
        CONSUMER_CLOSE,

        [EnumStringValue("consumer.dump")]
        CONSUMER_DUMP,

        [EnumStringValue("consumer.getStats")]
        CONSUMER_GET_STATS,

        [EnumStringValue("consumer.pause")]
        CONSUMER_PAUSE,

        [EnumStringValue("consumer.resume")]
        CONSUMER_RESUME,

        [EnumStringValue("consumer.setPreferredLayers")]
        CONSUMER_SET_PREFERRED_LAYERS,

        [EnumStringValue("consumer.setPriority")]
        CONSUMER_SET_PRIORITY,

        [EnumStringValue("consumer.requestKeyFrame")]
        CONSUMER_REQUEST_KEY_FRAME,

        [EnumStringValue("consumer.enableTraceEvent")]
        CONSUMER_ENABLE_TRACE_EVENT,

        [EnumStringValue("dataProducer.close")]
        DATA_PRODUCER_CLOSE,

        [EnumStringValue("dataProducer.dump")]
        DATA_PRODUCER_DUMP,

        [EnumStringValue("dataProducer.getStats")]
        DATA_PRODUCER_GET_STATS,

        [EnumStringValue("dataConsumer.dump")]
        DATA_CONSUMER_CLOSE,

        [EnumStringValue("dataConsumer.close")]
        DATA_CONSUMER_DUMP,

        [EnumStringValue("dataConsumer.getStats")]
        DATA_CONSUMER_GET_STATS,

        [EnumStringValue("dataConsumer.getBufferedAmount")]
        DATA_CONSUMER_GET_BUFFERED_AMOUNT,

        [EnumStringValue("dataConsumer.setBufferedAmountLowThreshold")]
        DATA_CONSUMER_SET_BUFFERED_AMOUNT_LOW_THRESHOLD,

        [EnumStringValue("rtpObserver.close")]
        RTP_OBSERVER_CLOSE,

        [EnumStringValue("rtpObserver.pause")]
        RTP_OBSERVER_PAUSE,

        [EnumStringValue("rtpObserver.resume")]
        RTP_OBSERVER_RESUME,

        [EnumStringValue("rtpObserver.addProducer")]
        RTP_OBSERVER_ADD_PRODUCER,

        [EnumStringValue("rtpObserver.removeProducer")]
        RTP_OBSERVER_REMOVE_PRODUCER
    }
}
