using Tubumu.Core.Extensions;

namespace TubumuMeeting.Mediasoup
{
    public enum DtlsState
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
