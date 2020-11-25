using NetTopologySuite.Geometries;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

namespace CoreComponents
{

    //TODO possible changes:

    public class DbTables
    {
        public class PlayerData
        {
            public int PlayerDataID { get; set; }
            public string deviceID { get; set; }
            public int t10Cells { get; set; }
            public int t8Cells { get; set; }
            public int cellVisits { get; set; }
            public double distance { get; set; }
            public int score { get; set; }
            public int DateLastTrophyBought { get; set; }
            public int timePlayed { get; set; }
            public double maxSpeed { get; set; }
            public double totalSpeed { get; set; }
            public int altitudeSpread { get; set; }
            public DateTime lastSyncTime { get; set; }
        }

        public class PerformanceInfo
        {
            public int PerformanceInfoID { get; set; }
            public string functionName { get; set; }
            public long runTime { get; set; }
            public DateTime calledAt { get; set; }
            public string notes { get; set; }
        }

        public class MapData
        {
            public long MapDataId { get; set; }
            public string name { get; set; }

            [Column(TypeName = "geography")]
            public Geometry place { get; set; } //allows any sub-type of Geometry to be used
            public string type { get; set; }//Still need this for admin boundary levels.
            public long? WayId { get; set; }
            public long? NodeId { get; set; }
            public long? RelationId { get; set; }

            //Temporarily removing these: adding this to the global data set takes an hour and creates a log file the size of the DB.
            //public AreaType AreaType { get; set; }
            public int AreaTypeId { get; set; }

        }       

        //Reference table for names of areas we care about storing.
        public class AreaType
        {
            public int AreaTypeId { get; set; }
            public string AreaName { get; set; }
            public string OsmTags { get; set; } //These are not 1:1, so this column may not be useful after all.
            public string HtmlColorCode { get; set; } //for tile-drawing operations, possibly in-app stuff too.
        }

        public class MapTile
        { 
            public long MapTileId { get; set; } //int should be OK for a limited range game and/or big tiles. Making this long just to make sure.
            public string PlusCode { get; set; } //MapTiles are drawn for Cell8 or Cell10 areas.
            public byte[] tileData { get; set; } //png binary data.
            public bool regenerate { get; set; } //TODO: If 1, re-make this tile because MapData for the area has changed
            public int resolutionScale { get; set; } //10 or 11, depending on the Cell size 1 pixel is. Usually 11
        }

        public class PremadeResults
        {
            public long PremadeResultsId { get; set; }
            public string PlusCode6 { get; set; }
            public string Data { get; set; } 
        }

        public class AreaControlTeam //A table for tracking which player faction controls which area (we dont store data on player location on the servers)
        {
            public long AreaControlTeamId { get; set; }
            public int factionId { get; set; }
            public long MapDataId { get; set; }
            public long points { get; set; } //a quick reference of how many cells this area takes to own. Saving here to reduce calculations if/when I set up a scoreboard of areas owned.
        }

        public class Faction
        {
            public long FactionId { get; set; }
            public string Name { get; set; }
        }

        public class GeneratedMapData
        {
            public long GeneratedMapDataId { get; set; } //TODO: determine the best way to make this separate table not have ID collisions, if possible, with the main MapData table.
            public string name { get; set; } //probably won't get a specific name by default, but games may want one here.

            [Column(TypeName = "geography")]
            public Geometry place { get; set; } //allows any sub-type of Geometry to be used
            public string type { get; set; }// not apparently used in this table, but kept for possible compatibility depending on how AreaTypeId ends up being used.

            public int AreaTypeId { get; set; } //This will probably be a fixed value.

        }

    }
}

