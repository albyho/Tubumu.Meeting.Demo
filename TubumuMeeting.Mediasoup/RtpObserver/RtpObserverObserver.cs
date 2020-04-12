using System;
using System.Collections.Generic;
using System.Text;

namespace TubumuMeeting.Mediasoup
{
    public class RtpObserverObserver
    {
        public event Action? Close;

        public event Action? Pause;

        public event Action? Resume;

        public event Action<Producer>? AddProducer;

        public event Action<Producer>? RemoveProducer;

        public void EmitClose()
        {
            Close?.Invoke();
        }

        public void EmitPause()
        {
            Pause?.Invoke();
        }

        public void EmitResume()
        {
            Resume?.Invoke();
        }

        public void EmitAddProducer(Producer producer)
        {
            AddProducer?.Invoke(producer);
        }

        public void EmitRemoveProducer(Producer producer)
        {
            RemoveProducer?.Invoke(producer);
        }
    }
}
