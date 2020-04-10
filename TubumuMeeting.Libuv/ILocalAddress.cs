using System;

namespace TubumuMeeting.Libuv
{
	public interface ILocalAddress<T>
	{
		T LocalAddress { get; }
	}
}

