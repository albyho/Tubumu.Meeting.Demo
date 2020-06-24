using System;

namespace TubumuMeeting.Libuv
{
    public class AsyncCallback : AsyncWatcher<Action>
    {
        public AsyncCallback(Loop loop)
            : base(loop)
        {
            Callback += (callback) => callback();
        }
    }
}
