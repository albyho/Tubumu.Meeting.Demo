using System.IO;
using Microsoft.Extensions.Hosting;
using NLog;
using NLog.Web;

namespace Microsoft.AspNetCore.Builder
{
    public static class NLogHostBuilderExtensions
    {
        public static IHostBuilder UseNLogWeb(this IHostBuilder builder)
        {
            builder.UseNLog();
            builder.ConfigureAppConfiguration((context, configuration) =>
            {
                var environment = context.HostingEnvironment;
                NLogBuilder.ConfigureNLog($"{environment.ContentRootPath}{Path.DirectorySeparatorChar}NLog.config");
                LogManager.Configuration.Variables["configDir"] = environment.ContentRootPath;
            });

            return builder;
        }
    }
}
