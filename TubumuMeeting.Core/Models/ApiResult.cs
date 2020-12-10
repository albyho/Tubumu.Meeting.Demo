using Newtonsoft.Json;

namespace Tubumu.Core.Models
{
    public class ApiResult
    {
        public int Code { get; set; } = 200;

        public string Message { get; set; }
    }

    public class ApiResult<T> : ApiResult
    {
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public T Data { get; set; }
    }
}
