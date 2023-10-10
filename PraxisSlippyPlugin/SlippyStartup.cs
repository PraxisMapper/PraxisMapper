using PraxisCore.GameTools;
using PraxisCore.Support;
using PraxisCore;
using System.Diagnostics;
using PraxisCore.Styles;
using static PraxisCore.DbTables;

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
        GenericData.SetGlobalData("SlippyOverlay-Cities (filled)", "MapTile/Slippy/adminBoundsFilled/city");
        GenericData.SetGlobalData("SlippyOverlay-States (filled)", "MapTile/Slippy/adminBoundsFilled/state");
        GenericData.SetGlobalData("SlippyOverlay-Countries (filled)", "MapTile/Slippy/adminBoundsFilled/country");

        Log.WriteLog("[SlippyPlugin]: Loaded default values to database", Log.VerbosityLevels.High);
    }
}