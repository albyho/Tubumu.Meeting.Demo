using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

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
            .ConfigureWebHostDefaults(webBuilder =>
            {
                var mediasoupconfig = new ConfigurationBuilder()
                    .AddJsonFile("mediasoupsettings.json", optional: false)
                    .AddJsonFile("consulsettings.json", optional: false)
                    .Build();
                webBuilder.UseConfiguration(mediasoupconfig);
                webBuilder.UseStartup<Startup>();
            });
    }
}
