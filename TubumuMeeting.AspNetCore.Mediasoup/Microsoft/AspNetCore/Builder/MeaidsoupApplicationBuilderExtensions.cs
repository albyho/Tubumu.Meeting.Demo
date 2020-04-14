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
                    var logger = loggerFactory.CreateLogger<Mediasoup>();
                    var mediasoup = app.ApplicationServices.GetRequiredService<Mediasoup>();
                    var mediasoupOptions = app.ApplicationServices.GetRequiredService<MediasoupOptions>();
                    for (var c = 0; c < mediasoupOptions.NumberOfWorkers; c++)
                    {
                        var worker = app.ApplicationServices.GetRequiredService<Worker>();
                        mediasoup.Workers.Add(worker);
                        worker.On("@success", _ =>
                        {
                            logger.LogInformation($"worker[pid:{worker.ProcessId}] create success.");
                        });
                    }
                });
            });

            return app;
        }
    }
}
