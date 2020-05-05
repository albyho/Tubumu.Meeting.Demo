using Tubumu.Core.Extensions;

namespace TubumuMeeting.Mediasoup
{
    public enum IceState
    {
        [EnumStringValue("new")]
        New,

        [EnumStringValue("connected")]
        Connected,

        [EnumStringValue("completed")]
        Completed,

        [EnumStringValue("disconnected")]
        Disconnected,

        [EnumStringValue("closed")]
        Closed
    }
}
