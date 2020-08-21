namespace TubumuMeeting.Meeting.Server
{
    public class ConsulSettings
    {
        public bool Enabled { get; set; }

        public string ServiceName { get; set; }

        public string ServiceIP { get; set; }

        public int ServicePort { get; set; }

        public string ServiceHealthCheck { get; set; }

        public string ConsulAddress { get; set; }
    }
}
