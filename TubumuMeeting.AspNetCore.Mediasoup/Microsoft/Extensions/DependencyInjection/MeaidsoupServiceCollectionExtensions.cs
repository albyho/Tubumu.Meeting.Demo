using System;
using System.Threading.Tasks;
using TubumuMeeting.Mediasoup;
using TubumuMeeting.Libuv;
using TubumuMeeting.Meeting;

namespace Microsoft.Extensions.DependencyInjection
{
    public static class MeaidsoupServiceCollectionExtensions
    {
        public static IServiceCollection AddMediasoup(this IServiceCollection services, Action<MediasoupOptions>? configure = null)
        {
            var mediasoupOptions = MediasoupOptions.Default;
            configure?.Invoke(mediasoupOptions);
            services.AddSingleton(mediasoupOptions);
            services.AddSingleton<MediasoupServer>();
            services.AddSingleton<RoomManager>();
            services.AddTransient<Worker>();
            return services;
        }
    }
}
