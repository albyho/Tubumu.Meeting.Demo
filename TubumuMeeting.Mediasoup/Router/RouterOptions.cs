namespace TubumuMeeting.Mediasoup
{
	public class RouterOptions
	{
		/// <summary>
		/// Router media codecs.
		/// </summary>
		public RtpCodecCapability[] MediaCodecs { get; set; }

		/// <summary>
		/// Custom application data.
		/// </summary>
		public object? AppData { get; set; }
	}
}
