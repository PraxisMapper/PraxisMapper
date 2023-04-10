using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using NetTopologySuite.Geometries.Prepared;
using PraxisCore;
using PraxisCore.PbfReader;
using PraxisCore.Support;
using PraxisMapper.Classes;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net;
using System.Reflection;

namespace PraxisMapper {
    public class Startup {
        string mapTilesEngine;
        bool usePerfTracker;
        bool useHeaderCheck;
        bool useAuthCheck;
        bool useAntiCheat;
        bool usePlugins;
        string maintenanceMessage = "";
        public static IConfiguration Configuration { get; set; }

        public Startup(IConfiguration configuration)  //can't use MemoryCache here, have to wait until Configure for services and DI
        {
            Configuration = configuration;
            usePerfTracker = Configuration.GetValue<bool>("enablePerformanceTracker");
            useHeaderCheck = Configuration.GetValue<bool>("enableHeaderCheck");
            useAuthCheck = Configuration.GetValue<bool>("enableAuthCheck");
            useAntiCheat = Configuration.GetValue<bool>("enableAntiCheat");
            usePlugins = Configuration.GetValue<bool>("enablePlugins");
            maintenanceMessage = Configuration.GetValue<string>("maintenanceMessage");
            PraxisHeaderCheck.ServerAuthKey = Configuration.GetValue<string>("serverAuthKey");
            Log.SaveToFile = Configuration.GetValue<bool>("enableFileLogging");
            PraxisContext.serverMode = Configuration.GetValue<string>("dbMode");
            PraxisContext.connectionString = Configuration.GetValue<string>("dbConnectionString");
            DataCheck.DisableBoundsCheck = Configuration.GetValue<bool>("DisableBoundsCheck");
            MapTileSupport.SlippyTileSizeSquare = Configuration.GetValue<int>("slippyTileSize");
            MapTileSupport.BufferSize = Configuration.GetValue<double>("AreaBuffer");
            MapTileSupport.GameTileScale = Configuration.GetValue<int>("mapTileScaleFactor");

            mapTilesEngine = Configuration.GetValue<string>("MapTilesEngine");
        }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services) {
            Console.WriteLine("Connecting to " + Configuration.GetValue<string>("dbMode"));
            BuildAndLoadDB();

            services.AddControllers();
            services.AddMvc();
            services.AddMemoryCache(); //AddMvc calls this quietly, but I'm calling it explicitly here anyways.
            services.AddResponseCompression();

            var executionFolder = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            IMapTiles mapTiles = null;

            Assembly asm = null;
            if (mapTilesEngine == "SkiaSharp")
                asm = Assembly.LoadFrom(executionFolder + "/PraxisMapTilesSkiaSharp.dll");
            else if (mapTilesEngine == "ImageSharp") 
                asm = Assembly.LoadFrom(executionFolder + "/PraxisMapTilesImageSharp.dll");

            mapTiles = (IMapTiles)Activator.CreateInstance(asm.GetType("PraxisCore.MapTiles"));
            services.AddSingleton(typeof(IMapTiles), mapTiles);

            TagParser.Initialize(Configuration.GetValue<bool>("ForceStyleDefaults"), mapTiles);
            MapTileSupport.MapTiles = mapTiles;

            if (usePlugins)
                foreach (var potentialPlugin in Directory.EnumerateFiles(executionFolder + "/plugins", "*.dll")) {
                    try {
                        var assembly = Assembly.LoadFile(potentialPlugin);
                        var types = assembly.GetTypes().Where(t => t.IsAssignableTo(typeof(IPraxisPlugin)));
                        var startupTypes = assembly.GetTypes().Where(t => t.IsAssignableTo(typeof(IPraxisStartup)));
                        if (startupTypes.Any()) {
                            foreach (var s in startupTypes) {
                                try {
                                    var startupMethod = s.GetMethod("Startup");
                                    var results = startupMethod.Invoke(s, null);
                                }
                                catch (Exception ex) {
                                    ErrorLogger.LogError(new Exception("Error thrown attempting to load " + potentialPlugin));
                                    while (ex.InnerException != null)
                                        ex = ex.InnerException;
                                    ErrorLogger.LogError(ex);
                                }
                            }
                        }

                        if (types.Any()) {
                            Log.WriteLog("Loading plugin " + potentialPlugin);
                            services.AddControllersWithViews().AddApplicationPart(assembly);
                        }
                        else {
                            assembly = null;
                        }
                    }
                    catch (Exception ex) {
                        //continue.
                        Log.WriteLog("Error loading " + potentialPlugin + ": " + ex.Message + "|" + ex.StackTrace);
                        ErrorLogger.LogError(ex);
                    }
                }

            //Some plugins need TagParser initialized to do their work. Some add styles.
            //Re-initialize TagParser here so that any new styles are in place and ready to go
            TagParser.Initialize(Configuration.GetValue<bool>("ForceStyleDefaults"), mapTiles);

            //Start cleanup threads that fire every half hour.
            System.Threading.Tasks.Task.Run(() => {
                var db = new PraxisContext();
                db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
                db.ChangeTracker.AutoDetectChangesEnabled = false;
                while (true) {
                    PraxisAuthentication.DropExpiredEntries();
                    db.Database.ExecuteSqlRaw("DELETE FROM PlaceData WHERE expiration IS NOT NULL AND expiration < CURRENT_TIMESTAMP");
                    db.Database.ExecuteSqlRaw("DELETE FROM AreaData WHERE expiration IS NOT NULL AND expiration < CURRENT_TIMESTAMP");
                    db.Database.ExecuteSqlRaw("DELETE FROM PlayerData WHERE expiration IS NOT NULL AND expiration < CURRENT_TIMESTAMP");
                    //remember, don't delete map tiles here, since those track how many times they've been redrawn so the client knows when to ask for the image again.

                    if (useAntiCheat) {
                        List<string> toRemove = new List<string>();
                        foreach (var entry in PraxisAntiCheat.antiCheatStatus)
                            if (entry.Value.validUntil < DateTime.Now)
                                toRemove.Add(entry.Key);

                        foreach (var i in toRemove)
                            PraxisAntiCheat.antiCheatStatus.TryRemove(i, out var removed);
                    }
                    System.Threading.Thread.Sleep(1800000); // 30 minutes in milliseconds
                }
            });
        }

        /// <summary>
        /// This function allows PraxisMapper to populate itself from a .pbf file in the same folder. Allows for the fastest setup of a functioning server.
        /// </summary>
        private void BuildAndLoadDB() {
            var db = new PraxisContext();
            db.MakePraxisDB(); //Does nothing if DB already exists, creates DB if not.

            //if the DB is empty, attmept to auto-load from a pbf file. This removes a couple of manual steps from smaller games, even if it takes a few extra minutes.
            if (!db.Places.Any()) {
                TagParser.Initialize(true, null); //Force the default styles in place, since we've created the DB and haven't yet started up a drawing plugin.
                Log.WriteLog("No data loaded, attempting to auto-load");
                var relationAsBounds = Configuration.GetValue<long>("useRelationForBounds");
                var candidates = Directory.EnumerateFiles(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "*.pbf");
                if (candidates.Any()) {
                    PbfReader reader = new PbfReader();
                    reader.outputPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) + "\\";
                    reader.saveToDB = true;
                    reader.ProcessFile(candidates.First(), relationAsBounds);
                    Log.WriteLog("Done populating DB from " + candidates.First());
                    db = new PraxisContext();
                    db.SetServerBounds(relationAsBounds);
                }
            }
         }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env, IMemoryCache cache) {
            var db = new PraxisContext();

            if (env.IsDevelopment()) {
                app.UseDeveloperExceptionPage();
            }

            //app.UseHttpsRedirection(); //Testing using only http on app instead of https to allow me to use a personal PC while getting a server functional
            app.UseStaticFiles(new StaticFileOptions() { FileProvider = new PhysicalFileProvider(Path.Combine(env.ContentRootPath, "Content")), RequestPath = "/Content" });
            app.UseRouting();
            app.UseResponseCompression();

            app.UsePraxisMaintenanceMessage(maintenanceMessage);
            app.UseGlobalErrorHandler();

            if (useHeaderCheck)
                app.UsePraxisHeaderCheck();

            if (useAuthCheck) {
                app.UsePraxisAuthentication();
                //PraxisAuthentication.whitelistedPaths.Add("/MapTile"); //May may this toggleable.
                PraxisAuthentication.whitelistedPaths.Add("/Server/Test"); //Don't require a sucessful login to confirm server is alive.
                PraxisAuthentication.whitelistedPaths.Add("/Server/Login"); //Don't require a sucessful login to login.
                PraxisAuthentication.whitelistedPaths.Add("/Server/CreateAccount"); //Don't require a sucessful login to make a new account
                PraxisAuthentication.whitelistedPaths.Add("/Server/ServerBounds"); //Required for Slippy stuff to work.
                PraxisAuthentication.whitelistedPaths.Add("/Content"); //Don't require a sucessful login to get images/scripts.
                PraxisAuthentication.admins = db.AuthenticationData.Where(a => a.isAdmin).Select(a => a.accountId).ToHashSet();
            }

            if (useAntiCheat) {
                app.UsePraxisAntiCheat();
                PraxisAntiCheat.expectedCount = db.AntiCheatEntries.Select(c => c.filename).Distinct().Count();
            }

            if (usePerfTracker)
                app.UsePraxisPerformanceTracker();

            app.UseEndpoints(endpoints => {
                endpoints.MapControllers();
            });

            //Populate the memory cache with some data we won't edit until a restart occurs.
            var entryOptions = new MemoryCacheEntryOptions().SetPriority(CacheItemPriority.NeverRemove);

            var settings = db.ServerSettings.First();
            cache.Set<DbTables.ServerSetting>("settings", settings, entryOptions);
            var serverBounds = Converters.GeoAreaToPreparedPolygon(new Google.OpenLocationCode.GeoArea(settings.SouthBound, settings.WestBound, settings.NorthBound, settings.EastBound));
            DataCheck.bounds = serverBounds;
            cache.Set<IPreparedGeometry>("serverBounds", serverBounds, entryOptions);
            cache.Set("saveMapTiles", Configuration.GetValue<bool>("saveMapTiles"));

            System.Threading.Tasks.Task.Run(() => {
                while (true) {
                    ((MemoryCache)cache).Compact(.5);
                    System.Threading.Thread.Sleep(1800000); // 30 minutes in milliseconds
                }
            });

            PraxisPerformanceTracker.LogInfoToPerfData("Startup", "PraxisMapper online.");
            Log.WriteLog("PraxisMapper configured and running at:");
            Log.WriteLog(Dns.GetHostName());
            foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces().Where(a => a.NetworkInterfaceType == NetworkInterfaceType.Ethernet || a.NetworkInterfaceType == NetworkInterfaceType.Wireless80211))
            {
                foreach (var ip in adapter.GetIPProperties().UnicastAddresses)
                    if (ip.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && !ip.Address.ToString().StartsWith("169.254"))
                    {
                        Log.WriteLog(ip.Address.ToString());
                    }
            }
        }
    }
}
