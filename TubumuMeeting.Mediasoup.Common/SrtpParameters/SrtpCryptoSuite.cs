using Tubumu.Core.Extensions;

namespace TubumuMeeting.Mediasoup
{
    /// <summary>
    /// SRTP crypto suite.
    /// </summary>
    public enum SrtpCryptoSuite
    {
        [EnumStringValue("AES_CM_128_HMAC_SHA1_80")]
        AES_CM_128_HMAC_SHA1_80,

        [EnumStringValue("AES_CM_128_HMAC_SHA1_32")]
        AES_CM_128_HMAC_SHA1_32
    }
}
