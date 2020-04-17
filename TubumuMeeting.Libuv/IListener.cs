using System;

namespace TubumuMeeting.Libuv
{
    public interface IListener<TStream>
    {
        void Listen();
        event Action Connection;
        TStream Accept();
    }
}

