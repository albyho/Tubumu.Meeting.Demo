using System;
using System.Collections.Generic;
using System.Text;

namespace TubumuMeeting.Mediasoup
{
    public class AudioLevelObserverObserver
    {
        public event Action<AudioLevelObserverVolume[]>? Volumes;

        public event Action? Silence;

        public void EmitVolumes(AudioLevelObserverVolume[] volumes)
        {
            Volumes?.Invoke(volumes);
        }

        public void EmitSilence()
        {
            Silence?.Invoke();
        }
    }
}
