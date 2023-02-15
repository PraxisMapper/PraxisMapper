using NetTopologySuite.Geometries;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PraxisCore
{
    public class DbTables
    {
        public class PlayerData
        {
            public int PlayerDataID { get; set; }
            public string accountId { get; set; }
            public string DataKey { get; set; }
            public DateTime? Expiration { get; set; }
            public byte[] IvData { get; set; } //Only set if data is encrypted.
            public byte[] DataValue { get; set; } //Holds byte data for both normal and encrypted entries. 

        }

        public class PerformanceInfo
        {
            public int PerformanceInfoID { get; set; }
            public string FunctionName { get; set; }
            public long RunTime { get; set; }
            public DateTime CalledAt { get; set; }
            public string Notes { get; set; }
        }

        public class MapTile
        {
            public long MapTileId { get; set; } 
            public string PlusCode { get; set; } //MapTiles are drawn for Cell8 areas.
            public byte[] TileData { get; set; } //png binary data.
            public string StyleSet { get; set; } //Which styleSet this maptile was drawn with. Allows for multiple layers and arbitrary types.
            public DateTime ExpireOn { get; set; } //assume that a tile needs regenerated if passed this timestamp. Set to CURRENT_TIMESTAMP to exipre.
            [Column(TypeName = "geography")]
            [Required]
            public Geometry AreaCovered { get; set; } //This lets us find and expire map tiles if the data under them changes.
            public long GenerationID { get; set; } = 0; //How many times this tile has been redrawn. Used by client to know when to get new tiles.
        }

        public class ErrorLog
        {
            public int ErrorLogId { get; set; }
            public string Message { get; set; }
            public string StackTrace { get; set; }
            public DateTime LoggedAt { get; set; }
        }

        public class GlobalData //Arbitrary data storage
        { 
            public int Id { get; set; }
            public string DataKey { get; set; }
            public byte[] DataValue { get; set; } //Holds byte data for both normal and encrypted entries. 
        }
        

        public class StyleEntry
        {
            //For users that customize the rules for parsing tags.
            //Tag pairs are Key:Value per OSM data.
            public long Id { get; set; }
            public string Name { get; set; }
            public string StyleSet { get; set; }
            public int MatchOrder { get; set; }
            public ICollection<StyleMatchRule> StyleMatchRules { get; set; }
            public bool IsGameElement { get; set; } // This tag should be used when asking for game areas, not just map tiles. Would let me use a single table for both again. Arguably, this could just be a second style to search that only contains elements of interest instead of a flag on one.
            public ICollection<StylePaint> PaintOperations { get; set; }
        }

        public class StyleMatchRule
        { 
            public long Id { get; set; }
            public string Key { get; set; } //the left side of the key:value tag
            public string Value { get; set; } //the right side of the key:value tag
            public string MatchType { get; set; } //Any, all, contains, not.
        }

        public class StylePaint
        {
            //For enabling layering of drawn elements
            public long Id { get; set; }
            //Layer note: it's still pretty possible that a lot of elements all get drawn on one layer, and only a few use multiple layers, like the outlined roads.
            public int LayerId { get; set; } //All paint operations should be done on their own layer, then all merged together? smaller ID are on top, bigger IDs on bottom. Layer 1 hides 2 hides 3 etc.
            //The below are copied from the original object.These get used to create the SKPaint/Pen/Brush object once at startup, then that paint object is used from then on.
            public string HtmlColorCode { get; set; } //This STARTS with the alpha color, and ImageSharp prefers it END with the alpha color. Handled by the PraxisMapTilesImageSharp library.
            public string FillOrStroke { get; set; }
            /// <summary>
            /// The width to draw the outline, in degrees. For Points, this is the diameter of the circle drawn.
            /// </summary>
            public float LineWidthDegrees { get; set; } 
            public string LinePattern { get; set; } //If 'solid' or blank, solid line. If not, split string into float[] on |
            public string FileName { get; set; } //A path to an image file that will be used as a repeating pattern. Null for solid colors.
            public double MinDrawRes { get; set; } = 0;//skip drawing this item if  resPerPixelX is below this value. (what doesn't draw zoomed in on OSM? name text?
            public double MaxDrawRes { get; set; } = 4; //skip drawing this item if resPerPixelX is above this value. (EX: tertiary roads don't draw at distant zooms
            public bool Randomize { get; set; } //if true, assign a random color at draw-time.
            public bool FromTag { get; set; } //if set, read the string for the color value at draw-time from the tag named here.
            public int FixedWidth { get; set; } = 0; //If > 0, always draw the element at this size. Intended for lines that should be equally visible at all zooms, like the Exercise Tracker path drawing.
        }

        public class ServerSetting
        {
            public int Id { get; set; } //just for a primary key
            public double NorthBound { get; set; }
            public double EastBound { get; set; }
            public double SouthBound { get; set; }
            public double WestBound { get; set; }
            public long NextAutoGeneratedAreaId { get; set; } = 1000000000000; //Track what our next ID is for generated areas without querying the table every time. Starts at 1 trillion.
            public long NextUserSuppliedAreaId { get; set; } = 2000000000000; //Track what our next ID is for user-generated areas without querying the table every time. Starts at 2 trillion.
            public string MessageOfTheDay { get; set; }
        }

        public class SlippyMapTile
        {
            public long SlippyMapTileId { get; set; } //int should be OK for a limited range game and/or big tiles. Making this long just to make sure.
            public string Values { get; set; } //x|y|zoom
            public byte[] TileData { get; set; } //png binary data.
            public string StyleSet { get; set; } // which style set was used to draw this tile. Allows for layers and varying data sets.
            public DateTime ExpireOn { get; set; } //assume that a tile needs regenerated if passed this timestamp. Set to CURRENT_TIMESTAMP to expire.
            [Column(TypeName = "geography")]
            [Required]
            public Geometry AreaCovered { get; set; } //This lets us find and expire map tiles if the data under them changes.
            public long GenerationID { get; set; } = 0; //How many times this tile has been redrawn. Used by client to know when to get new tiles.
        }

        //This is the new, 5th iteration of geography data storage for PraxisMapper.
        //All types can be stored in this one table, though some data will be applied on read
        //because the TagParser will determine it on-demand instead of storing changeable data.
        public class Place
        {
            public long Id { get; set; } //Internal primary key, don't pass this to clients.
            public long SourceItemID { get; set; } //Try to use PrivacyId instead of this where possible to avoid connecting players to locations.
            public int SourceItemType { get; set; } //1: node, 2: way, 3: relation
            [Column(TypeName = "geography")]
            [Required]
            public Geometry ElementGeometry { get; set; }
            public ICollection<PlaceTags> Tags { get; set; }
            public ICollection<PlaceData> PlaceData { get; set; } = new List<PlaceData>();
            [NotMapped]
            public bool IsGameElement { get; set; } //Gets determined by styles, shouldn't be a persisted property.
            [NotMapped]
            public string StyleName { get; set; } //Placeholder for TagParser to load up the name of the matching style for this element, but don't save it to the DB so we can change it on the fly.
            public Guid PrivacyId { get; set; } = Guid.NewGuid(); //Pass this Id to clients, so we can attempt to block attaching players to locations in the DB.
            public double DrawSizeHint { get; set; } //optimization. This is estimated pixels to draw at standard scale. Calculate your floor if you zoom in or out differently, and skip loading if its too small to see.

            public override string ToString()
            {
                return (SourceItemType == 3 ? "Relation " : SourceItemType == 2 ? "Way " : "Node ") +  SourceItemID.ToString() + " " + TagParser.GetName(Tags);
            }

            public Place Clone()
            {
                return (Place)this.MemberwiseClone();
            }
        }
         
        public class PlaceTags
        {
            public long Id { get; set; }
            public long SourceItemId { get; set; } 
            public int SourceItemType { get; set; } 
            public Place Place { get; set; }
            public string Key { get; set; }
            public string Value { get; set; }

            public override string ToString()
            {
                return Key + ":" + Value;
            }

            public bool Equals(PlaceTags other)
            {
                return other.Key == Key && other.Value == Value;
            }
        }

        public class AreaData
        {
            //for storing collection data server-side per plus-code
            public long Id { get; set; }
            public string PlusCode { get; set; } //can be any valid pluscode length 2-11
            public string DataKey { get; set; }
            public DateTime? Expiration { get; set; } //optional. If value is in the past, ignore this data.
            [Required]
            public Geometry GeoAreaIndex { get; set; } //PlusCode listed as a geometry object for index/search purposes.
            public byte[] IvData { get; set; } //Only set if data is encrypted.
            public byte[] DataValue { get; set; } //Holds byte data for both normal and encrypted entries. 
        }

        public class PlaceData
        {
            //for storing collection data server-side per existing map area. Join on that table to get geometry area.
            public long Id { get; set; } //internal primary key
            public long PlaceId { get; set; } //might not be necessary?
            public Place Place { get; set; }
            public string DataKey { get; set; }
            public DateTime? Expiration { get; set; } //optional. If value is in the past, ignore this data.
            public byte[] IvData { get; set; } //Only set if data is encrypted.
            public byte[] DataValue { get; set; } //Holds byte data for both normal and encrypted entries. 
        }

        public class StyleBitmap
        {
            public long Id { get; set; }
            public string Filename { get; set; }
            public byte[] Data { get; set; }
        }

        public class AntiCheatEntry
        { 
            public long Id { get; set; }
            public string filename { get; set; }
            public long revision { get; set; }
            public byte[] data { get; set; }
        }

        public class AuthenticationData
        {
            public long Id { get; set; }
            public string accountId { get; set; }
            public string loginPassword { get; set; } //The external password the account uses to get an AuthToken
            public string dataPassword { get; set; } //The internal password the account uses to read/write secure data entries for the player. Inaccesssible to the server owner.
            public byte[] dataIV { get; set; } // encryption values for dataPassword.
            public bool isAdmin { get; set; } = false;
            public DateTime? bannedUntil { get; set; }
        }
    }
}