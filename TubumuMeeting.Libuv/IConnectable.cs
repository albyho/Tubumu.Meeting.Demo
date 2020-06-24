using System;

namespace TubumuMeeting.Libuv
{
    public interface IConnectable<TType, TEndPoint>
    {
        void Connect(TEndPoint endPoint, Action<Exception> callback);
    }
}
