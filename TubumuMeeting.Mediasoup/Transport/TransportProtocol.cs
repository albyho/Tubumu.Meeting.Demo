using Tubumu.Core.Extensions;

namespace TubumuMeeting.Mediasoup
{
    /// <summary>
    /// Transport protocol.
    /// </summary>
    public enum TransportProtocol
    {
        [EnumStringValue("udp")]
        UDP,

        [EnumStringValue("tcp")]
        TCP
    }
}
