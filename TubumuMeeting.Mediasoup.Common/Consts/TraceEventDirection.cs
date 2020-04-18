using Tubumu.Core.Extensions;

namespace TubumuMeeting.Mediasoup
{
    /// <summary>
    /// Trace event direction.
    /// </summary>
    public enum TraceEventDirection
    {
        [EnumStringValue("in")]
        In,

        [EnumStringValue("out")]
        Out
    }
}
