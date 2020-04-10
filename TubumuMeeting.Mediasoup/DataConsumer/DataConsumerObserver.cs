using System;
using System.Collections.Generic;
using System.Text;

namespace TubumuMeeting.Mediasoup
{
    public class DataConsumerObserver
    {
        public event Action? Close;

        public void EmitClose()
        {
            Close?.Invoke();
        }
    }
}
