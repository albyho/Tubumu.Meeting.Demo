using System;
using System.Threading.Tasks;
using TubumuMeeting.Mediasoup;
using TubumuMeeting.Libuv;
using Microsoft.AspNetCore.Builder;

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
                    var mediasoup = app.ApplicationServices.GetRequiredService<Mediasoup>();
                    var mediasoupOptions = app.ApplicationServices.GetRequiredService<MediasoupOptions>();
                    for (var c = 0; c < mediasoupOptions.NumberOfWorkers; c++)
                    {
                        var worker = app.ApplicationServices.GetRequiredService<IWorker>();
                        mediasoup.Workers.Add(worker);
                    }
                });
            });

            return app;
        }
    }
}
