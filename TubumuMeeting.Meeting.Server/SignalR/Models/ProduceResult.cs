using TubumuMeeting.Mediasoup;

namespace TubumuMeeting.Meeting.Server
{
    public class ProduceResult
    {
        /// <summary>
        /// SelfPeer
        /// </summary>
        public Peer SelfPeer { get; set; }
        
        /// <summary>
        /// Producer
        /// </summary>
        public Producer Producer { get; set; }

        /// <summary>
        /// PullPaddingPeerRoomIds
        /// <para>因为消费 Peer 不一定全是在本次生产的对应的 Room 里发起的，故需要带上 RoomId 。</para>
        /// </summary>
        public PeerWithRoomId[] PullPaddingPeerRoomIds { get; set; }
    }

    public class PeerWithRoomId
    {
        public Peer Peer { get; set; }

        public string RoomId { get; set; }
    }
}
