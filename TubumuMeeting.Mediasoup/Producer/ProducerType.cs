using System;
using Tubumu.Core.Extensions;

namespace TubumuMeeting.Mediasoup
{
	/// <summary>
	/// Producer type.
	/// </summary>
	public enum ProducerType
	{
		[EnumStringValue("simple")]
		simple,

		[EnumStringValue("simulcast")]
		Simulcast,

		[EnumStringValue("svc")]
		Svc
	}
}
