using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SIPSorcery.SIP;
using TubumuMeeting.GB28181.Settings;
using Tubumu.Core.Extensions;
using System;

namespace Microsoft.AspNetCore.Builder
{
    public static class ApplicationBuilderExtensions
    {
        public static IApplicationBuilder UseGB28181(this IApplicationBuilder app)
        {
            var loggerFactory = app.ApplicationServices.GetRequiredService<ILoggerFactory>();
            var logger = loggerFactory.CreateLogger("GB28281");

            var sipSeetings = app.ApplicationServices.GetRequiredService<SIPSettings>();
            var sipTransport = app.ApplicationServices.GetRequiredService<SIPTransport>();
            // TODO: 支持的SIP通道传输协议从配置中读取。
            var listenIP = sipSeetings.SIPServerIP.IsNullOrEmpty() ? IPAddress.Any : IPAddress.Parse(sipSeetings.SIPServerIP);
            sipTransport.AddSIPChannel(new SIPUDPChannel(new IPEndPoint(IPAddress.Any, sipSeetings.SIPServerPort)));
            sipTransport.SIPTransportRequestReceived += (SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPRequest sipRequest) =>
            {
                logger.LogDebug($"Request received {localSIPEndPoint}<-{remoteEndPoint}: {sipRequest.StatusLine} Thread:{Thread.CurrentThread.ManagedThreadId}");
                return Task.CompletedTask;
            };

            sipTransport.SIPTransportResponseReceived += (SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPResponse sipResponse) =>
            {
                logger.LogDebug($"Response received {localSIPEndPoint}<-{remoteEndPoint}: {sipResponse.ShortDescription} Thread:{Thread.CurrentThread.ManagedThreadId}");
                return Task.CompletedTask;
            };

            return app;
        }
    }
}
