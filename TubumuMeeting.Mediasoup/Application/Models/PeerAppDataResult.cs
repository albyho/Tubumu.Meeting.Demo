using System.Collections.Generic;

namespace TubumuMeeting.Mediasoup
{
    public class PeerAppDataResult
    {
        public Peer SelfPeer { get; set; }

        public string[] OtherPeerIds { get; set; }
    }
}
