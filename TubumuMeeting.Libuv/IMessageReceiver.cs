using System;

namespace TubumuMeeting.Libuv
{
	public interface IMessageReceiver<TMessage>
	{
		event Action<TMessage> Message;
	}
}

