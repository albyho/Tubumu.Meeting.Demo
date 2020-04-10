using System;
using Tubumu.Core.Extensions;

namespace TubumuMeeting.Mediasoup
{
	/// <summary>
	/// Valid types for 'trace' event.
	/// </summary>
	public enum TraceEventType
	{
		/// <summary>
		/// RTP
		/// </summary>
		[EnumStringValue("rtp")]
		RTP,

		[EnumStringValue("keyframe")]
		Keyframe,

		/// <summary>
		/// 关键帧
		/// </summary>
		[EnumStringValue("nack")]
		nack,

		/// <summary>
		/// PLI: (Picture Loss Indication) 视频帧丢失重传
		/// </summary>
		[EnumStringValue("pli")]
		PLI,

		/// <summary>
		/// Full Intra Request
		/// </summary>
		[EnumStringValue("fir")]
		FIR
	}
}
