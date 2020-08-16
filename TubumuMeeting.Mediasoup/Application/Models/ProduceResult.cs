namespace TubumuMeeting.Mediasoup
{
    public class ProduceResult
    {
        /// <summary>
        /// ProducerPeer
        /// </summary>
        public Peer ProducerPeer { get; set; }

        /// <summary>
        /// Producer
        /// </summary>
        public Producer Producer { get; set; }

        /// <summary>
        /// PullPaddingConsumerPeerWithRoomIds
        /// <para>因为消费 Peer 不一定全是在本次生产的对应的 Room 里发起的，故需要带上 RoomId 。</para>
        /// </summary>
        public ConsumerPeerWithRoomId[] PullPaddingConsumerPeerWithRoomIds { get; set; }
    }

    public class ConsumerPeerWithRoomId
    {
        public Peer ConsumerPeer { get; set; }

        public string RoomId { get; set; }
    }
}
