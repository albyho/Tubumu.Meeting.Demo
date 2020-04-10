using System;
using System.Collections.Generic;
using System.Text;
using Tubumu.Core.Extensions;

namespace TubumuMeeting.Mediasoup
{
    public enum WorkerLogLevel
    {
        [EnumStringValue("debug")]
        Debug,

        [EnumStringValue("warn")]
        Warn,

        [EnumStringValue("error")]
        Error,

        [EnumStringValue("none")]
        None
    }
}
