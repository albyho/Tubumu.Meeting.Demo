using Tubumu.Core.Extensions;

namespace TubumuMeeting.Mediasoup
{
    /// <summary>
    /// Producer type.
    /// </summary>
    public enum ProducerType
    {
        [EnumStringValue("simple")]
        Simple,

        [EnumStringValue("simulcast")]
        Simulcast,

        [EnumStringValue("svc")]
        Svc,

        [EnumStringValue("pipe")]
        Pipe
    }
}
