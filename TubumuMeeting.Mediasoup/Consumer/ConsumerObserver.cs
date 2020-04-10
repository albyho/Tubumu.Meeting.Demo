using System;
using System.Collections.Generic;
using System.Text;

namespace TubumuMeeting.Mediasoup
{
    public class ConsumerObserver
    {
        public event Action? Close;

        public event Action? Pause;

        public event Action? Resume;

        public event Action<ConsumerScore>? Score;

        public event Action<ConsumerLayers?>? LayersChange;

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

        public void EmitScore(ConsumerScore score)
        {
            Score?.Invoke(score);
        }

        public void EmitLayersChange(ConsumerLayers? consumerLayers)
        {
            LayersChange?.Invoke(consumerLayers);
        }

        public void EmitTrace(TraceEventData traceEventData)
        {
            Trace?.Invoke(traceEventData);
        }
    }
}
