﻿using System;
using Tubumu.Mediasoup;
using TubumuMeeting.Meeting;
using TubumuMeeting.Meeting.Server;

namespace Microsoft.Extensions.DependencyInjection
{
    public static class MeaidsoupServiceCollectionExtensions
    {
        public static IServiceCollection AddMediasoup(this IServiceCollection services, Action<MediasoupOptions>? setupAction = null)
        {
            var mediasoupOptions = MediasoupOptions.Default;
            setupAction?.Invoke(mediasoupOptions);
            services.AddSingleton(mediasoupOptions);
            services.AddSingleton<MediasoupServer>();
            services.AddTransient<Worker>();
            services.AddSingleton<Scheduler>();
            services.AddSingleton<BadDisconnectSocketService>();
            return services;
        }
    }
}
