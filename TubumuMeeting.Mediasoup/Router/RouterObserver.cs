using System;
using System.Collections.Generic;
using System.Text;

namespace TubumuMeeting.Mediasoup
{
    public class RouterObserver
    {
        public event Action? Close;

        public event Action<Transport>? NewTransport;

        public event Action<RtpObserver>? NewRtpObserver;

        public void EmitClose()
        {
            Close?.Invoke();
        }

        public void EmitNewTransport(Transport transport)
        {
            NewTransport?.Invoke(transport);
        }

        public void EmitNewRtpObserver(RtpObserver rtpObserver)
        {
            NewRtpObserver?.Invoke(rtpObserver);
        }
    }
}
