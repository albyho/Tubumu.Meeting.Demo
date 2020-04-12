using System;
using System.Collections.Generic;
using System.Text;
using Tubumu.Core.Extensions.Object;

namespace TubumuMeeting.Mediasoup
{
    public class Mediasoup
    {
        public List<IWorker> Workers { get; set; } = new List<IWorker>();

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
