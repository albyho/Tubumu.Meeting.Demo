using System;
using Tubumu.Core.Extensions;

namespace TubumuMeeting.Mediasoup
{
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
