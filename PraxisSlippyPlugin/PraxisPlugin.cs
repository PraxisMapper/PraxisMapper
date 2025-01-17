﻿using PraxisCore;
using PraxisCore.Support;

namespace PraxisSlippyPlugin
{
    public class PraxisPlugin : IPraxisPlugin
    {
        public List<DbTables.StyleEntry> Styles => new List<DbTables.StyleEntry>();
        public string PrivacyPolicy => "";
        public string PluginName => "Slippy";
        public float Version => 1.0f;

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
            //Reset default styles to the database on startup
            //Format is Slippy-{name} for key and the URL to use for value
            GenericData.SetGlobalData("SlippyBase-OSMLike", "MapTile/Slippy/mapTiles");
            GenericData.SetGlobalData("SlippyBase-Suggested Gameplay", "MapTile/Slippy/suggestedGameplay");
            GenericData.SetGlobalData("SlippyOverlay-All Admin Bounds", "MapTile/Slippy/adminBounds");
            GenericData.SetGlobalData("SlippyOverlay-All Place Outlines", "MapTile/Slippy/outlines");
            GenericData.SetGlobalData("SlippyOverlay-Area Control", "MapTile/Slippy/teamColor");
            GenericData.SetGlobalData("SlippyOverlay-Countries (filled)", "MapTile/Slippy/adminBoundsFilled/country");
            GenericData.SetGlobalData("SlippyOverlay-Regions(filled)", "MapTile/Slippy/adminBoundsFilled/region");
            GenericData.SetGlobalData("SlippyOverlay-States (filled)", "MapTile/Slippy/adminBoundsFilled/state");
            GenericData.SetGlobalData("SlippyOverlay-Admin Level 5 (filled)", "MapTile/Slippy/adminBoundsFilled/admin5");
            GenericData.SetGlobalData("SlippyOverlay-Counties (filled)", "MapTile/Slippy/adminBoundsFilled/county");
            GenericData.SetGlobalData("SlippyOverlay-Townships (filled)", "MapTile/Slippy/adminBoundsFilled/township");
            GenericData.SetGlobalData("SlippyOverlay-Cities (filled)", "MapTile/Slippy/adminBoundsFilled/city");
            GenericData.SetGlobalData("SlippyOverlay-Wards (filled)", "MapTile/Slippy/adminBoundsFilled/ward");
            GenericData.SetGlobalData("SlippyOverlay-Neighborhoods (filled)", "MapTile/Slippy/adminBoundsFilled/neighborhood");
            GenericData.SetGlobalData("SlippyOverlay-Admin Level 11 (filled)", "MapTile/Slippy/adminBoundsFilled/admin11");

            Log.WriteLog("[SlippyPlugin]: Loaded default values to database", Log.VerbosityLevels.High);
        }
    }
}
