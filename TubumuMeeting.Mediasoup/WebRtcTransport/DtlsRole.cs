using Tubumu.Core.Extensions;

namespace TubumuMeeting.Mediasoup
{
    public enum DtlsRole
    {
        [EnumStringValue("auto")]
        Auto,

        [EnumStringValue("client")]
        Client,

        [EnumStringValue("server")]
        Server
    }
}
