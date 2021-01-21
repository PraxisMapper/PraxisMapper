using NetTopologySuite.Geometries;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CoreComponents
{
    public class DbTables
    {
        public class PlayerData
        {
            public int PlayerDataID { get; set; }
            public string deviceID { get; set; }   
            public string DisplayName { get; set; }
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
            [Required]
            public Geometry place { get; set; } //allows any sub-type of Geometry to be used
            public string type { get; set; }//Still need this for admin boundary levels.
            public long? WayId { get; set; }
            public long? NodeId { get; set; }
            public long? RelationId { get; set; }

            //Temporarily removing these: adding this to the global data set takes an hour and creates a log file the size of the DB.
            //public AreaType AreaType { get; set; }
            public int AreaTypeId { get; set; }
        }       

        //Reference table
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
            //public bool regenerate { get; set; } //TODO: If 1, re-make this tile because MapData for the area has changed. This might be done just by deleting the old map tile.
            public int resolutionScale { get; set; } //10 or 11, depending on the Cell size 1 pixel is. Usually 11
            public int mode { get; set; } //is this for general use, multiplayer area control. 1 = 'baseline map', 2 = 'Multiplayer Area Control' overlay.
            public DateTime CreatedOn { get; set; } //Timestamp for when a map tile was generated
            public DateTime ExpireOn { get; set; } //assume that a tile needs regenerated if passed this timestamp. Possibly cleared out via SQL script.
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
            public int FactionId { get; set; }
            public long MapDataId { get; set; }
            public bool IsGeneratedArea { get; set; }
            public long points { get; set; } //a quick reference of how many cells this area takes to own. Saving here to reduce calculations if/when I set up a scoreboard of areas owned.
            public DateTime claimedAt { get; set; }
        }

        public class Faction
        {
            public long FactionId { get; set; }
            public string Name { get; set; }
            public string HtmlColor { get; set; } //Should be transparent, so this can be overlaid on top of the normal map tile.
        }

        public class GeneratedMapData
        {
            public long GeneratedMapDataId { get; set; } //TODO: determine the best way to make this separate table not have ID collisions, if possible, with the main MapData table.
            public string name { get; set; } //probably won't get a specific name by default, but games may want one here.

            [Column(TypeName = "geography")]
            [Required]
            public Geometry place { get; set; } //allows any sub-type of Geometry to be used
            public string type { get; set; }// not apparently used in this table, but kept for possible compatibility depending on how AreaTypeId ends up being used.

            public int AreaTypeId { get; set; } //This will probably be a fixed value.
            public DateTime GeneratedAt { get; set; }
        }

        public class TurfWarEntry
        {
            public long TurfWarEntryId { get; set; }
            public int TurfWarConfigId { get; set; } //If we're running multiple TurfWar instances at once, this lets us identify which one belongs to which.
            public string Cell10 { get; set; }
            public string Cell8 { get; set; } //Index this one
            public int FactionId { get; set; }
            public DateTime ClaimedAt { get; set; }
            public DateTime CanFlipFactionAt { get; set; }
        }

        public class TurfWarConfig
        {
            //A table for one instance entry? Meh, but these need to update and persist between app pool expirations.
            //Do i want to allow a server to host multiple instance? I might, if someone wanted to have different durations running.
            //Do i want an Enabled flag to be checked so a game can be turned on and off without resetting stats?
            public int TurfWarConfigId { get; set; }
            public string Name { get; set; } //help identify which game/scoreboard we're looking at.
            public int TurfWarDurationHours { get; set; } //how long to let a game run for. Set to -1 to make a permanent turf war mode.
            public DateTime TurfWarNextReset { get; set; } //add TurfWarDurationHours to this if we're past the expiration time. Subtract to see the last reset date.
            public int Cell10LockoutTimer { get; set; } //The number of seconds that a Cell10 entry cannot be flipped for when a valid claim happens.
            public bool Repeating { get; set; } //Does this instance automatically repeat
            public DateTime StartTime { get; set; } //For non-repeating instances, when to start taking requests.
        }

        public class TurfWarScoreRecord
        {
            public long TurfWarScoreRecordId { get; set; }
            public int TurfWarConfigId { get; set; }
            public string Results { get; set; } //A concatenated set of results into one column.
            public int WinningFactionID { get; set; }
            public int WinningScore { get; set; }
            public DateTime RecordedAt { get; set; }
        }

        //Note: this is the server tracking user devices for Turf War team assignments. This doesn't track any data about the user or their device
        //that wasn't generated on the server itself. The primary use for this is to ensure we generate balanced teams during a Turf War instance.
        public class TurfWarTeamAssignment
        {
            public long TurfWarTeamAssignmentId { get; set; }
            public string deviceID { get; set; }
            public int TurfWarConfigId { get; set; }
            public int FactionId { get; set; }
            public DateTime ExpiresAt { get; set; } //Set to the finish time for this TurfWarConfigId
        }

        public class ErrorLog
        {
            public int ErrorLogId { get; set; }
            public string Message { get; set; }
            public string StackTrace { get; set; }
            public DateTime LoggedAt { get; set; }

        }

        public class TagParserEntry
        {
            //For users that customize the rules for parsing tags.
            //Some types may have duplicate entries to handle wider spreads?
            //Will need to support ||, &&, and ! conditions.
            //Tag pairs are Key:Value per OSM data.
            public int id { get; set; }
            public string name { get; set; }
            public int typeID { get; set; }
            public string matchRules { get; set; }
            public string HtmlColorCode { get; set; }
            public int Priority { get; set; } //order tags should be matched in. EX: Retail should be matched before Building, since its more specific and useful.
        }

        public class ServerSetting
        {
            public int ServerSettingId { get; set; } //just for a primary key
            public double NorthBound { get; set; }
            public double EastBound { get; set; }
            public double SouthBound { get; set; }
            public double WestBound { get; set; }
        }



    }
}

