using Tubumu.Core.Extensions;

namespace TubumuMeeting.Mediasoup
{
    public enum WorkerLogTag
    {
        /// <summary>
        /// Logs about software/library versions, configuration and process information.
        /// </summary>
        [EnumStringValue("info")]
        Info,

        /// <summary>
        /// Logs about ICE.
        /// </summary>
        [EnumStringValue("ice")]
        Ice,

        /// <summary>
        /// Logs about DTLS.
        /// </summary>
        [EnumStringValue("dtls")]
        Dtls,

        /// <summary>
        /// Logs about RTP.
        /// </summary>
        [EnumStringValue("rtp")]
        Rtp,

        /// <summary>
        /// Logs about SRTP encryption/decryption.
        /// </summary>
        [EnumStringValue("srtp")]
        Srtp,

        /// <summary>
        /// Logs about RTCP.
        /// </summary>
        [EnumStringValue("rtcp")]
        Rtcp,

        /// <summary>
        /// Logs about RTP retransmission, including NACK/PLI/FIR.
        /// </summary>
        [EnumStringValue("rtx")]
        Rtx,

        /// <summary>
        /// Logs about transport bandwidth estimation.
        /// </summary>
        [EnumStringValue("bwe")]
        Bwe,

        /// <summary>
        /// Logs related to the scores of Producers and Consumers.
        /// </summary>
        [EnumStringValue("score")]
        Score,

        /// <summary>
        /// Logs about video simulcast.
        /// </summary>
        [EnumStringValue("simulcast")]
        Simulcast,

        /// <summary>
        /// Logs about video SVC.
        /// </summary>
        [EnumStringValue("svc")]
        Svc,

        /// <summary>
        /// Logs about SCTP (DataChannel).
        /// </summary>
        [EnumStringValue("sctp")]
        Sctp,

        /// <summary>
        /// Logs about messages (can be SCTP messages or direct messages).
        /// </summary>
        [EnumStringValue("message")]
        Message,
    }
}
