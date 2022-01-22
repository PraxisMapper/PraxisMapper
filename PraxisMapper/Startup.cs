using PraxisCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using NetTopologySuite.Geometries.Prepared;
using PraxisMapper.Classes;
using System.IO;
using System.Linq;
using System.Reflection;
using System;

namespace PraxisMapper
{
    public class Startup
    {
        string mapTilesEngine;

        public Startup(IConfiguration configuration)  //can't use MemoryCache here, have to wait until Configure for services and DI
        {
            Configuration = configuration;
            PerformanceTracker.EnableLogging = Configuration.GetValue<bool>("enablePerformanceTracker");
            PraxisCore.Log.WriteToFile = Configuration.GetValue<bool>("enableFileLogging");
            PraxisContext.connectionString = Configuration.GetValue<string>("dbConnectionString");
            PraxisContext.serverMode = Configuration.GetValue<string>("dbMode");
            PraxisHeaderCheck.enableAuthCheck = Configuration.GetValue<bool>("enableAuthCheck");
            PraxisHeaderCheck.ServerAuthKey = Configuration.GetValue<string>("serverAuthKey");
            //AdminController.adminPwd = Configuration.GetValue<string>("adminPwd"); This pulls it directly from the configuration object in AdminController.

            mapTilesEngine = Configuration.GetValue<string>("MapTilesEngine");

            TagParser.Initialize(Configuration.GetValue<bool>("ForceTagParserDefaults")); //set to true when debugging new style rules without resetting the database entries.
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddControllers();
            services.AddMvc();
            //services.AddCoreComponentServiceCollection(); //injects the DbContext and other services into this collection. (Eventually, still working on that)
            services.AddMemoryCache(); //AddMvc calls this quietly, but I'm calling it explicitly here anyways.

            if (mapTilesEngine == "SkiaSharp")
            {
                var asm = Assembly.LoadFrom(@".\bin\Debug\net6.0\PraxisMapTilesSkiaSharp.dll"); //works in debug. path isn't gonna work in publish.
                var mapTiles = Activator.CreateInstance(asm.GetType("PraxisCore.MapTiles"));
                services.AddSingleton(typeof(IMapTiles), mapTiles); //compiles
            }
            else if (mapTilesEngine == "ImageSharp")
            {
                var asm = Assembly.LoadFrom(@".\bin\Debug\net6.0\PraxisMapTilesImageSharp.dll"); //works in debug. path isn't gonna work in publish.
                var mapTiles = Activator.CreateInstance(asm.GetType("PraxisCore.MapTiles"));
                services.AddSingleton(typeof(IMapTiles), mapTiles);
            }
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env, IMemoryCache cache)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            //app.UseHttpsRedirection(); //Testing using only http on app instead of https to allow me to use a personal PC while getting a server functional
            app.UseStaticFiles( new StaticFileOptions() { FileProvider = new PhysicalFileProvider(Path.Combine(env.ContentRootPath, "Content")), RequestPath = "/Content" });
            app.UseRouting();

            app.UsePraxisHeaderCheck();
            app.UseGlobalErrorHandler();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
            });

            //Populate the memory cache with some data we won't edit until a restart occurs.
            var entryOptions = new MemoryCacheEntryOptions().SetPriority(CacheItemPriority.NeverRemove);

            var db = new PraxisContext();
            var settings = db.ServerSettings.First();
            cache.Set<DbTables.ServerSetting>("settings", settings, entryOptions);
            var serverBounds = Converters.GeoAreaToPreparedPolygon(new Google.OpenLocationCode.GeoArea(settings.SouthBound, settings.WestBound, settings.NorthBound, settings.EastBound));
            cache.Set<IPreparedGeometry>("serverBounds", serverBounds, entryOptions);
            cache.Set("caching", settings.enableMapTileCaching); //convenience entry
            IMapTiles.MapTileSizeSquare = settings.SlippyMapTileSizeSquare; //TODO: set in config instead of static value?
        }
    }
}
