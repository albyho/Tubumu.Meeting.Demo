using Newtonsoft.Json;

namespace TubumuMeeting.Mediasoup
{
    public class RequestMessage
    {
        public int Id { get; set; }

        public string Method { get; set; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public object? Internal { get; set; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public object? Data { get; set; }
    }

    public class ResponseMessage
    {
        public int? Id { get; set; }

        public string? TargetId { get; set; }

        public string? Event { get; set; }

        public bool? Accepted { get; set; }

        public string? Error { get; set; }

        public string? Reason { get; set; }

        public object? Data { get; set; }
    }

    public class TransportProduceResponseData
    {
        public ProducerType Type { get; set; }
    }

    public class TransportConsumeResponseData
    {
        public bool Paused { get; set; }

        public bool ProducerPaused { get; set; }

        public ConsumerScore Score { get; set; }

        public ConsumerLayers PreferredLayers { get; set; }
    }

    public class TransportDataProduceResponseData
    {
        /// <summary>
        /// DataProducer id (just for Router.pipeToRouter() method).
        /// </summary>
        public string? Id { get; set; }

        /// <summary>
        /// SCTP parameters defining how the endpoint is sending the data.
        /// </summary>
        public SctpStreamParameters SctpStreamParameters { get; set; }

        /// <summary>
        /// A label which can be used to distinguish this DataChannel from others.
        /// </summary>
        public string? Label { get; set; }

        /// <summary>
        /// Name of the sub-protocol used by this DataChannel.
        /// </summary>
        public string? Protocol { get; set; }
    }

    public class TransportDataConsumeResponseData
    {
        /// <summary>
        /// DataConsumer id (just for Router.pipeToRouter() method).
        /// </summary>
        public string? Id { get; set; }

        /// <summary>
        /// SCTP parameters defining how the endpoint is sending the data.
        /// </summary>
        public SctpStreamParameters SctpStreamParameters { get; set; }

        /// <summary>
        /// A label which can be used to distinguish this DataChannel from others.
        /// </summary>
        public string? Label { get; set; }

        /// <summary>
        /// Name of the sub-protocol used by this DataChannel.
        /// </summary>
        public string? Protocol { get; set; }
    }

    public class AudioLevelObserverVolumeNotificationData
    {
        /// <summary>
        /// The audio producer id.
        /// </summary>
        public string ProducerId { get; set; }

        /// <summary>
        /// The average volume (in dBvo from -127 to 0) of the audio producer in the
        /// last interval.
        /// </summary>
        public int Volume { get; set; }
    }
}
