namespace TubumuMeeting.Mediasoup
{
    public class DataConsumerOptions
    {
        /// <summary>
        /// The id of the DataProducer to consume.
        /// </summary>
        public string DataProducerId { get; set; }

        /// <summary>
        /// Custom application data.
        /// </summary>
        public object? AppData { get; set; }
    }
}
