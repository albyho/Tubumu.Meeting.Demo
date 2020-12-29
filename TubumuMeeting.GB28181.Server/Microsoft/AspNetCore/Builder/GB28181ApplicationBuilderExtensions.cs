using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SIPSorcery.SIP;

namespace Microsoft.AspNetCore.Builder
{
    public static class ApplicationBuilderExtensions
    {
        public static IApplicationBuilder UseGB28181(this IApplicationBuilder app)
        {
            var loggerFactory = app.ApplicationServices.GetRequiredService<ILoggerFactory>();
            var logger = loggerFactory.CreateLogger("GB28281");
            var sipTransport = app.ApplicationServices.GetRequiredService<SIPTransport>();
            // TODO: 支持的传输协议、IP和端口从配置中读取
            sipTransport.AddSIPChannel(new SIPUDPChannel(new IPEndPoint(IPAddress.Any, 5060)));
            sipTransport.SIPTransportRequestReceived += (SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPRequest sipRequest) =>
            {
                logger.LogDebug($"Request received {localSIPEndPoint}<-{remoteEndPoint}: {sipRequest.StatusLine}");
                return Task.CompletedTask;
            };

            sipTransport.SIPTransportResponseReceived += (SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPResponse sipResponse) =>
            {
                logger.LogDebug($"Response received {localSIPEndPoint}<-{remoteEndPoint}: {sipResponse.ShortDescription}");
                return Task.CompletedTask;
            };

            return app;
        }
    }
}
