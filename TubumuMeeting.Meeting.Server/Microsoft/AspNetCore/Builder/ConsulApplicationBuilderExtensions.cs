using System;
using Consul;
using Microsoft.Extensions.Hosting;
using TubumuMeeting.Meeting.Server;

namespace Microsoft.AspNetCore.Builder
{
    public static class ConsulApplicationBuilderExtensions
    {
        public static IApplicationBuilder UseConsul(this IApplicationBuilder app, IHostApplicationLifetime lifetime, ConsulSettings consulSettings)
        {
            var consulClient = new ConsulClient(config =>
            {
                config.Address = new Uri(consulSettings.ConsulAddress);
            });

            var registration = new AgentServiceRegistration()
            {
                ID = Guid.NewGuid().ToString(),
                Name = consulSettings.ServiceName,// 服务名
                Address = consulSettings.ServiceIP, // 服务绑定IP
                Port = consulSettings.ServicePort, // 服务绑定端口
                Check = new AgentServiceCheck()
                {
                    DeregisterCriticalServiceAfter = TimeSpan.FromSeconds(5),//服务启动多久后注册
                    Interval = TimeSpan.FromSeconds(60),//健康检查时间间隔
                    HTTP = consulSettings.ServiceHealthCheck,//健康检查地址
                    Timeout = TimeSpan.FromSeconds(5)
                }
            };

            // 服务注册
            consulClient.Agent.ServiceRegister(registration).ConfigureAwait(false).GetAwaiter().GetResult();

            // 应用程序终止时，服务取消注册
            lifetime.ApplicationStopping.Register(() =>
            {
                consulClient.Agent.ServiceDeregister(registration.ID).ConfigureAwait(false).GetAwaiter().GetResult();
            });

            return app;
        }
    }
}
