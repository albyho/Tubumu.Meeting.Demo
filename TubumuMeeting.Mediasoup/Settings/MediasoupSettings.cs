namespace TubumuMeeting.Mediasoup
{
    public class MediasoupSettings
    {
        public WorkerSettings WorkerSettings { get; set; }

        public RouteSettings RouteSettings { get; set; }

        public WebRtcTransportSettings WebRtcTransportSettings { get; set; }
    }
}
