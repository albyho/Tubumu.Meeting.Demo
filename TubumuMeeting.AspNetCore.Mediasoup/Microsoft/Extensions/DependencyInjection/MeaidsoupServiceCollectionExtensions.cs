using System;
using System.Threading.Tasks;
using TubumuMeeting.Mediasoup;
using TubumuMeeting.Libuv;

namespace Microsoft.Extensions.DependencyInjection
{
    public static class MeaidsoupServiceCollectionExtensions
    {
        public static IServiceCollection AddMediasoup(this IServiceCollection services, Action<MediasoupOptions>? configure = null)
        {
            var mediasoupOptions = MediasoupOptions.Default;
            configure?.Invoke(mediasoupOptions);
            services.AddSingleton(mediasoupOptions);
            services.AddSingleton<Mediasoup>();
            services.AddTransient<IWorker, Worker>();
            return services;
        }
    }
}
