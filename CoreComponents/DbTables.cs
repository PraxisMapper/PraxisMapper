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
            public long StoredElementId { get; set; }
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
            public string fileName { get; set; } //A path to an image file that will be used as a repeating pattern. Null for solid colors.
            public ICollection<TagParserPaint> paintOperations { get; set; }
            public double minDrawRes { get; set; } = 0;//skip drawing this item if  resPerPixelX is below this value. (what doesn't draw zoomed in on OSM? name text?
            public double maxDrawRes { get; set; } = 4; //skip drawing this item if resPerPixelX is above this value. (EX: tertiary roads don't draw at distant zooms but primary roads do)
        }

        public class TagParserMatchRule
        { 
            public long id { get; set; }
            public ICollection<TagParserEntry> TagParserEntries { get; set; }
            public string Key { get; set; } //the left side of the key:value tag
            public string Value { get; set; } //the right side of the key:value tag
            public string MatchType { get; set; } //Any, all, contains, not.
        }

        public class TagParserPaint
        {
            //For enabling layering of drawn elements
            public long id { get; set; }
            //Layer note: it's still pretty possible that a lot of elements all get drawn on one layer, and only a few use multiple layers, like the outlined roads.
            public int layerId { get; set; } //All paint operations should be done on their own layer, then all merged together? smaller ID are on top, bigger IDs on bottom. Layer 1 hides 2 hides 3 etc.
            //The below are copied from the original object.These get used to create the SKPaint object once at startup, then that paint object is used from then on.
            public string HtmlColorCode { get; set; }
            public string FillOrStroke { get; set; }
            public float LineWidth { get; set; } //Todo ponder: should this be pixels, or degrees? or scale based on degreesperpixelx?
            public string LinePattern { get; set; } //solid, dashed, other varieties? //If blank, solid line. If not, split string into float[] on |
            public string fileName { get; set; } //A path to an image file that will be used as a repeating pattern. Null for solid colors.
            [NotMapped]
            public SKPaint paint { get; set; } //Fill in on app start.
            public double minDrawRes { get; set; } = 0;//skip drawing this item if  resPerPixelX is below this value. (what doesn't draw zoomed in on OSM? name text?
            public double maxDrawRes { get; set; } = 4; //skip drawing this item if resPerPixelX is above this value. (EX: tertiary roads don't draw at distant zooms

        }


        public class ServerSetting
        {
            public int id { get; set; } //just for a primary key
            public double NorthBound { get; set; }
            public double EastBound { get; set; }
            public double SouthBound { get; set; }
            public double WestBound { get; set; }
            public int SlippyMapTileSizeSquare { get; set; }
            public long NextAutoGeneratedAreaId { get; set; } = 1000000000000; //Track what our next ID is for generated areas without querying the table every time. Starts at 1 trillion.
            public long NextUserSuppliedAreaId { get; set; } = 2000000000000; //Track what our next ID is for user-generated areas without querying the table every time. Starts at 2 trillion.
            public string GameKey { get; set; } //This should be sent with requests from an app/game. If it doesn't match, return a 500 error. TODO: implement this across endpoints, in mobile app framework, and permit all if this is blank.
            public bool enableMapTileCaching { get; set; } = true;
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
        public class StoredOsmElement
        {
            public long id { get; set; }
            public string name { get; set; }
            public long sourceItemID { get; set; }
            public int sourceItemType { get; set; } //1: node, 2: way, 3: relation
            [Column(TypeName = "geography")]
            [Required]
            public Geometry elementGeometry { get; set; }
            public ICollection<ElementTags> Tags { get; set; }
            public bool IsGameElement { get; set; } //To use when determining if this element should or shouldn't be used as an answer when determining game interaction in an area.
            [NotMapped]
            public string GameElementName { get; set; } //Placeholder for TagParser to load up the name of the matching style for this element, but don't save it to the DB so we can change it on the fly.
            public double AreaSize { get; set; } //For sorting purposes.
            public bool IsGenerated { get; set; } //Was auto-generated for spaces devoid of IsGameElement areas to interact with. assumed IsGameElement is true. SourceItemId will be set to some magic value plus an increment.
            public bool IsUserProvided { get; set; } //A user created/uploaded this area. SourceItemId will be set to some magic value plus an increment.
            public override string ToString()
            {
                return (sourceItemType == 3 ? "Relation " : sourceItemType == 2 ? "Way " : "Node ") +  sourceItemID.ToString() + ":" + name;
            }

            public StoredOsmElement Clone()
            {
                return (StoredOsmElement)this.MemberwiseClone();
            }
        }
         
        //TODO: at some point i should investigate if its worth having a unique KeyValuePair table, and joining that to 
        //StoredOsmElement entries in this table instead. Would mean a join on sourceID/Type instead of the PK.
        //Needs some planning to rework things around that, and saves a little DB space. Is it faster for the ap to process?
        public class ElementTags
        {
            public long id { get; set; }
            public long SourceItemId { get; set; } //Needed to attach tags later. OR FKey this to StoredOsmElement.SourceItemId
            public int SourceItemType { get; set; } //Needed to attach tags later. 3 = relation, 2 = way, 1 = node.
            public StoredOsmElement storedOsmElement { get; set; }
            public string Key { get; set; }
            public string Value { get; set; }

            public override string ToString()
            {
                return Key + ":" + Value;
            }
        }

        //We can auto-create scavenger hunts from tags for the stand-alone game. 
        //Can do the same here, if we define a hunt as a tag and an area.
        //Manual scavenger hunts need to be a list of places (either a point or a shape, and a name/description of the place to go.)

        public class ScavengerHunt
        {
            public long id { get; set; }
            public string name { get; set; }
            public ICollection<ScavengerHuntEntry> entries { get; set; }
        }

        public class ScavengerHuntEntry
        {
            public long id { get; set; }
            public ScavengerHunt ScavengerHunt { get; set; }
            public string description { get; set; }
            public string StoredOsmElementId { get; set; } //This means we have to add a point/polygon if it's not an existing OSM entry.
        }

        public class CreatureCollectorConfigs
        {
            //The "walk around, find rare creatures of various types for your collection" mode info.
            //Determines some baseline properties of mode behaior, like global spawn rate and duration of spawns.
            public long id { get; set; }
            public double BaseSpawnRate { get; set; } //The odds of any single Cell10 area spawning a creature on a spawn check
            public double SpawnCheckTimer { get; set; } //How often, with active players, to check for new creature spawns within a Cell8 area.
            //Only check for creatures if an area has players (IE: on request), but only once per area. I do need to track times when this was called.
        }

        public class CreatureColllectorSpawnCheckTime
        {
            //When SpawnCheckTimer % DateTime.Now.Seconds == 0, check this table for the cell8's LastChecked time. If it's time value (to second accuracy)
            //is the same as DateTime.now, don't make new spawns because we already did. Update the record and return.
            public long id { get; set; }
            public string Cell8 { get; set; }
            public DateTime LastChecked { get; set; }
        }

        public class Creature
        {
            //Actual info on the creatures in question.
            public long id { get; set; }
            public string name { get; set; }
            public byte[] imageData { get; set; } //yes, i'm saving files in a database.
            public string imageName { get;  set; } //Or im saving paths to files for now
            public string type1 { get; set; }
            public string type2 { get; set; }

        }

        public class CreatureAreaRules
        {
            //For each gameplay area type, set what changes in that area 
            //EX: water areas spawn more water type creatures.
            public long id { get; set; }
        }

        public class PlayerCollections
        {
            //for storing collection data server-side.
            public long id { get; set; }
        }
    }
}

