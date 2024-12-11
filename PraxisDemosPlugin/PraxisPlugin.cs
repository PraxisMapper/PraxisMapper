using PraxisCore;
using PraxisCore.GameTools;
using PraxisCore.Support;
using PraxisDemosPlugin.Controllers;
using PraxisMapper.Classes;
using System.Diagnostics;

namespace PraxisDemosPlugin
{
    public class PraxisPlugin : IPraxisPlugin
    {
        public string PrivacyPolicy => "";
        public string PluginName => "Demos";
        public float Version => 1.0f;
        public List<DbTables.StyleEntry> Styles => DemoStyles.splatterStyle;

        public void OnCreateAccount(string accountId)
        {
            return;
        }

        public void OnDeleteAccount(string accountId)
        {
            return;
        }

        public void Startup()
        {
            PraxisAuthentication.whitelistedPaths.Add("/Splatter/FreeSplat"); //Added to allow webview toy mode.
            //Insert Slippy values for map viewer
            GenericData.SetGlobalData("SlippyOverlay-Splatter", "Splatter/Slippy");
            SplatterController.colors = DemoStyles.splatterStyle.Count - 2;//-2 to exclude background.

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
