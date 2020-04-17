namespace TubumuMeeting.Mediasoup
{
    public class ConsumerScore
    {
        /// <summary>
        /// The score of the RTP stream of the consumer.
        /// </summary>
        public int Score { get; set; }

        /// <summary>
        /// The score of the currently selected RTP stream of the producer.
        /// </summary>
        public int ProducerScore { get; set; }
    }
}
