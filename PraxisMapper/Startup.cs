using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CoreComponents;
using PraxisMapper.Classes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace PraxisMapper
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
            PerformanceTracker.EnableLogging = Configuration.GetValue<bool>("enablePerformanceTracker");
            CoreComponents.Log.WriteToFile = Configuration.GetValue<bool>("enableFileLogging");
            PraxisContext.connectionString = Configuration.GetValue<string>("dbConnectionString");
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddControllers();
            services.AddCoreComponentServiceCollection(); //injects the DbContext and other services into this collection. (Eventually, still working on that)
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            //app.UseHttpsRedirection(); //Testing using only http on app instead of https to allow me to use a personal PC while getting a server functional

            app.UseRouting();

            //app.UseAuthorization(); //I dont really use this on this API

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
            });
        }
    }
}
