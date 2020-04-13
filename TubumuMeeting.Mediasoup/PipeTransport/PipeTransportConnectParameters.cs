using System;
using System.Collections.Generic;
using System.Text;

namespace TubumuMeeting.Mediasoup
{
    public class PipeTransportConnectParameters
    {
        public string Ip { get; set; }

        public int Port { get; set; }

        public SrtpParameters SrtpParameters { get; set; }
    }
}
