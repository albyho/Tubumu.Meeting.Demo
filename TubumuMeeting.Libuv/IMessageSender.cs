using System;

namespace TubumuMeeting.Libuv
{
    public interface IMessageSender<TMessage>
    {
        void Send(TMessage message, Action<Exception> callback);
    }
}
