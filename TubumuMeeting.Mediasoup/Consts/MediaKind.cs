using System;
using System.Collections.Generic;
using System.Text;
using Tubumu.Core.Extensions;

namespace TubumuMeeting.Mediasoup
{
    public enum MediaKind
    {
        [EnumStringValue("audio")]
        Audio,

        [EnumStringValue("video")]
        Video
    }
}
