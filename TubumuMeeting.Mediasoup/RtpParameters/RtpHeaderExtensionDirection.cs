using Tubumu.Core.Extensions;

namespace TubumuMeeting.Mediasoup
{
    /// <summary>
    /// Direction of RTP header extension.
    /// </summary>
    public enum RtpHeaderExtensionDirection
    {
        [EnumStringValue("sendrecv")]
        SendReceive,

        [EnumStringValue("sendonly")]
        SendOnly,

        [EnumStringValue("recvonly")]
        ReceiveOnly,

        [EnumStringValue("inactive")]
        Inactive
    }
}
