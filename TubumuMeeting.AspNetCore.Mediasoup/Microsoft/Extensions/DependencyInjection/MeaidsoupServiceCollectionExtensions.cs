using System;
using TubumuMeeting.Mediasoup;
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
            services.AddTransient<Worker>();
            services.AddSingleton<MeetingManager>();
            return services;
        }
    }
}
