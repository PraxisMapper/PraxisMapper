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
            DataCheck.DisableBoundsCheck = Configuration.GetValue<bool>("DisableBoundsCheck");

            mapTilesEngine = Configuration.GetValue<string>("MapTilesEngine");
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddControllers();
            services.AddMvc();
            //services.AddCoreComponentServiceCollection(); //injects the DbContext and other services into this collection. (Eventually, still working on that)
            services.AddMemoryCache(); //AddMvc calls this quietly, but I'm calling it explicitly here anyways.

            IMapTiles mapTiles = null;

            if (mapTilesEngine == "SkiaSharp")
            {
                Assembly asm;
                if (System.Diagnostics.Debugger.IsAttached) //Folders vary, when debugging in IIS run path and local folder aren't the same so we check here.
                    asm = Assembly.LoadFrom(@".\bin\Debug\net6.0\PraxisMapTilesSkiaSharp.dll");
                else
                    asm = Assembly.LoadFrom(@"PraxisMapTilesSkiaSharp.dll");
                mapTiles = (IMapTiles)Activator.CreateInstance(asm.GetType("PraxisCore.MapTiles"));
                services.AddSingleton(typeof(IMapTiles), mapTiles); //compiles
            }
            else if (mapTilesEngine == "ImageSharp")
            {
                Assembly asm;
                if (System.Diagnostics.Debugger.IsAttached)
                    asm = Assembly.LoadFrom(@".\bin\Debug\net6.0\PraxisMapTilesImageSharp.dll");
                else
                    asm = Assembly.LoadFrom(@"PraxisMapTilesImageSharp.dll");
                mapTiles = (IMapTiles)Activator.CreateInstance(asm.GetType("PraxisCore.MapTiles"));
                services.AddSingleton(typeof(IMapTiles), mapTiles);
            }

            TagParser.Initialize(Configuration.GetValue<bool>("ForceTagParserDefaults"), mapTiles);
            MapTileSupport.MapTiles = mapTiles;
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
            IMapTiles.MapTileSizeSquare = settings.SlippyMapTileSizeSquare;

            Log.WriteLog("PraxisMapper configured and running.");
        }
    }
}
