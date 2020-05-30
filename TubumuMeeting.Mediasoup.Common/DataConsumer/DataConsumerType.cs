using Tubumu.Core.Extensions;

namespace TubumuMeeting.Mediasoup
{
    public enum DataConsumerType
    {
        [EnumStringValue("sctp")]
        Sctp,

        [EnumStringValue("direct")]
        Direct
    }
}
