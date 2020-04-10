using Newtonsoft.Json;

namespace TubumuMeeting.Mediasoup
{
    public class RequestMessage
    {
        public int Id { get; set; }

        public string Method { get; set; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public object? Internal { get; set; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public object? Data { get; set; }
    }

    public class ResponseMessage
    {
        public int? Id { get; set; }

        public string? TargetId { get; set; }

        public string? Event { get; set; }

        public bool? Accepted { get; set; }

        public string? Error { get; set; }

        public string? Reason { get; set; }

        public object? Data { get; set; }
    }
}
