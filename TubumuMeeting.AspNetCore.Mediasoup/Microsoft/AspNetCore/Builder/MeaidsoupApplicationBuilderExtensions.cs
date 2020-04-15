using System;
using System.Threading.Tasks;
using TubumuMeeting.Mediasoup;
using TubumuMeeting.Libuv;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Logging;

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
                    var logger = loggerFactory.CreateLogger<MediasoupWorkerManager>();
                    var mediasoupWorkerManager = app.ApplicationServices.GetRequiredService<MediasoupWorkerManager>();
                    var mediasoupOptions = app.ApplicationServices.GetRequiredService<MediasoupOptions>();
                    for (var c = 0; c < mediasoupOptions.NumberOfWorkers; c++)
                    {
                        var worker = app.ApplicationServices.GetRequiredService<Worker>();
                        worker.On("@success", _ =>
                        {
                            mediasoupWorkerManager.AddWorker(worker);
                            logger.LogInformation($"worker[pid:{worker.ProcessId}] create success.");
                        });
                    }
                });
            });

            return app;
        }
    }
}
