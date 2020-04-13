namespace TubumuMeeting.Mediasoup
{
	public class PipeToRouterOptions
	{
		/// <summary>
		/// The id of the Producer to consume.
		/// </summary>
		public string? ProducerId { get; set; }

		/// <summary>
		/// The id of the DataProducer to consume.
		/// </summary>
		public string? DataProducerId { get; set; }

		/// <summary>
		/// Target Router instance.
		/// </summary>
		public Router Router { get; set; }

		/// <summary>
		/// IP used in the PipeTransport pair. Default '127.0.0.1'.
		/// </summary>
		public TransportListenIp? ListenIp { get; set; } = new TransportListenIp { Ip = "0.0.0.0", AnnouncedIp = "127.0.0.1" };

		/// <summary>
		/// Create a SCTP association. Default false.
		/// </summary>
		public bool? EnableSctp { get; set; } = false;

		/// <summary>
		/// SCTP streams number.
		/// </summary>
		public NumSctpStreams? NumSctpStreams { get; set; }

		/// <summary>
		/// Enable RTX and NACK for RTP retransmission.
		/// </summary>
		public bool? EnableRtx { get; set; }

		/// <summary>
		/// Enable SRTP.
		/// </summary>
		public bool? EnableSrtp { get; set; }
	}
}
