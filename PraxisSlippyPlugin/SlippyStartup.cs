using PraxisCore.GameTools;
using PraxisCore.Support;
using PraxisCore;
using System.Diagnostics;

public class PraxisSlippyStartup : IPraxisStartup
{
    public static void Startup()
    {
        //Reset default styles to the database on startup
        //Format is Slippy-{name} for key and the URL to use for value
        GenericData.SetGlobalData("SlippyBase-OSMLike", "MapTile/Slippy/mapTiles");
        GenericData.SetGlobalData("SlippyBase-Suggested Gameplay", "MapTile/Slippy/suggestedGameplay");
        GenericData.SetGlobalData("SlippyOverlay-All Admin Bounds", "MapTile/Slippy/adminBounds");
        GenericData.SetGlobalData("SlippyOverlay-All Place Outlines", "MapTile/Slippy/outlines");
        GenericData.SetGlobalData("SlippyOverlay-Area Control", "MapTile/Slippy/teamColor");

        Log.WriteLog("[SlippyPlugin]: Loaded default values to database", Log.VerbosityLevels.High);
    }
}