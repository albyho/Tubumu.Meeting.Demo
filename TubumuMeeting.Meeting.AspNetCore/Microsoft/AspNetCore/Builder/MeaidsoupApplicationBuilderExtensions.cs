using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Logging;
using TubumuMeeting.Libuv;
using TubumuMeeting.Mediasoup;

namespace Microsoft.Extensions.DependencyInjection
{
    public static class ApplicationBuilderExtensions
    {
        public static IApplicationBuilder UseMediasoup(this IApplicationBuilder app)
        {
            Task.Run(() =>
            {
                Loop.Default.Run(() =>
                {
                    var loggerFactory = app.ApplicationServices.GetRequiredService<ILoggerFactory>();
                    var logger = loggerFactory.CreateLogger<MediasoupServer>();
                    var mediasoupServer = app.ApplicationServices.GetRequiredService<MediasoupServer>();
                    var mediasoupOptions = app.ApplicationServices.GetRequiredService<MediasoupOptions>();
                    for (var c = 0; c < mediasoupOptions.MediasoupStartupSettings.NumberOfWorkers!; c++)
                    {
                        var worker = app.ApplicationServices.GetRequiredService<Worker>();
                        worker.On("@success", _ =>
                        {
                            mediasoupServer.AddWorker(worker);
                            logger.LogInformation($"worker[pid:{worker.ProcessId}] create success.");
                        });
                    }
                });
            });

            return app;
        }
    }
}
