using System.Collections.Generic;
using Tubumu.Core.Extensions.Object;

namespace TubumuMeeting.Mediasoup
{
    public class Mediasoup
    {
        public List<Worker> Workers { get; set; } = new List<Worker>();

        /// <summary>
        /// Get a cloned copy of the mediasoup supported RTP capabilities.
        /// </summary>
        /// <returns></returns>
        public static RtpCapabilities GetSupportedRtpCapabilities()
        {
            return RtpCapabilities.SupportedRtpCapabilities.DeepClone<RtpCapabilities>();
        }
    }
}
