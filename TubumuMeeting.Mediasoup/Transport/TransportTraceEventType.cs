using System;
using System.Collections.Generic;
using System.Text;
using Tubumu.Core.Extensions;

namespace TubumuMeeting.Mediasoup
{
    public enum TransportTraceEventType
    {
        [EnumStringValue("probation")]
        Probation,

        [EnumStringValue("bwe")]
        BWE
    }
}
