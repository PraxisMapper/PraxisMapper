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
        //TODO: convert this to a namespace, make each entry its own class?
        public static GeometryFactory factory = NtsGeometryServices.Instance.CreateGeometryFactory(4326);
        public static PreparedGeometryFactory pgf = new PreparedGeometryFactory();
        public static bool SimplifyAreas = false;

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

        
        //A * value is a wildcard that means any value counts as a match for that key.
        //types vary:
        //any: one of the pipe delimited values in the value is present for the given key.
        //equals: the one specific value exists on that key. Slightly faster than Any when matching a single key, but trivially so.
        //or: as long as one of the rules with or is true, accept this entry as a match. each Or entry should be treated as its own 'any' for parsing purposes.
        //not: this rule must be FALSE for the style to be applied.
        //default: always true.
        public static List<TagParserEntry> defaultTagParserEntries = new List<TagParserEntry>()
        {
            new TagParserEntry() { id = 1, name ="water", HtmlColorCode = "0000B3", FillOrStroke = "fill", LineWidth=1, LinePattern= "solid", TagParserMatchRules = new List<TagParserMatchRule>() {
                new TagParserMatchRule() {Key = "natural", Value = "water|strait|bay", MatchType = "or"},
                new TagParserMatchRule() {Key = "waterway", Value ="*", MatchType="or" },
                new TagParserMatchRule() {Key = "landuse", Value ="basin", MatchType="or" },
                new TagParserMatchRule() {Key = "leisure", Value ="swimming_pool", MatchType="or" },
                new TagParserMatchRule() {Key = "place", Value ="sea", MatchType="or" }, //stupid Labrador sea value.
            }},
            new TagParserEntry() { id = 2, name ="wetland", HtmlColorCode = "0C4026", FillOrStroke = "fill", LineWidth=1, LinePattern= "solid", TagParserMatchRules = new List<TagParserMatchRule>() { new TagParserMatchRule() { Key = "natural", Value = "wetland", MatchType = "equals" }} },
            new TagParserEntry() { IsGameElement = true, id = 3, name ="park", HtmlColorCode = "C8FACC", FillOrStroke = "fill", LineWidth=1, LinePattern= "solid", TagParserMatchRules = new List<TagParserMatchRule>() { 
                new TagParserMatchRule() { Key = "leisure", Value = "park", MatchType = "or" },
                new TagParserMatchRule() { Key = "leisure", Value = "playground", MatchType = "or" },
            }},
            new TagParserEntry() { IsGameElement = true, id = 4, name ="beach", HtmlColorCode = "F5E9C6", FillOrStroke = "fill", LineWidth=1, LinePattern= "solid", TagParserMatchRules = new List<TagParserMatchRule>() {
                new TagParserMatchRule() {Key = "natural", Value = "beach", MatchType = "or" },
                new TagParserMatchRule() {Key = "leisure", Value="beach_resort", MatchType="or"}
            } },
            new TagParserEntry() { IsGameElement = true, id = 5, name ="university", HtmlColorCode = "FFFFE5", FillOrStroke = "fill", LineWidth=1, LinePattern= "solid", TagParserMatchRules = new List<TagParserMatchRule>() { new TagParserMatchRule() { Key = "amenity", Value = "university|college", MatchType = "any" }} },
            new TagParserEntry() { IsGameElement = true, id = 6, name ="natureReserve", HtmlColorCode = "124504", FillOrStroke = "fill", LineWidth=1, LinePattern= "solid", TagParserMatchRules = new List<TagParserMatchRule>() { new TagParserMatchRule() { Key = "leisure", Value = "nature_reserve", MatchType = "equals" }} },
            new TagParserEntry() {IsGameElement = true, id = 7, name ="cemetery", HtmlColorCode = "AACBAF",FillOrStroke = "fill", LineWidth=1, fileName="MapPatterns\\Landuse-cemetery.png", LinePattern= "solid", TagParserMatchRules = new List<TagParserMatchRule>() { new TagParserMatchRule() { Key = "landuse", Value = "cemetery", MatchType = "or" }, new TagParserMatchRule() {Key="amenity", Value="grave_yard", MatchType="or" } } },
            new TagParserEntry() { id = 8, name ="retail", HtmlColorCode = "FFD6D1",FillOrStroke = "fill", LineWidth=1, LinePattern= "solid", TagParserMatchRules = new List<TagParserMatchRule>() { 
                new TagParserMatchRule() { Key = "landuse", Value = "retail", MatchType = "or"}, 
                new TagParserMatchRule() {Key="building", Value="retail", MatchType="or" }, 
                new TagParserMatchRule() {Key="shop", Value="*", MatchType="or" } 
            }},
            new TagParserEntry() { IsGameElement = true, id = 9, name ="tourism", HtmlColorCode = "660033",FillOrStroke = "fill", LineWidth=1, LinePattern= "solid", TagParserMatchRules = new List<TagParserMatchRule>() { new TagParserMatchRule() { Key = "tourism", Value = "*", MatchType = "equals" }} },
            new TagParserEntry() { IsGameElement = true, id = 10, name ="historical", HtmlColorCode = "B3B3B3",FillOrStroke = "fill", LineWidth=1, LinePattern= "solid", TagParserMatchRules = new List<TagParserMatchRule>() { new TagParserMatchRule() { Key = "historic", Value = "*", MatchType = "equals" }} },
            new TagParserEntry() { id = 11, name ="trailFilled", HtmlColorCode = "F0E68C", FillOrStroke = "fill", LineWidth=1, LinePattern= "solid",TagParserMatchRules = new List<TagParserMatchRule>() {
                new TagParserMatchRule() {Key="highway", Value="path|bridleway|cycleway|footway|living_street", MatchType="any"},
                new TagParserMatchRule() { Key="footway", Value="sidewalk|crossing", MatchType="not"},
                new TagParserMatchRule() { Key="area", Value="yes", MatchType="equals"}
            }},
            new TagParserEntry() { IsGameElement = true, id = 12, name ="trail", HtmlColorCode = "F0E68C", FillOrStroke = "stroke", LineWidth=1, LinePattern= "solid",TagParserMatchRules = new List<TagParserMatchRule>() {
                new TagParserMatchRule() {Key="highway", Value="path|bridleway|cycleway|footway|living_street", MatchType="any"},
                new TagParserMatchRule() { Key="footway", Value="sidewalk|crossing", MatchType="not"}
            }},
            //Making admin transparent again.
            new TagParserEntry() { id = 13, name ="admin", HtmlColorCode = "00FF2020",FillOrStroke = "stroke", LineWidth=2, LinePattern= "10|5", TagParserMatchRules = new List<TagParserMatchRule>() { new TagParserMatchRule() { Key = "boundary", Value = "administrative", MatchType = "equals" }} },
            new TagParserEntry() { id = 14, name ="building", HtmlColorCode = "808080", FillOrStroke = "fill", LineWidth=1, LinePattern= "solid",TagParserMatchRules = new List<TagParserMatchRule>() { new TagParserMatchRule() { Key = "building", Value = "*", MatchType = "equals" }} },
            new TagParserEntry() { id = 15, name ="roadFilled", HtmlColorCode = "0D0D0D",FillOrStroke = "fill", LineWidth=1, LinePattern= "solid", TagParserMatchRules = new List<TagParserMatchRule>()
            {
                new TagParserMatchRule() { Key = "highway", Value = "motorway|trunk|primary|secondary|tertiary|unclassified|residential|motorway_link|trunk_link|primary_link|secondary_link|tertiary_link|service|road", MatchType = "any" },
                new TagParserMatchRule() { Key="footway", Value="sidewalk|crossing", MatchType="not"},
                new TagParserMatchRule() { Key="area", Value="yes", MatchType="equals"}
            }},
            new TagParserEntry() { id = 16, name ="road", HtmlColorCode = "0D0D0D",FillOrStroke = "stroke", LineWidth=1, LinePattern= "solid", TagParserMatchRules = new List<TagParserMatchRule>()
            {
                new TagParserMatchRule() { Key = "highway", Value = "motorway|trunk|primary|secondary|tertiary|unclassified|residential|motorway_link|trunk_link|primary_link|secondary_link|tertiary_link|service|road", MatchType = "any" },
                new TagParserMatchRule() { Key="footway", Value="sidewalk|crossing", MatchType="not"}
            }},
            new TagParserEntry() { id = 17, name ="parking", HtmlColorCode = "0D0D0D",FillOrStroke = "fill", LineWidth=1, LinePattern= "solid", TagParserMatchRules = new List<TagParserMatchRule>() { new TagParserMatchRule() { Key = "amenity", Value = "parking", MatchType = "equals" }} },

            //New generic entries for mapping by color
            new TagParserEntry() { id = 18, name ="greenspace", HtmlColorCode = "C8FACC",FillOrStroke = "fill", LineWidth=1, LinePattern= "solid", TagParserMatchRules = new List<TagParserMatchRule>() {
                new TagParserMatchRule() { Key = "landuse", Value = "grass|farmland|farmyard|meadow|vineyard|recreation_ground|village_green", MatchType = "or" },
                new TagParserMatchRule() { Key = "natural", Value = "scrub|heath|grassland", MatchType = "or" },
                new TagParserMatchRule() { Key = "leisure", Value = "garden", MatchType = "or" },
            }},
            new TagParserEntry() { id = 19, name ="alsobeach", HtmlColorCode = "D7B526",FillOrStroke = "fill", LineWidth=1, LinePattern= "solid", TagParserMatchRules = new List<TagParserMatchRule>() {
                new TagParserMatchRule() { Key = "natural", Value = "sand|shingle|dune|scree", MatchType = "or" },
                new TagParserMatchRule() { Key = "surface", Value = "sand", MatchType = "or" }
            }},
            new TagParserEntry() { id = 20, name ="darkgreenspace", HtmlColorCode = "ADD19E",FillOrStroke = "fill", LineWidth=1, LinePattern= "solid", TagParserMatchRules = new List<TagParserMatchRule>() {
                new TagParserMatchRule() { Key = "natural", Value = "wood", MatchType = "or" },
               new TagParserMatchRule() { Key = "landuse", Value = "forest|orchard", MatchType = "or" },
            }},
            new TagParserEntry() { id = 21, name ="otherspots", HtmlColorCode = "EB63EB",FillOrStroke = "fill", LineWidth=1, LinePattern= "solid", TagParserMatchRules = new List<TagParserMatchRule>() {
               new TagParserMatchRule() { Key = "landuse", Value = "industrial|commerical", MatchType = "any" },
            }},
            new TagParserEntry() { id = 22, name ="residential", HtmlColorCode = "009933",FillOrStroke = "fill", LineWidth=1, LinePattern= "solid", TagParserMatchRules = new List<TagParserMatchRule>() {
                new TagParserMatchRule() { Key = "landuse", Value = "residential", MatchType = "equals" },
            }},
            new TagParserEntry() { id = 23, name ="sidewalk", HtmlColorCode = "C0C0C0",FillOrStroke = "fill", LineWidth=1, LinePattern= "solid", TagParserMatchRules = new List<TagParserMatchRule>() {
                new TagParserMatchRule() { Key = "highway", Value = "pedestrian", MatchType = "equals" },
            }},
            //Transparent: we don't usually want to draw census boundaries
            new TagParserEntry() { id = 24, name ="censusbounds", HtmlColorCode = "00000000",FillOrStroke = "fill", LineWidth=1, LinePattern= "solid", TagParserMatchRules = new List<TagParserMatchRule>() {
                new TagParserMatchRule() { Key = "boundary", Value = "census", MatchType = "equals" },
            }},
            //Transparents: Explicitly things that don't help when drawn in one color.
            new TagParserEntry() { id = 25, name ="donotdraw", HtmlColorCode = "00000000",FillOrStroke = "fill", LineWidth=1, LinePattern= "solid", TagParserMatchRules = new List<TagParserMatchRule>() {
                new TagParserMatchRule() { Key = "place", Value = "locality|islet", MatchType = "any" },
            }},
            new TagParserEntry() { id = 25, name ="greyFill", HtmlColorCode = "AAAAAA",FillOrStroke = "fill", LineWidth=1, LinePattern= "solid", TagParserMatchRules = new List<TagParserMatchRule>() {
                new TagParserMatchRule() { Key = "man_made", Value = "breakwater", MatchType = "any" },
            }},

            //NOTE: hiding elements of a given type is done by drawing those elements in a transparent color
            //My default set wants to draw things that haven't yet been identified, so I can see what needs improvement or matched by a rule.
            new TagParserEntry() { id = 9999, name ="background", HtmlColorCode = "545454", FillOrStroke = "fill", LineWidth=1, LinePattern= "solid",TagParserMatchRules = new List<TagParserMatchRule>() { new TagParserMatchRule() { Key = "*", Value = "*", MatchType = "none" }} },

            new TagParserEntry() { id = 1000, name ="unmatched", HtmlColorCode = "00000000", FillOrStroke = "fill", LineWidth=1, LinePattern= "solid",TagParserMatchRules = new List<TagParserMatchRule>() { new TagParserMatchRule() { Key = "*", Value = "*", MatchType = "default" }} }
        };

        //Note: the last entry on this must be transparent, or else team-color maptiles will shade non-claimed areas with a background color.
        public static List<TagParserEntry> defaultTeamColors = new List<TagParserEntry>() 
        {
            new TagParserEntry() { id = 1, name ="Red Team", HtmlColorCode = "FF0000", FillOrStroke = "fill", LineWidth=1, LinePattern= "solid", TagParserMatchRules = new List<TagParserMatchRule>() {
                new TagParserMatchRule() {Key = "team", Value = "red", MatchType = "equals"},
            }},
            new TagParserEntry() { id = 2, name ="Green Team", HtmlColorCode = "00FF00", FillOrStroke = "fill", LineWidth=1, LinePattern= "solid", TagParserMatchRules = new List<TagParserMatchRule>() {
                new TagParserMatchRule() {Key = "team", Value = "green", MatchType = "equals"},
            }},
            new TagParserEntry() { id = 3, name ="Blue Team", HtmlColorCode = "0000FF", FillOrStroke = "fill", LineWidth=1, LinePattern= "solid", TagParserMatchRules = new List<TagParserMatchRule>() {
                new TagParserMatchRule() {Key = "team", Value = "blue", MatchType = "equals"},
            }},
            new TagParserEntry() { id = 1, name ="Unclaimed", HtmlColorCode = "00000000", FillOrStroke = "fill", LineWidth=1, LinePattern= "solid", TagParserMatchRules = new List<TagParserMatchRule>() {
                new TagParserMatchRule() {Key = "*", Value = "*", MatchType = "default"},
            }},
        };

        //Short counter by type. Ideally would like 4+ of each type, whatever types may end up being.
        //Using the type data from the original source for now.
        //Short rules:
        //Normal: any otherwise untagged area
        //water: around water areas.
        //ghost: graveyards.
        //Grass: parks.

        //grass 3
        //normal 3
        //flying 1
        //water 5
        //poison 2
        //electric 1
        //dark 3
        //bug 5
        //ghost 2
        //fairy 4
        //Fire 3
        //ground 2
        //psychic 3
        //rock 2
        //dragon 1
        //ice 1
        //fighting 1



        //26 total entries for Hypothesis/example usage.
        public static List<Creature> defaultCreatures = new List<Creature>() { 
            new Creature() { name ="Acafia", type1 ="Grass", type2 = "", imageName ="CreatureImages/acafia.png" },
            new Creature() { name ="Acceleret", type1 ="Normal", type2 = "Flying", imageName ="CreatureImages/acceleret.png" },
            new Creature() { name ="Aeolagio", type1 ="Water", type2 = "Poison", imageName ="CreatureImages/aeolagio.png" },
            new Creature() { name ="Bandibat", type1 ="Electric", type2 = "Dark", imageName ="CreatureImages/bandibat.png" },
            new Creature() { name ="Belamrine", type1 ="Bug", type2 = "Water", imageName ="CreatureImages/belmarine.png" },
            new Creature() { name ="Bojina", type1 ="Ghost", type2 = "", imageName ="CreatureImages/bojina.png" },
            new Creature() { name ="Caslot", type1 ="Dark", type2 = "Fairy", imageName ="CreatureImages/caslot.png" },
            new Creature() { name ="Cindigre", type1 ="Fire", type2 = "", imageName ="CreatureImages/cindigre.png" },
            new Creature() { name ="Curlsa", type1 ="Fairy", type2 = "", imageName ="CreatureImages/curlsa.png" },
            new Creature() { name ="Decicorn", type1 ="Poison", type2 = "", imageName ="CreatureImages/decicorn.png" },
            new Creature() { name ="Dauvespa", type1 ="Bug", type2 = "Ground", imageName ="CreatureImages/dauvespa.png" },
            new Creature() { name ="Drakella", type1 ="Water", type2 = "Grass", imageName ="CreatureImages/drakella.png" },
            new Creature() { name ="Eidograph", type1 ="Ghost", type2 = "Psychic", imageName ="CreatureImages/eidograph.png" },
            new Creature() { name ="Encanoto", type1 ="Psychic", type2 = "", imageName ="CreatureImages/encanoto.png" },
            new Creature() { name ="Faintrick", type1 ="Normal", type2 = "", imageName ="CreatureImages/faintrick.png" },
            new Creature() { name ="Galavena", type1 ="Rock", type2 = "Psychic", imageName ="CreatureImages/galavena.png" },
            new Creature() { name ="Vanitarch", type1 ="Bug", type2 = "Fairy", imageName ="CreatureImages/vanitarch.png" },
            new Creature() { name ="Grotuille", type1 ="Water", type2 = "Rock", imageName ="CreatureImages/grotuille.png" },
            new Creature() { name ="Gumbwaal", type1 ="Normal", type2 = "", imageName ="CreatureImages/gumbwaal.png" },
            new Creature() { name ="Mandragoon", type1 ="Grass", type2 = "Dragon", imageName ="CreatureImages/mandragoon.png" },
            new Creature() { name ="Ibazel", type1 ="Dark", type2 = "", imageName ="CreatureImages/ibazel.png" },
            new Creature() { name ="Makappa", type1 ="Ice", type2 = "Fire", imageName ="CreatureImages/makappa.png" },
            new Creature() { name ="Pyrobin", type1 ="Fire", type2 = "Fairy", imageName ="CreatureImages/pyrobin.png" },
            new Creature() { name ="Rocklantis", type1 ="Water", type2 = "Fighting", imageName ="CreatureImages/rocklantis.png" },
            new Creature() { name ="Strixlan", type1 ="Dark", type2 = "Flying", imageName ="CreatureImages/strixlan.png" },
            new Creature() { name ="Tinimer", type1 ="Bug", type2 = "", imageName ="CreatureImages/tinimer.png" },
            new Creature() { name ="Vaquerado", type1 ="Bug", type2 = "Ground", imageName ="CreatureImages/vaquerado.png" },
        };
    }
}
