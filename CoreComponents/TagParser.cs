using Microsoft.EntityFrameworkCore;
using OsmSharp.Tags;
using System;
using System.Collections.Generic;
using System.Linq;
using static PraxisCore.DbTables;

namespace PraxisCore
{
    //NOTE:
    //per https://lists.openstreetmap.org/pipermail/talk/2008-February/023419.html
    // Mapnik uses at least 5 layers internally for its tiles. (roughly, in top to bottom order)
    //Labels (text), Fill, Features, Area, HillShading.
    //I would need better area-vs-feature detection (OSMSharp has this,and I think I included that in my code)

    /// <summary>
    /// Determines an element's gameplay type and rules for drawing it on maptiles, along with tracking styles.
    /// </summary>
    public static class TagParser
    {
        public static StyleEntry defaultStyle; //background color must be last if I want un-matched areas to be hidden, its own color if i want areas with no ways at all to show up.
        public static Dictionary<string, byte[]> cachedBitmaps = new Dictionary<string, byte[]>(); //Icons for points separate from pattern fills, though I suspect if I made a pattern fill with the same size as the icon I wouldn't need this.
        public static Dictionary<string, Dictionary<string, StyleEntry>> allStyleGroups = new Dictionary<string, Dictionary<string, StyleEntry>>();

        private static IMapTiles MapTiles;

        //TODO: this might need an ID and then added to allStyleGroups.
        public static StyleEntry outlineStyle = new StyleEntry()
        {
            MatchOrder = 9998,
            Name = "outline",
            StyleSet = "special",
            PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "000000", FillOrStroke = "stroke", LineWidthDegrees=2, LinePattern= "solid", LayerId = 101 }
                },
            StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "*", Value = "*", MatchType = "none" }}
        };

        /// <summary>
        /// Call once when the server or app is started. Loads all the styles and caches baseline data for later use.
        /// </summary>
        /// <param name="onlyDefaults">if true, skip loading the styles from the DB and use Praxismapper's defaults </param>
        public static void Initialize(bool onlyDefaults = false, IMapTiles mapTiles = null)
        {
            Singletons.defaultStyleEntries.AddRange(Styles.adminBounds.style);
            Singletons.defaultStyleEntries.AddRange(Styles.mapTiles.style);
            Singletons.defaultStyleEntries.AddRange(Styles.outlines.style);
            Singletons.defaultStyleEntries.AddRange(Styles.paintTown.style);
            Singletons.defaultStyleEntries.AddRange(Styles.suggestedGameplay.style);
            Singletons.defaultStyleEntries.AddRange(Styles.suggestedMini.style);
            Singletons.defaultStyleEntries.AddRange(Styles.teamColor.style);

            MapTiles = mapTiles;
            List<StyleEntry> styles;

            if (onlyDefaults)
            {
                styles = Singletons.defaultStyleEntries;

                long i = 1;
                foreach (var s in styles)
                    foreach (var p in s.PaintOperations)
                        p.Id = i++;
            }
            else
            {
                try
                {
                    //Load TPE entries from DB for app.
                    var db = new PraxisContext();
                    styles = db.StyleEntries.Include(t => t.StyleMatchRules).Include(t => t.PaintOperations).ToList();
                    if (styles == null || styles.Count == 0)
                    {
                        styles = Singletons.defaultStyleEntries;
                        long i = 1;
                        foreach (var s in styles)
                        {
                            foreach (var p in s.PaintOperations)
                                p.Id = i++;
                            foreach (var p in s.StyleMatchRules)
                                p.Id = i++;
                        }
                    }

                    var bitmaps = db.StyleBitmaps.ToList();
                    foreach (var b in bitmaps)
                    {
                        cachedBitmaps.Add(b.Filename, b.Data); //Actual MapTiles dll will process the bitmap, we just load it here.
                    }

                }
                catch (Exception ex)
                {
                    //The database doesn't exist, use defaults.
                    Log.WriteLog("Error initializing:" + ex.Message, Log.VerbosityLevels.Errors);
                    styles = Singletons.defaultStyleEntries;
                }
            }

            var groups = styles.GroupBy(s => s.StyleSet);
            foreach (var g in groups)
                allStyleGroups.Add(g.Key, g.Select(gg => gg).OrderBy(gg => gg.MatchOrder).ToDictionary(k => k.Name, v => v));

            //Default style should be transparent, matches anything (assumed other styles were checked first)
            defaultStyle = new StyleEntry()
            {
                MatchOrder = 10000,
                Name = "unmatched",
                StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "00000000", FillOrStroke = "fill", LineWidthDegrees=1, LinePattern= "solid", LayerId = 101 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "*", Value = "*", MatchType = "default" }}
            };
            if (MapTiles != null)
                MapTiles.Initialize();
        }

        /// <summary>
        /// Returns the style to use on an element given its tags and a styleset to search against.
        /// </summary>
        /// <param name="tags">the tags attached to a Place to search. A list will be converted to a dictionary and error out if duplicate keys are present in the tags.</param>
        /// <param name="styleSet">the styleset with the rules for parsing elements</param>
        /// <returns>The StyleEntry that matches the rules and tags given, or a defaultStyle if none match.</returns>
        public static StyleEntry GetStyleForOsmWay(ICollection<PlaceTags> tags, string styleSet = "mapTiles")
        {
            if (tags == null || tags.Count == 0)
                return defaultStyle;

            Dictionary<string, string> dTags = tags.ToDictionary(k => k.Key, v => v.Value);
            return GetStyleForOsmWay(dTags, styleSet);
        }

        /// <summary>
        /// Returns the style to use on an element given its tags and a styleset to search against.
        /// </summary>
        /// <param name="tags">the tags attached to a Place to search</param>
        /// <param name="styleSet">the styleset with the rules for parsing elements</param>
        /// <returns>The StyleEntry that matches the rules and tags given, or a defaultStyle if none match.</returns>
        public static StyleEntry GetStyleForOsmWay(Dictionary<string, string> tags, string styleSet = "mapTiles")
        {
            if (tags == null || tags.Count == 0)
                return defaultStyle;

            foreach (var drawingRules in allStyleGroups[styleSet])
            {
                if (MatchOnTags(drawingRules.Value, tags))
                    return drawingRules.Value;
            }

            return defaultStyle;
        }

        /// <summary>
        /// Returns the style to use on an CompleteGeo object given its tags and a styleset to search against.
        /// </summary>
        /// <param name="tags">the tags attached to a CompleteGeo object to search</param>
        /// <param name="styleSet">the styleset with the rules for parsing elements</param>
        /// <returns>The TagParserEntry that matches the rules and tags given, or a defaultStyle if none match.</returns>
        public static StyleEntry GetStyleForOsmWay(TagsCollectionBase tags, string styleSet = "mapTiles")
        {
            var tempTags = tags.ToDictionary(k => k.Key, v => v.Value);
            return GetStyleForOsmWay(tempTags, styleSet);
        }

        /// <summary>
        /// Determines the name of the matching style for a CompleteGeo object
        /// </summary>
        /// <param name="tags">the tags attached to a completeGeo object</param>
        /// <param name="styleSet">the styleset to match against</param>
        /// <returns>The name of the style from the given styleSet that matches the CompleteGeo's tags</returns>
        public static string GetAreaType(TagsCollectionBase tags, string styleSet = "mapTiles")
        {
            var tempTags = tags.Select(t => new PlaceTags() { Key = t.Key, Value = t.Value }).ToList();
            return GetAreaType(tempTags);
        }

        /// <summary>
        /// Determines if the name of the matching style for a Place object
        /// </summary>
        /// <param name="tags">the tags attached to a Place object</param>
        /// <param name="styleSet">the styleset to match against</param>
        /// <returns>The name of the style from the given styleSet that matches the Place tags</returns>
        public static string GetAreaType(ICollection<PlaceTags> tags, string styleSet = "mapTiles")
        {
            if (tags == null || tags.Count == 0)
                return defaultStyle.Name;

            foreach (var drawingRules in allStyleGroups[styleSet])
                if (MatchOnTags(drawingRules.Value, tags))
                    return drawingRules.Value.Name;

            return defaultStyle.Name;
        }

        /// <summary>
        /// Determines if the name of the matching style for a Place object
        /// </summary>
        /// <param name="tags">the tags attached to a Place object</param>
        /// <param name="styleSet">the styleset to match against</param>
        /// <returns>The name of the style from the given styleSet that matches the Place tags</returns>
        public static string GetAreaType(Dictionary<string, string> tags, string styleSet = "mapTiles")
        {
            if (tags == null || tags.Count == 0)
                return defaultStyle.Name;

            foreach (var drawingRules in allStyleGroups[styleSet])
                if (MatchOnTags(drawingRules.Value, tags))
                    return drawingRules.Value.Name;

            return defaultStyle.Name;
        }

        /// <summary>
        /// Determine if this StyleEntry matches or not against a Place's tags.
        /// </summary>
        /// <param name="tpe">The StyleEntry to check</param>
        /// <param name="tags">the tags for a Place to use</param>
        /// <returns>true if this StyleEntry applies to this Place's tags, or false if it does not.</returns>
        public static bool MatchOnTags(StyleEntry tpe, ICollection<PlaceTags> tags)
        {
            //NOTE: this cast make this path slightly slower (.01ms), but I don't have to maintain 2 version of this functions full code.
            return MatchOnTags(tpe, tags.ToDictionary(k => k.Key, v => v.Value));
        }

        /// <summary>
        /// Determine if this StyleEntry matches or not against a Place's tags. Higher performance due to the use of a dictionary.
        /// </summary>
        /// <param name="tpe">the StyleEntry to match</param>
        /// <param name="tags">a Dictionary representing the place's tags</param>
        /// <returns>true if this rule entry matches against the place's tags, or false if not.</returns>
        public static bool MatchOnTags(StyleEntry tpe, Dictionary<string, string> tags)
        {
            bool OrMatched = false;
            int orRuleCount = 0;

            StyleMatchRule entry;

            //Step 1: check all the rules against these tags.
            //The * value is required for all the rules, so check it first.
            for (var i = 0; i < tpe.StyleMatchRules.Count; i++)
            {
                entry = tpe.StyleMatchRules.ElementAt(i);

                string actualvalue = "";
                bool isPresent = tags.TryGetValue(entry.Key, out actualvalue);

                switch (entry.MatchType)
                {
                    case "any":
                        if (!isPresent)
                            return false;

                        if (entry.Value == "*")
                            continue;

                        if (!entry.Value.Contains(actualvalue))
                            return false;
                        break;
                    case "or": //Or rules don't fail early, since only one of them needs to match. Otherwise is the same as ANY logic.
                        orRuleCount++;
                        if (!isPresent || OrMatched) //Skip checking the actual value if we already matched on an OR rule.
                            continue;

                        if (entry.Value == "*" || entry.Value.Contains(actualvalue))
                            OrMatched = true;
                        break;
                    case "not":
                        if (!isPresent)
                            continue;

                        if (entry.Value.Contains(actualvalue) || entry.Value == "*")
                            return false; //Not does not want to match this.
                        break;
                    case "equals": //for single possible values, EQUALS is slightly faster than ANY
                        if (entry.Value == "*" && isPresent)
                            continue;

                        if (!isPresent || actualvalue != entry.Value)
                            return false;
                        break;
                    case "none":
                        //never matches anything. Useful for background color or other special styles that need to exist but don't want to appear normally.
                        return false;
                    case "default":
                        //Always matches. Can only be on one entry, which is the last entry and the default color
                        return true;
                }
            }

            //Now, we should have bailed out if any mandatory thing didn't match. Now make sure that we either had 0 OR checks or matched on any OR rule provided.
            if (OrMatched || orRuleCount == 0)
                return true;

            //We did not match an OR clause, so this TPE is not a match.
            return false;
        }

        /// <summary>
        /// Filters out a bunch of tags PraxisMapper will not use. 
        /// </summary>
        /// <param name="rawTags"> The initial tags from a CompleteGeo object</param>
        /// <returns>A list of ElementTags with the undesired tags removed.</returns>
        public static List<PlaceTags> getFilteredTags(TagsCollectionBase rawTags)
        {
            return rawTags.Where(t =>
                t.Key != "source" &&
                !t.Key.StartsWith("addr:") &&
                !t.Key.StartsWith("alt_name:") &&
                !t.Key.StartsWith("brand") &&
                !t.Key.StartsWith("building:") &&
                !t.Key.StartsWith("change:") &&
                !t.Key.StartsWith("contact:") &&
                !t.Key.StartsWith("created_by") &&
                !t.Key.StartsWith("demolished:") &&
                !t.Key.StartsWith("destination:") &&
                !t.Key.StartsWith("disused:") &&
                !t.Key.StartsWith("email") &&
                !t.Key.StartsWith("fax") &&
                !t.Key.StartsWith("FIXME") &&
                !t.Key.StartsWith("generator:") &&
                !t.Key.StartsWith("gnis:") &&
                !t.Key.StartsWith("hgv:") &&
                !t.Key.StartsWith("import_uuid") &&
                !t.Key.StartsWith("is_in") &&
                !t.Key.StartsWith("junction:") &&
                !t.Key.StartsWith("maxspeed") &&
                !t.Key.StartsWith("mtb:") &&
                !t.Key.StartsWith("nist:") &&
                !t.Key.StartsWith("node") &&
                !t.Key.StartsWith("not:") &&
                !t.Key.StartsWith("old_name:") &&
                !t.Key.StartsWith("parking:") &&
                !t.Key.StartsWith("payment:") &&
                !t.Key.StartsWith("phone") &&
                !t.Key.StartsWith("name:") &&
                !t.Key.StartsWith("recycling:") &&
                !t.Key.StartsWith("ref:") &&
                !t.Key.StartsWith("reg_name:") &&
                !t.Key.StartsWith("roof:") &&
                !t.Key.StartsWith("source:") &&
                !t.Key.StartsWith("subject:") &&
                !t.Key.StartsWith("telephone") &&
                !t.Key.StartsWith("tiger:") &&
                !t.Key.StartsWith("turn:") &&
                !t.Key.StartsWith("was:") &&
                !t.Key.StartsWith("website")
                )
                .Select(t => new PlaceTags() { Key = t.Key, Value = t.Value }).ToList();
        }

        /// <summary>
        /// Search the tags of a CompleteGeo object for a name value
        /// </summary>
        /// <param name="tagsO">the tags to search</param>
        /// <returns>a Name value if one is found, or an empty string if not</returns>
        public static string GetPlaceName(TagsCollectionBase tagsO)
        {
            if (tagsO.Count == 0)
                return "";
            var retVal = tagsO.GetValue("name");
            if (retVal == null)
                retVal = "";

            return retVal;
        }

        /// <summary>
        /// Search the tags of a CompleteGeo object for a name value
        /// </summary>
        /// <param name="tagsO">the tags to search</param>
        /// <returns>a Name value if one is found, or an empty string if not</returns>
        public static string GetPlaceName(ICollection<PlaceTags> tagsO)
        {
            if (tagsO.Count == 0)
                return "";
            var retVal = tagsO.FirstOrDefault(t => t.Key == "name");
            if (retVal == null)
                return "";

            return retVal.Value;
        }

        /// <summary>
        /// Get a link to a Place's wikipedia page, if tagged with a wiki tag.
        /// </summary>
        /// <param name="element">the Place with tags to check</param>
        /// <returns>a link to the relevant Wikipedia page for an element, or an empty string if the element has no such tag.</returns>
        public static string GetWikipediaLink(DbTables.Place element)
        {
            var wikiTag = element.Tags.FirstOrDefault(t => t.Key == "wikipedia");
            if (wikiTag == null)
                return "";

            string[] splitValue = wikiTag.Value.Split(":");
            return "https://" + splitValue[0] + ".wikipedia.org/wiki/" + splitValue[1]; //TODO: check if special characters need replaced or encoded on this.
        }

        /// <summary>
        /// Apply the rules for a styleset to a list of places. Fills in some placeholder values that aren't persisted into the database.
        /// </summary>
        /// <param name="places">the list of Places to check</param>
        /// <param name="styleSet">the name of the style set to apply to the elements.</param>
        /// <returns>the list of places with the appropriate data filled in.</returns>
        public static List<DbTables.Place> ApplyTags(List<DbTables.Place> places, string styleSet)
        {
            foreach (var p in places)
            {
                var style = GetStyleForOsmWay(p.Tags, styleSet);
                p.GameElementName = style.Name;
                p.IsGameElement = style.IsGameElement;
            }
            return places;
        }

        public static DbTables.Place ApplyTags(DbTables.Place place, string styleSet)
        {
            var style = GetStyleForOsmWay(place.Tags, styleSet);
            place.GameElementName = style.Name;
            place.IsGameElement = style.IsGameElement;
            return place;
        }

        /// <summary>
        /// Pull a static, but practically randomized, color for an area based on the MD5 hash of its name. All identically named areas will get the same color.
        /// </summary>
        /// <param name="areaname">the name of the area to generate a color for</param>
        /// <returns>a string with the hex value for the color based on the area's name.</returns>
        public static string PickStaticColorForArea(string areaname)
        {
            var value = areaname.ToByteArrayUTF8();
            var hash = System.Security.Cryptography.MD5.HashData(value);
            string results = BitConverter.ToString(hash, 0, 3);
            return results;
        }

        //TODO: test these insert methods.
        public static void InsertStyle(StyleEntry s)
        {
            var db = new PraxisContext();
            db.StyleEntries.Add(s);
            db.SaveChanges();

            if (allStyleGroups.TryGetValue(s.StyleSet, out var group))
            {
                group.Add(s.Name, s);
            }
            else
            {
                var newDict = new Dictionary<string, StyleEntry> { { s.Name, s }};
                allStyleGroups.Add(s.StyleSet, newDict);
            }
        }

        public static void InsertStyleList(List<StyleEntry> styles)
        {
            foreach(var style in styles)
                InsertStyle(style);
        }
    }
}
