using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tubumu.Libuv;
using Tubumu.Mediasoup;

namespace Microsoft.AspNetCore.Builder
{
    public static class ApplicationBuilderExtensions
    {
        public static IApplicationBuilder UseMediasoup(this IApplicationBuilder app)
        {
            ThreadPool.QueueUserWorkItem(_ =>
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
                            logger.LogInformation($"Worker[pid:{worker.ProcessId}] create success.");
                            return Task.CompletedTask;
                        });
                    }
                });
            });

            return app;
        }
    }
}
