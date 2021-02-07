using System.Text.Json.Serialization;

namespace Tubumu.Meeting.Client.WPF
{
    /// <summary>
    /// MeetingMessage
    /// </summary>
    public class MeetingMessage
    {
        public int Code { get; set; } = 200;

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? InternalCode { get; set; }

        public string Message { get; set; } = "Success";

        public static string Stringify(int code, string message, string? data = null)
        {
            if (data == null)
            {
                return $"{{\"code\":{code},\"message\":\"{message}\"}}";
            }
            return $"{{\"code\":{code},\"message\":\"{message}\",\"data\":{data}}}";
        }
    }

    /// <summary>
    /// MeetingMessage
    /// </summary>
    public class MeetingMessage<T> : MeetingMessage
    {
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public T Data { get; set; }
    }
}
