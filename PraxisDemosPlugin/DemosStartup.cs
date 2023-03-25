using PraxisCore.Support;
using PraxisCore;
using PraxisDemosPlugin.Controllers;
using PraxisCore.GameTools;
using System.Diagnostics;

namespace PraxisDemosPlugin
{
    public class DemosStartup : IPraxisStartup
    {
        public static void Startup() {
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
