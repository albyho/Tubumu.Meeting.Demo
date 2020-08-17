namespace TubumuMeeting.Meeting.Server
{
    public class PeerAppDataResult
    {
        public Peer SelfPeer { get; set; }

        public string[] OtherPeerIds { get; set; }
    }
}
