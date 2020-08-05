using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace TubumuMeeting.Web
{
    public class Program
    {
        public static void Main(string[] args)
        {
            CreateHostBuilder(args).Build().Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddDebug();
                logging.AddConsole();
            })
            .ConfigureWebHostDefaults(webBuilder =>
            {
                var configs = new ConfigurationBuilder()
                    .AddJsonFile("Hosting.json", optional: false)
                    .AddJsonFile("mediasoupsettings.json", optional: false)
                    .AddJsonFile("consulsettings.json", optional: false)
                    .Build();

                webBuilder.UseConfiguration(configs);
                webBuilder.UseStartup<Startup>();
            });
    }
}
