using System;
using Tubumu.Core.Extensions;

namespace TubumuMeeting.Mediasoup
{
	/// <summary>
	/// Consumer type.
	/// </summary>
	public enum ConsumerType
	{
		[EnumStringValue("simple")]
		simple,

		[EnumStringValue("simulcast")]
		Simulcast,

		[EnumStringValue("svc")]
		Svc,

		[EnumStringValue("pipe")]
		Pipe
	}
}
