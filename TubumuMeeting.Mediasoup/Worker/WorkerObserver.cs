using System;
using System.Collections.Generic;
using System.Text;

namespace TubumuMeeting.Mediasoup
{
    public class WorkerObserver
    {
        public event Action? Close;

        public event Action<Router>? NewRouter;

        public void EmitClose()
        {
            Close?.Invoke();
        }

        public void EmitNewRouter(Router router)
        {
            NewRouter?.Invoke(router);
        }
    }
}
