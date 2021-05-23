using CoreComponents;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using PraxisMapper.Classes;
using System.IO;
using System.Linq;

namespace PraxisMapper
{
    public class Startup
    {
        public Startup(IConfiguration configuration, IMemoryCache cache)
        {
            Configuration = configuration;
            PerformanceTracker.EnableLogging = Configuration.GetValue<bool>("enablePerformanceTracker");
            CoreComponents.Log.WriteToFile = Configuration.GetValue<bool>("enableFileLogging");
            PraxisContext.connectionString = Configuration.GetValue<string>("dbConnectionString");
            PraxisContext.serverMode = Configuration.GetValue<string>("dbMode");
            //AdminController.adminPwd = Configuration.GetValue<string>("adminPwd"); This pulls it directly from the configuration object in AdminController.

            TagParser.Initialize(false); //set to true when debugging new style rules without resetting the database entries.
            //Let's save some stuff to the cache.
            var db = new PraxisContext();
            var serverSettings = db.ServerSettings.FirstOrDefault();
            cache.Set("ServerSettings", serverSettings);
            var factions = db.Factions.ToList();
            cache.Set("Factions", factions);
            var paintTownConfigs = db.PaintTownConfigs.ToList();
            cache.Set("PTTConfigs", paintTownConfigs);

        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddControllers();
            services.AddMvc();
            services.AddCoreComponentServiceCollection(); //injects the DbContext and other services into this collection. (Eventually, still working on that)
            services.AddMemoryCache();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            //app.UseHttpsRedirection(); //Testing using only http on app instead of https to allow me to use a personal PC while getting a server functional
            app.UseStaticFiles( new StaticFileOptions() { FileProvider = new PhysicalFileProvider(Path.Combine(env.ContentRootPath, "Content")), RequestPath = "/Content" });
            app.UseRouting();

            //app.UseAuthorization(); //I dont really use this on this API

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
            });
        }
    }
}
