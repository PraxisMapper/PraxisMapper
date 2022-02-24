using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PraxisCore;
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
            Log.WriteLog("PraxisMapper starting.");
            var host = CreateHostBuilder(args).Build();
            host.Run();
            Log.WriteLog("PraxisMapper closing.");
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseStartup<Startup>();
                    webBuilder.UseUrls("http://0.0.0.0:5000", "https://0.0.0.0:5001");
                });
    }
}
