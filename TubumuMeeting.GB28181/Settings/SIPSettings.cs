using System;
using System.Text;

namespace TubumuMeeting.GB28181.Settings
{
    public class SIPSettings
    {
        public string Id { get; set; } = Guid.Empty.ToString("N");

        public string Name { get; set; } = "Default SIP Server";

        public string SIPServerNumber { get; set; } = "34020000002000000001";

        public string SIPServerDomain { get; set; } = "3402000000";

        public string? SIPServerIP { get; set; }

        public ushort SIPServerPort { get; set; } = 5060;

        public bool Authentication { get; set; } = false;

        public string SIPDefaultUsername { get; set; } = "admin";

        public string SIPDefaultPassword { get; set; } = "123456";

        //public string SIPMessageEncoding { get; set; } = "GB2312";

        //public SIPChannelProtocol SIPChannelProtocols { get; set; } = SIPChannelProtocol.UDP;

        public int KeepaliveInterval { get; set; } = 3600;

        public int MaxTimeOutNumber { get; set; } = 3;
    }

    [Flags]
    public enum SIPChannelProtocol
    {
        UDP         = 1 << 0,
        TCP         = 1 << 1,
        TLS         = 1 << 2,
        WebSocket   = 1 << 3
    }
}
