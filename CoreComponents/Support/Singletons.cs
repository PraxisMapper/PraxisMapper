using NetTopologySuite;
using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Prepared;
using System.Collections.Generic;
using System.Linq;
using static CoreComponents.DbTables;

namespace CoreComponents
{
    public static class Singletons
    {
        public static List<string> relevantTourismValues = new List<string>() { "artwork", "attraction", "gallery", "museum", "viewpoint", "zoo" }; //The stuff we care about in the tourism category. Zoo and attraction are debatable. This doesn't seem to include Disney World.
        public static List<string> relevantTrailValues = new List<string>() { "path", "bridleway", "cycleway", "footway", "living_street" }; //The stuff we care about in the highway category for trails. Living Streets are nonexistant in the US.
        public static List<string> relevantRoadValues = new List<string>() { "motorway", "trunk", "primary", "secondary", "tertiary", "unclassified", "residential", "motorway_link", "trunk_link", "primary_link", "secondary_link", "tertiary_link", "service", "road" }; //The stuff we care about in the highway category for roads. A lot more options for this.

        public static GeometryFactory factory = NtsGeometryServices.Instance.CreateGeometryFactory(4326);
        public static PreparedGeometryFactory pgf = new PreparedGeometryFactory();
        public static bool SimplifyAreas = false;

        //TOD: make this the default list, then load the list from the DB? Works for PraxisMapper, but not Larry without some rules to parse tags.
        public static List<AreaType> areaTypes = new List<AreaType>() {
            //Areas here are for the original explore concept
            new AreaType() { AreaTypeId = 999, AreaName = "", OsmTags = "", HtmlColorCode = "545454"}, //the default background color. 0 causes insert to fail with an identity column
            new AreaType() { AreaTypeId = 1, AreaName = "water", OsmTags = "", HtmlColorCode = "0000B3"},
            new AreaType() { AreaTypeId = 2, AreaName = "wetland", OsmTags = "", HtmlColorCode = "0C4026"},
            new AreaType() { AreaTypeId = 3, AreaName = "park", OsmTags = "", HtmlColorCode = "00B300"},
            new AreaType() { AreaTypeId = 4, AreaName = "beach", OsmTags = "", HtmlColorCode = "D7B526" },
            new AreaType() { AreaTypeId = 5, AreaName = "university", OsmTags = "", HtmlColorCode = "F5F0DB" },
            new AreaType() { AreaTypeId = 6, AreaName = "natureReserve", OsmTags = "", HtmlColorCode = "124504" },
            new AreaType() { AreaTypeId = 7, AreaName = "cemetery", OsmTags = "", HtmlColorCode = "242420" },
            new AreaType() { AreaTypeId = 9, AreaName = "retail", OsmTags = "", HtmlColorCode = "EB63EB" },
            new AreaType() { AreaTypeId = 10, AreaName = "tourism", OsmTags = "", HtmlColorCode = "1999D1" },
            new AreaType() { AreaTypeId = 11, AreaName = "historical", OsmTags = "", HtmlColorCode = "B3B3B3" },
            new AreaType() { AreaTypeId = 12, AreaName = "trail", OsmTags = "", HtmlColorCode = "F0E68C" },//was 782E05
            //These areas are more for map tiles than gameplay
            new AreaType() { AreaTypeId = 13, AreaName = "admin", OsmTags = "",HtmlColorCode = "FF2020" }, //Though there could be some gameplay or leaderboarding about cities/states/countries visited using this.
            new AreaType() { AreaTypeId = 14, AreaName = "building", OsmTags = "", HtmlColorCode = "808080" },
            new AreaType() { AreaTypeId = 15, AreaName = "road", OsmTags = "", HtmlColorCode = "0D0D0D"},
            new AreaType() { AreaTypeId = 16, AreaName = "parking", OsmTags = "", HtmlColorCode = "0D0D0D" },
            //new AreaType() { AreaTypeId = 17, AreaName = "amenity", OsmTags = "", HtmlColorCode = "F2F090" }, //no idea what color this is
            //not yet completely certain i want to pull in amenities as their own thing. its sort of like retail but somehow more generic
            //maybe i need to add some more amenity entries to retail?

            new AreaType() { AreaTypeId = 100, AreaName = "generated", OsmTags = "", HtmlColorCode = "FFFFFF" }, //Static values right now for auto-generated areas for empty spaces.
        };

        public static ILookup<string, int> areaTypeReference = areaTypes.ToLookup(k => k.AreaName, v => v.AreaTypeId);
        public static ILookup<int, string> areaIdReference = areaTypes.ToLookup(k => k.AreaTypeId, v => v.AreaName);
        public static ILookup<int, string> areaColorReference = areaTypes.ToLookup(k => k.AreaTypeId, v => v.HtmlColorCode);

        public static List<List<Coordinate>> possibleShapes = new List<List<Coordinate>>() //When generating gameplay areas in empty Cell8s
        {
            new List<Coordinate>() { new Coordinate(0, 0), new Coordinate(.5, 1), new Coordinate(1, 0)}, //triangle.
            new List<Coordinate>() { new Coordinate(0, 0), new Coordinate(0, 1), new Coordinate(1, 1), new Coordinate(1, 0) }, //square.
            new List<Coordinate>() { new Coordinate(.2, 0), new Coordinate(0, .8), new Coordinate(.5, 1), new Coordinate(1, .8), new Coordinate(.8, 0) }, //roughly a pentagon.
            new List<Coordinate>() { new Coordinate(.5, 0), new Coordinate(0, .33), new Coordinate(0, .66), new Coordinate(.5, 1), new Coordinate(1, .66), new Coordinate(1, .33) }, //roughly a hexagon.
            //TODO: more shapes, ideally more interesting than simple polygons? Star? Heart? Arc?
        };

        public static List<Faction> defaultFaction = new List<Faction>()
        {
            new Faction() { FactionId = 1,  HtmlColor = "FF000088", Name = "Red Team" },
            new Faction() { FactionId = 2, HtmlColor = "00FF0088", Name = "Green Team" },
            new Faction() { FactionId = 3, HtmlColor = "87CEEB88", Name = "Blue Team" }, //Sky blue, versus deep blue that matches Water elements.
        };

        public static ILookup<long, string> teamColorReferenceLookupSkia = defaultFaction.ToLookup(k => k.FactionId, v => v.HtmlColor.Substring(6, 2) + v.HtmlColor.Substring(0, 6)); //needed to make the Dictionary<> correctly.

        //Unfinished, for future plans to have tags be defined by database entries instead of a single code block
        //public static List<TagParserEntry> defaultTagParserEntries = new List<TagParserEntry>()
        //{
        //new TagParserEntry() { name = "wetland", typeID = 2, matchRules =  "natural:wetland" }
        //};

        //Remember, each individual rule must be true in order for the entry to apply.
        //A * value is a wildcard that means any entry counts as a match for that value.
        //types vary:
        //any: one of the pipe delimited values in the value is present for the given key.
        //equals: the one specific value exists on that key
        //or: as long as one of the rules with or is true, accept this entry as a match. each Or entry should be treated as its own 'any' for parsing purposes. Other entries that aren't of the Or type must also be true.
        //default: always true.
        public static List<TagParserEntry> defaultTagParserEntries = new List<TagParserEntry>()
        {
            new TagParserEntry() { id = 1, name ="water", HtmlColorCode = "0000B3", FillOrStroke = "fill", LineWidth=1, LinePattern= "solid", TagParserMatchRules = new List<TagParserMatchRule>() {
                new TagParserMatchRule() {Key = "natural", Value = "water|strait|bay", MatchType = "or"},
                new TagParserMatchRule() {Key = "waterway", Value ="*", MatchType="or" },
                new TagParserMatchRule() {Key = "landuse", Value ="basin", MatchType="or" },
            }},
            new TagParserEntry() { id = 2, name ="wetland", HtmlColorCode = "0C4026", FillOrStroke = "fill", LineWidth=1, LinePattern= "solid", TagParserMatchRules = new List<TagParserMatchRule>() { new TagParserMatchRule() { Key = "natural", Value = "wetland", MatchType = "equals" }} },
            new TagParserEntry() { id = 3, name ="park", HtmlColorCode = "00B300", FillOrStroke = "fill", LineWidth=1, LinePattern= "solid", TagParserMatchRules = new List<TagParserMatchRule>() { new TagParserMatchRule() { Key = "leisure", Value = "park", MatchType = "equals" }} },
            new TagParserEntry() { id = 4, name ="beach", HtmlColorCode = "D7B526", FillOrStroke = "fill", LineWidth=1, LinePattern= "solid", TagParserMatchRules = new List<TagParserMatchRule>() {
                new TagParserMatchRule() {Key = "natural", Value = "beach", MatchType = "or" },
                new TagParserMatchRule() {Key = "leisure", Value="beach_resort", MatchType="or"}
            } },
            new TagParserEntry() { id = 5, name ="university", HtmlColorCode = "F5F0DB", FillOrStroke = "fill", LineWidth=1, LinePattern= "solid", TagParserMatchRules = new List<TagParserMatchRule>() { new TagParserMatchRule() { Key = "amenity", Value = "university|college", MatchType = "any" }} },
            new TagParserEntry() { id = 6, name ="natureReserve", HtmlColorCode = "124504", FillOrStroke = "fill", LineWidth=1, LinePattern= "solid", TagParserMatchRules = new List<TagParserMatchRule>() { new TagParserMatchRule() { Key = "leisure", Value = "nature_reserve", MatchType = "equals" }} },
            new TagParserEntry() { id = 7, name ="cemetery", HtmlColorCode = "242420",FillOrStroke = "fill", LineWidth=1, LinePattern= "solid", TagParserMatchRules = new List<TagParserMatchRule>() { new TagParserMatchRule() { Key = "landuse", Value = "cemetery", MatchType = "or" }, new TagParserMatchRule() {Key="amenity", Value="grave_yard", MatchType="or" } } },
            new TagParserEntry() { id = 8, name ="retail", HtmlColorCode = "EB63EB",FillOrStroke = "fill", LineWidth=1, LinePattern= "solid", TagParserMatchRules = new List<TagParserMatchRule>() { 
                new TagParserMatchRule() { Key = "landuse", Value = "retail", MatchType = "or"}, 
                new TagParserMatchRule() {Key="building", Value="retail", MatchType="or" }, 
                new TagParserMatchRule() {Key="shop", Value="*", MatchType="or" } 
            }},
            new TagParserEntry() { id = 9, name ="tourism", HtmlColorCode = "1999D1",FillOrStroke = "fill", LineWidth=1, LinePattern= "solid", TagParserMatchRules = new List<TagParserMatchRule>() { new TagParserMatchRule() { Key = "tourism", Value = "*", MatchType = "equals" }} },
            new TagParserEntry() { id = 10, name ="historical", HtmlColorCode = "B3B3B3",FillOrStroke = "fill", LineWidth=1, LinePattern= "solid", TagParserMatchRules = new List<TagParserMatchRule>() { new TagParserMatchRule() { Key = "historic", Value = "*", MatchType = "equals" }} },
            new TagParserEntry() { id = 11, name ="trail", HtmlColorCode = "F0E68C", FillOrStroke = "stroke", LineWidth=1, LinePattern= "solid",TagParserMatchRules = new List<TagParserMatchRule>() {
                new TagParserMatchRule() {Key="highway", Value="path|bridleway|cycleway|footway|living_street", MatchType="any"},
                new TagParserMatchRule() { Key="footway", Value="sidewalk|crossing", MatchType="not"}
            }},
            new TagParserEntry() { id = 12, name ="admin", HtmlColorCode = "FF2020",FillOrStroke = "stroke", LineWidth=2, LinePattern= "10|5", TagParserMatchRules = new List<TagParserMatchRule>() { new TagParserMatchRule() { Key = "boundary", Value = "administrative", MatchType = "equals" }} }, //We got rid of these for a reason, don't bring htem back. These are transparent.
            new TagParserEntry() { id = 13, name ="building", HtmlColorCode = "808080", FillOrStroke = "fill", LineWidth=1, LinePattern= "solid",TagParserMatchRules = new List<TagParserMatchRule>() { new TagParserMatchRule() { Key = "building", Value = "*", MatchType = "equals" }} },
            new TagParserEntry() { id = 14, name ="road", HtmlColorCode = "0D0D0D",FillOrStroke = "stroke", LineWidth=1, LinePattern= "solid", TagParserMatchRules = new List<TagParserMatchRule>()
            {
                new TagParserMatchRule() { Key = "highway", Value = "motorway|trunk|primary|secondary|tertiary|unclassified|residential|motorway_link|trunk_link|primary_link|secondary_link|tertiary_link|service|road", MatchType = "any" },
                new TagParserMatchRule() { Key="footway", Value="sidewalk|crossing", MatchType="not"}
            }},
            new TagParserEntry() { id = 15, name ="parking", HtmlColorCode = "0D0D0D",FillOrStroke = "fill", LineWidth=1, LinePattern= "solid", TagParserMatchRules = new List<TagParserMatchRule>() { new TagParserMatchRule() { Key = "amenity", Value = "parking", MatchType = "equals" }} },

            //New generic entries for mapping by color
            new TagParserEntry() { id = 16, name ="greenspace", HtmlColorCode = "90EE90",FillOrStroke = "fill", LineWidth=1, LinePattern= "solid", TagParserMatchRules = new List<TagParserMatchRule>() {
                new TagParserMatchRule() { Key = "landuse", Value = "grass|farmland|farmyard|meadow|vineyard|recreation_ground|village_green", MatchType = "or" },
                new TagParserMatchRule() { Key = "natural", Value = "scrub|heath|grassland", MatchType = "or" },
            }},
            new TagParserEntry() { id = 17, name ="alsobeach", HtmlColorCode = "D7B526",FillOrStroke = "fill", LineWidth=1, LinePattern= "solid", TagParserMatchRules = new List<TagParserMatchRule>() {
                new TagParserMatchRule() { Key = "natural", Value = "sand|shingle|dune", MatchType = "or" },
                new TagParserMatchRule() { Key = "surface", Value = "sand", MatchType = "or" }
            }},
            new TagParserEntry() { id = 18, name ="darkgreenspace", HtmlColorCode = "124504",FillOrStroke = "fill", LineWidth=1, LinePattern= "solid", TagParserMatchRules = new List<TagParserMatchRule>() {
                new TagParserMatchRule() { Key = "natural", Value = "wood", MatchType = "or" },
               new TagParserMatchRule() { Key = "landuse", Value = "forest|orchard", MatchType = "or" },
            }},
            new TagParserEntry() { id = 19, name ="otherspots", HtmlColorCode = "EB63EB",FillOrStroke = "fill", LineWidth=1, LinePattern= "solid", TagParserMatchRules = new List<TagParserMatchRule>() {
               new TagParserMatchRule() { Key = "landuse", Value = "industrial|commerical", MatchType = "any" },
            }},
            new TagParserEntry() { id = 20, name ="residential", HtmlColorCode = "009933",FillOrStroke = "fill", LineWidth=1, LinePattern= "solid", TagParserMatchRules = new List<TagParserMatchRule>() {
                new TagParserMatchRule() { Key = "landuse", Value = "residential", MatchType = "equals" },
            }},

            //NOTE: hiding elements of a given type will have to be done by drawing those elements in a transparent color (see the admin entry at id 12 for an example)
            new TagParserEntry() { id = 9999, name ="background", HtmlColorCode = "545454", FillOrStroke = "fill", LineWidth=1, LinePattern= "solid",TagParserMatchRules = new List<TagParserMatchRule>() { new TagParserMatchRule() { Key = "*", Value = "*", MatchType = "default" }} }
        };
    }
}
