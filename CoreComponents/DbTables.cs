using NetTopologySuite.Geometries;
using SkiaSharp;
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
            public long FactionId { get; set; }
        }

        public class PerformanceInfo
        {
            public int PerformanceInfoID { get; set; }
            public string functionName { get; set; }
            public long runTime { get; set; }
            public DateTime calledAt { get; set; }
            public string notes { get; set; }
        }

        //public class MapData
        //{
        //    public long MapDataId { get; set; }
        //    public string name { get; set; }

        //    [Column(TypeName = "geography")]
        //    [Required]
        //    public Geometry place { get; set; } //allows any sub-type of Geometry to be used
        //    public string type { get; set; }//Still need this for admin boundary levels.
        //    public long? WayId { get; set; }
        //    public long? NodeId { get; set; }
        //    public long? RelationId { get; set; }
        //    public int AreaTypeId { get; set; }
        //    [NotMapped]
        //    public SKPaint paint { get; set; }
        //    public double? AreaSize { get; set; } //Added recently, this should make drawing bigger map tiles faster when used as a filter to not load areas smaller than 1 pixel.

        //    //public MapData Clone()
        //    //{
        //    //    return (MapData)this.MemberwiseClone();
        //    //}
        //}

        //public class AdminBound //A copy of MapData for the AdminBound table.
        //{
        //    public long AdminBoundId { get; set; }
        //    public string name { get; set; }

        //    [Column(TypeName = "geography")]
        //    [Required]
        //    public Geometry place { get; set; } //allows any sub-type of Geometry to be used
        //    public string type { get; set; }//Still need this for admin boundary levels.
        //    public long? WayId { get; set; }
        //    public long? NodeId { get; set; }
        //    public long? RelationId { get; set; }
        //    public int AreaTypeId { get; set; }
        //    public double? AreaSize { get; set; } //Added recently, this should make drawing bigger map tiles faster when used as a filter to not load areas smaller than 1 pixel.

        //    //public AdminBound Clone()
        //    //{
        //    //    return (AdminBound)this.MemberwiseClone();
        //    //}

        //    //public static implicit operator MapData(AdminBound a) => new MapData() { AreaSize = a.AreaSize, AreaTypeId = a.AreaTypeId, MapDataId = 0, name = a.name, NodeId = a.NodeId, place = a.place, RelationId = a.RelationId, type = a.type, WayId = a.WayId };
        //    //public static implicit operator AdminBound(MapData a) => new AdminBound() { AreaSize = a.AreaSize, AreaTypeId = a.AreaTypeId, AdminBoundId = 0, name = a.name, NodeId = a.NodeId, place = a.place, RelationId = a.RelationId, type = a.type, WayId = a.WayId };
        //}

        public class TileTracking
        {
            public long TileTrackingId { get; set; }
            public string PlusCodeCompleted { get; set; }
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
            [Column(TypeName = "geography")]
            [Required]
            public Geometry areaCovered { get; set; } //This lets us find and expire map tiles if the data under them changes.
        }

        public class AreaControlTeam //A table for tracking which player faction controls which area (we dont store data on player location on the servers)
        {
            public long AreaControlTeamId { get; set; }
            public long FactionId { get; set; }
            public long StoredWayId { get; set; }
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
            public long GeneratedMapDataId { get; set; } 
            public string name { get; set; } //probably won't get a specific name by default, but games may want one here.

            [Column(TypeName = "geography")]
            [Required]
            public Geometry place { get; set; } //allows any sub-type of Geometry to be used
            public string type { get; set; }// not apparently used in this table, but kept for possible compatibility depending on how AreaTypeId ends up being used.
            public DateTime GeneratedAt { get; set; }
        }

        public class PaintTownEntry
        {
            public long PaintTownEntryId { get; set; }
            public int PaintTownConfigId { get; set; } //If we're running multiple PaintTown instances at once, this lets us identify which one belongs to which.
            public string Cell10 { get; set; }
            public string Cell8 { get; set; }
            public int FactionId { get; set; }
            public DateTime ClaimedAt { get; set; }
            public DateTime CanFlipFactionAt { get; set; }
        }

        public class PaintTownConfig
        {
            public int PaintTownConfigId { get; set; }
            public string Name { get; set; } //help identify which game/scoreboard we're looking at.
            public int DurationHours { get; set; } //how long to let a game run for. Set to -1 to make a permanent instance.
            public DateTime NextReset { get; set; } //add DurationHours to this if we're past the expiration time. Subtract to see the last reset date. Set to far in the future on permanent instances.
            public int Cell10LockoutTimer { get; set; } //The number of seconds that a Cell10 entry cannot be flipped for when a valid claim happens.
            public bool Repeating { get; set; } //Does this instance automatically repeat
            public DateTime StartTime { get; set; } //For non-repeating instances, when to start taking requests.
        }

        public class PaintTownScoreRecord
        {
            public long PaintTownScoreRecordId { get; set; }
            public int PaintTownConfigId { get; set; }
            public string Results { get; set; } //A concatenated set of results into one column.
            public int WinningFactionID { get; set; }
            public int WinningScore { get; set; }
            public DateTime RecordedAt { get; set; }
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
            //Tag pairs are Key:Value per OSM data.
            public long id { get; set; }
            public string name { get; set; }
            public int typeID { get; set; }
            public ICollection<TagParserMatchRule> TagParserMatchRules { get; set; }
            public string HtmlColorCode { get; set; }
            public string FillOrStroke { get; set; }
            public float LineWidth { get; set; }
            public string LinePattern { get; set; } //solid, dashed, other varieties? //If blank, solid line. If not, split string into float[] on |
            [NotMapped]
            public SKPaint paint { get; set; } //Fill in on app start.
            public int Priority { get; set; } //order tags should be matched in. EX: Retail should be matched before Building, since its more specific and useful. But usually smallest/shortest path goes last and biggest/longest goes first
            public bool IsGameElement { get; set; } // This tag should be used when asking for game areas, not just map tiles. Would let me use a single table for both again.
        }

        public class TagParserMatchRule
        { 
            public long id { get; set; }
            public ICollection<TagParserEntry> TagParserEntries { get; set; }
            public string Key { get; set; } //the left side of the key:value tag
            public string Value { get; set; } //the right side of the key:value tag
            public string MatchType { get; set; } //Any, all, contains, not.
        }

        public class ServerSetting
        {
            public int ServerSettingId { get; set; } //just for a primary key
            public double NorthBound { get; set; }
            public double EastBound { get; set; }
            public double SouthBound { get; set; }
            public double WestBound { get; set; }
        }

        public class SlippyMapTile
        {
            public long SlippyMapTileId { get; set; } //int should be OK for a limited range game and/or big tiles. Making this long just to make sure.
            public string Values { get; set; } //x|y|zoom
            public byte[] tileData { get; set; } //png binary data.
            public int mode { get; set; } //is this for general use, multiplayer area control. 1 = 'baseline map', 2 = 'Multiplayer Area Control' overlay, etc.
            public DateTime CreatedOn { get; set; } //Timestamp for when a map tile was generated
            public DateTime ExpireOn { get; set; } //assume that a tile needs regenerated if passed this timestamp. Possibly cleared out via SQL script.
            [Column(TypeName = "geography")]
            [Required]
            public Geometry areaCovered { get; set; } //This lets us find and expire map tiles if the data under them changes.
        }

        public class ZztGame //TODO rename
        {
            //The prototype class for the data a game played on a map needs to hold.
            public long id { get; set; }
            public string gameData { get; set; } //Expected: a JSON string of the game objects. Final format still TBD, but it'll be stored in string format.
            public Geometry gameLocation { get; set; } //A square drawn around the game's bounds. Initial expectations are these will be a Cell8 or less, though could be as big as a Cell6 for max boundary limit.
            public long UserId { get; set; } //The user that created this game.
        }

        public class GamesBeaten
        {
            //A simple table to track which players have cleared which games. Might be used for leaderboards, might also change color of the squares on the map for users? Both data columns need indexed.
            public long id { get; set; }
            public long UserId { get; set; }
            public long ZztGameId { get; set; }
        }

        //This is the new, 4th iteration of geography data storage for PraxisMapper.
        //All types can be stored in this one table, though some data will be applied on read
        //because the TagParser will determine it on-demand instead of storing changeable data.
        public class StoredWay
        {
            public long id { get; set; }
            public string name { get; set; }
            public long sourceItemID { get; set; }
            public int sourceItemType { get; set; } //1: node, 2: way, 3: relation, 4: Generated (non-real area)? Kinda want generated stuff to sit in its own table still.
            
            [Column(TypeName = "geography")]
            [Required]
            public Geometry wayGeometry { get; set; }
            public ICollection<WayTags> WayTags { get; set; }
            public bool IsGameElement { get; set; } //To use when determining if this element should or shouldn't be used as an answer when determining game interaction in an area.
            [NotMapped]
            public string GameElementName { get; set; } //Placeholder for TagParser to load up the name of the matching style for this element, but don't save it to the DB so we can change it on the fly.
            public double AreaSize { get; set; } //For sorting purposes.

            public override string ToString()
            {
                return (sourceItemType == 3 ? "Relation " : sourceItemType == 2 ? "Way " : "Node ") +  sourceItemID.ToString() + ":" + name;
            }

            public StoredWay Clone()
            {
                return (StoredWay)this.MemberwiseClone();
            }
        }
                
        public class WayTags
        {
            public long id { get; set; }
            public StoredWay storedWay { get; set; }
            public string Key { get; set; }
            public string Value { get; set; }

            public override string ToString()
            {
                return Key + ":" + Value;
            }
        }       
    }
}

