using System;
using System.IO;
using TubumuMeeting.Libuv;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace TubumuMeeting.Web
{
    public class Startup
    {
        // This method gets called by the runtime. Use this method to add services to the container.
        // For more information on how to configure your application, visit https://go.microsoft.com/fwlink/?LinkID=398940
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddMediasoup(configure =>
            {
                configure.MediasoupSettings.WorkerSettings.LogLevel = Mediasoup.WorkerLogLevel.Debug;
                configure.NumberOfWorkers = 1;
                configure.WorkerPath = Path.Combine((System.Environment.OSVersion.Platform == PlatformID.Unix) || (System.Environment.OSVersion.Platform == PlatformID.MacOSX) ? 
                    @"/Users/alby/Developer/OpenSource/Meeting/Lab/w" : 
                    @"C:\Developer\OpenSource\Meeting\worker", 
                    "Release", "mediasoup-worker");
            });
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseRouting();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapGet("/", async context =>
                {
                    await context.Response.WriteAsync("Hello World!");
                });
            });

            app.UseMediasoup();
        }
    }
}
