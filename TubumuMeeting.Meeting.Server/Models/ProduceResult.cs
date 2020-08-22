using System;
using TubumuMeeting.Mediasoup;

namespace TubumuMeeting.Meeting.Server
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

    public class ConsumerPeerWithRoomId : IEquatable<ConsumerPeerWithRoomId>
    {
        public Peer ConsumerPeer { get; set; }

        public string RoomId { get; set; }

        public bool Equals(ConsumerPeerWithRoomId other)
        {
            if (other == null)
                return false;

            return RoomId == other.RoomId && ConsumerPeer.PeerId == other.ConsumerPeer.PeerId;
        }

        public override bool Equals(Object obj)
        {
            if (obj == null)
                return false;

            var tObj = obj as ConsumerPeerWithRoomId;
            if (tObj == null)
                return false;
            else
                return RoomId == tObj.RoomId && ConsumerPeer.PeerId == tObj.ConsumerPeer.PeerId;
        }

        public override int GetHashCode()
        {
            return RoomId.GetHashCode() ^ ConsumerPeer.PeerId.GetHashCode();
        }
    }
}
