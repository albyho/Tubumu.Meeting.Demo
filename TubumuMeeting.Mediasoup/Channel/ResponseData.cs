using Newtonsoft.Json;

namespace TubumuMeeting.Mediasoup
{
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

    public class TransportConnectResponseData
    {
        public DtlsRole? DtlsLocalRole { get; set; }
    }

    public class TransportRestartIceResponseData
    {
        public IceParameters IceParameters { get; set; }
    }
}
