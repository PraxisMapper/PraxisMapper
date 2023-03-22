using PraxisCore.Support;
using PraxisCore;
using PraxisDemosPlugin.Controllers;

namespace PraxisDemosPlugin
{
    public class DemosStartup : IPraxisStartup
    {
        public static void Startup() {
            //foreach (var color in SplatterController.htmlColors)

            TagParser.InsertStyles(DemoStyles.splatterStyle);

            for (int color = 0; color < 32; color++)
            {
                var data = GenericData.GetGlobalData("splats-" + color).ToUTF8String();
                if (!string.IsNullOrWhiteSpace(data))
                    SplatterController.splatCollection.TryAdd(color, Singletons.geomTextReader.Read(data));
                else
                    SplatterController.splatCollection.TryAdd(color, Singletons.geometryFactory.CreatePolygon());
            }
        }
    }
}
