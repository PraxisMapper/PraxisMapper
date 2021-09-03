using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CoreComponents;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace PraxisMapper
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var host = CreateHostBuilder(args).Build();

            //Let's save some stuff to the cache.
            var cache = host.Services.GetRequiredService<IMemoryCache>();
            var db = new PraxisContext();
            var serverSettings = db.ServerSettings.FirstOrDefault();
            cache.Set("ServerSettings", serverSettings);
            cache.Set("caching", serverSettings.enableMapTileCaching); //convenience entry
            var factions = db.Factions.ToList();
            cache.Set("Factions", factions);
            //Set configured Slippy Map tile size
            MapTiles.MapTileSizeSquare = serverSettings.SlippyMapTileSizeSquare;

            host.Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseStartup<Startup>();
                });
    }
}
