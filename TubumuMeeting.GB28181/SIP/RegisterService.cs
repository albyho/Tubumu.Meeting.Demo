using System;
using Microsoft.Extensions.Logging;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using Tubumu.Core.Extensions;
using TubumuMeeting.GB28181.Settings;

namespace TubumuMeeting.GB28181.SIP
{
    public class RegisterService : IRegisterService
    {
        private ILogger<RegisterService> _logger;
        private readonly SIPSettings _sipSettings;
        private readonly SIPTransport _sipTransport;
        private readonly SIPAuthenticateRequestDelegate _sipRequestAuthenticator = SIPRequestAuthenticator.AuthenticateSIPRequest;

        public RegisterService(ILogger<RegisterService> logger, SIPSettings sipSettings, SIPTransport sipTransport)
        {
            _logger = logger;
            _sipSettings = sipSettings;
            _sipTransport = sipTransport;
        }

        private int GetRequestedExpiry(SIPRequest registerRequest)
        {
            var contactHeaderExpiry = !registerRequest.Header.Contact.IsNullOrEmpty() ? registerRequest.Header.Contact[0].Expires : (int?)null;
            return contactHeaderExpiry ?? registerRequest.Header.Expires;
        }
    }
}
