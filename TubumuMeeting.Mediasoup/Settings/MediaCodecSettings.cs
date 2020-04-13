using System.Collections.Generic;

namespace TubumuMeeting.Mediasoup
{
    public class MediaCodecSettings
    {
        public MediaKind Kind { get; set; }

        public string MimeType { get; set; }

        public int ClockRate { get; set; }

        public int? Channels { get; set; }

        public IDictionary<string, object> Parameters { get; set; }
    }
}
