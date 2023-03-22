using PraxisCore.Support;
using PraxisCore;
using PraxisDemosPlugin.Controllers;
using PraxisCore.GameTools;

namespace PraxisDemosPlugin
{
    public class DemosStartup : IPraxisStartup
    {
        public static void Startup() {
            //foreach (var color in SplatterController.htmlColors)

            TagParser.InsertStyles(DemoStyles.splatterStyle);

            for (int color = 0; color < 32; color++)
            {
                var data = GenericData.GetGlobalData<GeometryTracker>("splats-" + color);
                if (data == null)
                    SplatterController.splatCollection.TryAdd(color, new GeometryTracker());
                else
                    SplatterController.splatCollection.TryAdd(color, data);
            }
        }
    }
}
