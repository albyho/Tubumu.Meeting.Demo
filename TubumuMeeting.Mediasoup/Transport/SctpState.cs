using Tubumu.Core.Extensions;

namespace TubumuMeeting.Mediasoup
{
    /// <summary>
    /// SCTP state.
    /// </summary>
    public enum SctpState
    {
        [EnumStringValue("new")]
        New,

        [EnumStringValue("connecting")]
        Connecting,

        [EnumStringValue("connected")]
        Connected,

        [EnumStringValue("failed")]
        Failed,

        [EnumStringValue("closed")]
        Closed
    }
}
