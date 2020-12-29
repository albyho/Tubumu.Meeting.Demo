﻿using System;
using SIPSorcery.SIP;

namespace Microsoft.Extensions.DependencyInjection
{
    public static class GB28181ServiceCollectionExtensions
    {
        public static IServiceCollection AddGB28281(this IServiceCollection services)
        {
            services.AddSingleton<SIPTransport>();
            return services;
        }
    }
}
