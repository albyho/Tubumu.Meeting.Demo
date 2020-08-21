using System.Collections.Generic;

namespace TubumuMeeting.Mediasoup
{
    public class SetPeerAppDataRequest
    {
        public Dictionary<string, object> PeerAppData { get; set; }
    }

    public class UnsetPeerAppDataRequest
    {
        public string[] Keys { get; set; }
    }
}
