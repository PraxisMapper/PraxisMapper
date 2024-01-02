using PraxisCore.Support;
using PraxisCore;
using PraxisDemosPlugin.Controllers;
using PraxisCore.GameTools;
using System.Diagnostics;
using PraxisMapper.Classes;

namespace PraxisDemosPlugin
{
    public class DemosStartup : IPraxisStartup
    {
        public static void Startup() {

            PraxisAuthentication.whitelistedPaths.Add("/Splatter/FreeSplat"); //Added to allow webview toy mode.
            //Insert Slippy values for map viewer
            GenericData.SetGlobalData("SlippyOverlay-Splatter", "Splatter/Slippy");

            SplatterController.colors = DemoStyles.splatterStyle.Count - 2;//-2 to exclude background.
                  TagParser.InsertStyles(DemoStyles.splatterStyle);

            Log.WriteLog("[DemosPlugin]: Loading splatter GeometryTrackers from DB", Log.VerbosityLevels.High);
            Stopwatch sw = Stopwatch.StartNew();
            for (int color = 0; color < SplatterController.colors; color++) 
            {
                var data = GenericData.GetGlobalData<GeometryTracker>("splat-" + color);
                if (data == null)
                    SplatterController.splatCollection.TryAdd(color, new GeometryTracker());
                else
                    SplatterController.splatCollection.TryAdd(color, data);
            }
            sw.Stop();
            Log.WriteLog("[DemosPlugin]: Loaded in " + sw.ElapsedMilliseconds + "ms", Log.VerbosityLevels.High);
        }
    }
}
