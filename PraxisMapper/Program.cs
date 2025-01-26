using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using PraxisCore;

namespace PraxisMapper {
    public class Program {
        public static void Main(string[] args) {
            Log.WriteLog("PraxisMapper starting.");
            var host = CreateHostBuilder(args).Build();
            host.Run();
            Log.WriteLog("PraxisMapper closing.");
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureWebHostDefaults(webBuilder => {
                    webBuilder.UseStartup<Startup>();
                    webBuilder.UseUrls("http://0.0.0.0:5005", "https://0.0.0.0:5001");
                });
    }
}
