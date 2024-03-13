using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SimpleViewer.Models;
namespace SimpleViewer
{
    public class Startup(IConfiguration configuration)
    {
        public IConfiguration Configuration { get; } = configuration;
        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddControllers();
            var clientID = Configuration["APS_CLIENT_ID"];
            var clientSecret = Configuration["APS_CLIENT_SECRET"];
            var bucket = Configuration["APS_BUCKET"]; // Optional
            if (string.IsNullOrEmpty(clientID) || 
                string.IsNullOrEmpty(clientSecret) || 
                string.IsNullOrEmpty(bucket))
            {
                throw new ApplicationException("Missing required environment variables APS_CLIENT_ID or APS_CLIENT_SECRET or APS_BUCKET.");
            }
            services.AddSingleton<APS>(new APS(clientID, clientSecret, bucket));
        }
        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            app.UseDefaultFiles();
            app.UseStaticFiles();
            app.UseRouting();
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
            });
        }
    }
}
