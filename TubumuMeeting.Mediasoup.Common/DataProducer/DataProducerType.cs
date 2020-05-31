using Tubumu.Core.Extensions;

namespace TubumuMeeting.Mediasoup
{
    public enum DataProducerType
    {
        [EnumStringValue("sctp")]
        Sctp,

        [EnumStringValue("direct")]
        Direct
    }
}
