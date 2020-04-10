using System;
using Tubumu.Core.Extensions;

namespace TubumuMeeting.Mediasoup
{
	/// <summary>
	/// 'trace' event data.
	/// </summary>
	public class TraceEventData
	{
		/// <summary>
		/// Trace type.
		/// </summary>
		public TraceEventType Type { get; set; }

		/// <summary>
		/// Event timestamp.
		/// </summary>
		public long Timestamp { get; set; }

		/// <summary>
		/// Event direction.
		/// </summary>
		public TraceEventDirection Direction { get; set; }

		/// <summary>
		/// Per type information.
		/// </summary>
		public object Info { get; set; }
	}
}
