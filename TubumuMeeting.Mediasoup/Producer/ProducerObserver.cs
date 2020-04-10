using System;
using System.Collections.Generic;
using System.Text;

namespace TubumuMeeting.Mediasoup
{
    public class ProducerObserver
    {
        public event Action? Close;

        public event Action? Pause;

        public event Action? Resume;

        public event Action<ProducerScore[]>? Score;

        public event Action<ProducerVideoOrientation>? VideoOrientationChange;

        public event Action<TraceEventData>? Trace;

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

        public void EmitScore(ProducerScore[] score)
        {
            Score?.Invoke(score);
        }

        public void EmitVideoOrientationChange(ProducerVideoOrientation producerVideoOrientation)
        {
            VideoOrientationChange?.Invoke(producerVideoOrientation);
        }

        public void EmitTrace(TraceEventData traceEventData)
        {
            Trace?.Invoke(traceEventData);
        }
    }
}
