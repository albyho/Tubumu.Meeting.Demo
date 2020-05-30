using System;
using Tubumu.Core.Extensions;

namespace TubumuMeeting.Mediasoup
{
    public enum WorkerLogTag
    {
        [EnumStringValue("info")]
        Info,

        [EnumStringValue("ice")]
        Ice,

        [EnumStringValue("dtls")]
        Dtls,

        [EnumStringValue("rtp")]
        Rtp,

        [EnumStringValue("srtp")]
        Srtp,

        [EnumStringValue("rtcp")]
        Rtcp,

        [EnumStringValue("rtx")]
        Rtx,

        [EnumStringValue("bwe")]
        Bwe,

        [EnumStringValue("score")]
        Score,

        [EnumStringValue("simulcast")]
        Simulcast,

        [EnumStringValue("svc")]
        Svc,

        [EnumStringValue("sctp")]
        Sctp,

        [EnumStringValue("message")]
        Message,
    }
}
