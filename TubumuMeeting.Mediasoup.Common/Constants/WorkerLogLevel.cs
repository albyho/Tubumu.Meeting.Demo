using Tubumu.Core.Extensions;

namespace TubumuMeeting.Mediasoup
{
    public enum WorkerLogLevel
    {
        /// <summary>
        /// Log all severities.
        /// </summary>
        [EnumStringValue("debug")]
        Debug,

        /// <summary>
        /// Log “warn” and “error” severities.
        /// </summary>
        [EnumStringValue("warn")]
        Warn,

        /// <summary>
        /// Log “error” severity.
        /// </summary>
        [EnumStringValue("error")]
        Error,

        /// <summary>
        /// Do not log anything.
        /// </summary>
        [EnumStringValue("none")]
        None
    }
}
