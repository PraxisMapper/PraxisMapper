using CoreComponents;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PraxisMapper.Classes;

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
            PraxisContext.serverMode = Configuration.GetValue<string>("dbMode");
            //AdminController.adminPwd = Configuration.GetValue<string>("adminPwd"); This pulls it directly from the configuration object in AdminController.
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddControllers();
            services.AddMvc();
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
            //app.UseStaticFiles(); //Was to let Leaflet load, but I haven't yet gotten that lined up for some reason. Might need more settings or a specific path?
            app.UseRouting();

            //app.UseAuthorization(); //I dont really use this on this API

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
            });
        }
    }
}
