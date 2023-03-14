using Microsoft.EntityFrameworkCore;
using OsmSharp;
using OsmSharp.Complete;
using OsmSharp.Tags;
using System;
using System.Collections.Generic;
using System.Linq;
using static PraxisCore.DbTables;

namespace PraxisCore {
    //NOTE:
    //per https://lists.openstreetmap.org/pipermail/talk/2008-February/023419.html
    // Mapnik uses at least 5 layers internally for its tiles. (roughly, in top to bottom order)
    //Labels (text), Fill, Features, Area, HillShading.
    //I would need better Fill-vs-area-vs-feature detection (I think I only really handle areas and features)

    //A * value is a wildcard that means any value counts as a match for that key. If the tag exists, its value is irrelevant. Cannot be used in NOT checks.
    //A * key is a rule that will not match based on tag values, as no key will == *. Used for backgrounds and special styles called up by name.
    //types vary:
    //any: one of the pipe delimited values in the value is present for the given key.
    //equals: the one specific value exists on that key. Slightly faster than Any when matching a single key, but trivially so.
    //or: as long as one of the rules with or is true, accept this entry as a match. each Or entry should be treated as its own 'any' for parsing purposes.
    //not: this rule must be FALSE for the style to be applied.
    //default: always true. Must be the last entry in the list of styles to check.

    /// <summary>
    /// Determines a Place's gameplay type and rules for drawing it on map tiles, along with tracking styles.
    /// </summary>
    public static class TagParser
    {
        public static StyleEntry defaultStyle; //background color must be last if I want un-matched areas to be hidden, its own color if i want areas with no ways at all to show up.
        public static Dictionary<string, byte[]> cachedBitmaps = new Dictionary<string, byte[]>(); //Icons for points separate from pattern fills, though I suspect if I made a pattern fill with the same size as the icon I wouldn't need this.
        public static Dictionary<string, Dictionary<string, StyleEntry>> allStyleGroups = new Dictionary<string, Dictionary<string, StyleEntry>>();

        private static IMapTiles MapTiles;

        /// <summary>
        /// Call once when the server or app is started. Loads all the styles and caches baseline data for later use.
        /// </summary>
        /// <param name="onlyDefaults">if true, skip loading the styles from the DB and use Praxismapper's defaults </param>
        public static void Initialize(bool onlyDefaults = false, IMapTiles mapTiles = null)
        {
            Singletons.defaultStyleEntries.Clear();
            allStyleGroups.Clear();

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
                    db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
                    db.ChangeTracker.AutoDetectChangesEnabled = false;
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
        /// Returns the style entry for the given AreaData row and style set.
        /// </summary>
        /// <param name="data"></param>
        /// <param name="styleSet"></param>
        /// <returns></returns>
        public static StyleEntry GetStyleEntry(List<AreaData> data, string styleSet = "mapTiles")
        {
            var areaDict = data.ToDictionary(k => k.DataKey, v => v.DataValue.ToUTF8String());
            return GetStyleEntry(areaDict, styleSet);
        }

        /// <summary>
        /// Returns the style entry for the given PlusCode and style set.
        /// </summary>
        /// <param name="plusCode"></param>
        /// <param name="styleSet"></param>
        /// <returns></returns>
        public static StyleEntry GetStyleEntry(string plusCode, string styleSet = "mapTiles")
        {
            var db = new PraxisContext();
            db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            var areaData = db.AreaData.Where(a => a.PlusCode == plusCode).ToList();
            var areaDict = areaData.ToDictionary(k => k.DataKey, v => v.DataValue.ToUTF8String());
            return GetStyleEntry(areaDict, styleSet);
        }

        /// <summary>
        /// returns the style entry for a given Place and style set.
        /// </summary>
        /// <param name="place"></param>
        /// <param name="styleSet"></param>
        /// <returns></returns>
        public static StyleEntry GetStyleEntry(DbTables.Place place, string styleSet = "mapTiles")
        {
            var allTags = new Dictionary<string, string>(place.Tags.Count + place.PlaceData.Count);
            foreach (var t in place.Tags)
                allTags.TryAdd(t.Key, t.Value);

            foreach (var d in place.PlaceData)
                if (d.IvData == null) //dont add encrypted entries.
                    allTags.TryAdd(d.DataKey, d.DataValue.ToUTF8String());

            return GetStyleEntry(allTags, styleSet);
        }

        /// <summary>
        /// Returns the style entry for a Place given its tags and a styleset to search against.
        /// </summary>
        /// <param name="tags">the tags attached to a Place to search. A list will be converted to a dictionary and error out if duplicate keys are present in the tags.</param>
        /// <param name="styleSet">the styleset with the rules for parsing elements</param>
        /// <returns>The StyleEntry that matches the rules and tags given, or a defaultStyle if none match.</returns>
        public static StyleEntry GetStyleEntry(ICollection<PlaceTags> tags, string styleSet = "mapTiles")
        {
            if (tags == null || tags.Count == 0)
                return defaultStyle;

            Dictionary<string, string> dTags = tags.ToDictionary(k => k.Key, v => v.Value);
            return GetStyleEntry(dTags, styleSet);
        }

        /// <summary>
        /// Returns the style used on a set of tags and a styleset to search against.
        /// </summary>
        /// <param name="tags">the tags attached to a Place to search</param>
        /// <param name="styleSet">the styleset with the rules for parsing elements</param>
        /// <returns>The StyleEntry that matches the rules and tags given, or a defaultStyle if none match.</returns>
        private static StyleEntry GetStyleEntry(Dictionary<string, string> tags, string styleSet = "mapTiles")
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
        /// Returns the style used on an CompleteOsnGeo object given its tags and a styleset to search against.
        /// </summary>
        /// <param name="tags">the tags attached to a CompleteGeo object to search</param>
        /// <param name="styleSet">the styleset with the rules for parsing elements</param>
        /// <returns>The TagParserEntry that matches the rules and tags given, or a defaultStyle if none match.</returns>
        public static StyleEntry GetStyleEntry(TagsCollectionBase tags, string styleSet = "mapTiles") //named correctly, this one runs from PbfReader.
        {
            var tempTags = tags.ToDictionary(k => k.Key, v => v.Value);
            return GetStyleEntry(tempTags, styleSet);
        }

        /// <summary>
        /// Returns the style used on an CompleteOsmGeo object and a styleset to search against.
        /// </summary>
        /// <param name="osm"></param>
        /// <param name="styleSet"></param>
        /// <returns></returns>
        public static StyleEntry GetStyleEntry(CompleteOsmGeo osm, string styleSet = "mapTiles")
        {
            var tempTags = osm.Tags.ToDictionary(k => k.Key, v => v.Value);
            return GetStyleEntry(tempTags, styleSet);
        }

        /// <summary>
        /// Returns the style used on an OsmGeo object given its tags and a styleset to search against.
        /// </summary>
        /// <param name="osm"></param>
        /// <param name="styleSet"></param>
        /// <returns></returns>
        public static StyleEntry GetStyleEntry(OsmGeo osm, string styleSet = "mapTiles")
        {
            var tempTags = osm.Tags.ToDictionary(k => k.Key, v => v.Value);
            return GetStyleEntry(tempTags, styleSet);
        }

        /// <summary>
        /// Returns the name of the style used on an CompleteOsmGeo object given its tags and a styleset to search against.
        /// </summary>
        /// <param name="osm"></param>
        /// <param name="styleSet"></param>
        /// <returns></returns>
        public static string GetStyleName(CompleteOsmGeo osm, string styleSet = "mapTiles")
        {
            var tempTags = osm.Tags.ToDictionary(k => k.Key, v => v.Value);
            return GetStyleEntry(tempTags, styleSet).Name;
        }

        /// <summary>
        /// Returns the name of the style used on an OsmGeo object given its tags and a styleset to search against.
        /// </summary>
        /// <param name="osm"></param>
        /// <param name="styleSet"></param>
        /// <returns></returns>
        public static string GetStyleName(OsmGeo osm, string styleSet = "mapTiles")
        {
            var tempTags = osm.Tags.ToDictionary(k => k.Key, v => v.Value);
            return GetStyleEntry(tempTags, styleSet).Name;
        }

        /// <summary>
        /// Returns the name of the style used on a Place given a styleset to search against.
        /// </summary>
        /// <param name="place"></param>
        /// <param name="styleSet"></param>
        /// <returns></returns>
        public static string GetStyleName(DbTables.Place place, string styleSet = "mapTiles")
        {
            return GetStyleEntry(place, styleSet).Name;
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
        /// Search the tags of a CompleteOsmGeo object for a name value
        /// </summary>
        /// <param name="tagsO">the tags to search</param>
        /// <returns>a Name value if one is found, or an empty string if not</returns>
        public static string GetName(ICompleteOsmGeo geo)
        {
            if (geo.Tags.Count == 0)
                return "";
            var retVal = geo.Tags.GetValue("name");
            if (retVal == null)
                retVal = "";

            return retVal;
        }

        /// <summary>
        /// Search the PlaceTags list for a name value
        /// </summary>
        /// <param name="tagsO">the tags to search</param>
        /// <returns>a Name value if one is found, or an empty string if not</returns>
        public static string GetName(ICollection<PlaceTags> tagsO)
        {
            if (tagsO.Count == 0)
                return "";
            var retVal = tagsO.FirstOrDefault(t => t.Key == "name");
            if (retVal == null)
                return "";

            return retVal.Value;
        }

        /// <summary>
        /// Returns the name of a Place by searching its tags.
        /// </summary>
        /// <param name="place"></param>
        /// <returns></returns>
        public static string GetName(DbTables.Place place)
        {
            return GetName(place.Tags);
        }

        /// <summary>
        /// Given a Place, searches both PlaceTags and PlaceData for an entry with a matching key and returns that value, or an empty string if not found.
        /// </summary>
        /// <param name="place"></param>
        /// <param name="key"></param>
        /// <returns></returns>
        public static string GetTagValue(DbTables.Place place, string key)
        {
            var tag = place.Tags.FirstOrDefault(t => t.Key == key);
            if (tag != null && tag.Value != null)
                return tag.Value;

            var data = place.PlaceData.FirstOrDefault(p => p.DataKey == key);
            if (data != null && data.DataValue != null && (data.Expiration == null || data.Expiration >= DateTime.UtcNow))
                return data.DataValue.ToUTF8String();

            return "";
        }

        /// <summary>
        /// Given a Place, searches both PlaceTags and PlaceData for an entry with a matching key and returns true or false, and supplies the tag's value as an out parameter.
        /// </summary>
        /// <param name="place"></param>
        /// <param name="key"></param>
        /// <param name="value"></param>
        /// <returns></returns>
        public static bool GetTagValue(DbTables.Place place, string key, out string value) {
            var tag = place.Tags.FirstOrDefault(t => t.Key == key);
            if (tag != null && tag.Value != null) {
                value = tag.Value;
                return true;
            }

            var data = place.PlaceData.FirstOrDefault(p => p.DataKey == key);
            if (data != null && data.DataValue != null && (data.Expiration == null || data.Expiration >= DateTime.UtcNow)) {
                value = data.DataValue.ToUTF8String();
                return true;
            }

            value = "";
            return false;
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
        /// Apply the rules for a styleset to a list of Places. Fills in some placeholder values that aren't persisted into the database.
        /// </summary>
        /// <param name="places">the list of Places to check</param>
        /// <param name="styleSet">the name of the style set to apply to the elements.</param>
        /// <returns>the list of places with the appropriate data filled in.</returns>
        public static List<DbTables.Place> ApplyTags(List<DbTables.Place> places, string styleSet)
        {
            foreach (var p in places)
            {
                ApplyTags(p, styleSet);
            }
            return places;
        }

        /// <summary>
        /// Apply the rules for a styleset to a Places. Fills in some placeholder values that aren't persisted into the database.
        /// </summary>
        /// <param name="place"></param>
        /// <param name="styleSet"></param>
        /// <returns></returns>
        public static DbTables.Place ApplyTags(DbTables.Place place, string styleSet)
        {
            var style = GetStyleEntry(place, styleSet);
            place.StyleName = style.Name;
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

        /// <summary>
        /// Adds a StyleEntry to the PraxisMapper database. Does not update existing entries with the same name. Expected to be used by plugins during Startup()
        /// </summary>
        /// <param name="s"></param>
        public static void InsertStyle(StyleEntry s)
        {
            var db = new PraxisContext();
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            if (allStyleGroups.TryGetValue(s.StyleSet, out var group))
            {
                if (group.TryAdd(s.Name, s)) {
                    db.StyleEntries.Add(s);
                }
            }
            else
            {
                var newDict = new Dictionary<string, StyleEntry> { { s.Name, s } };
                allStyleGroups.Add(s.StyleSet, newDict);
                db.StyleEntries.Add(s);
            }
            db.SaveChanges();
        }

        /// <summary>
        /// Adds a list of StyleEntries to the PraxisMapper database. Does not update existing entries with the same name. Expected to be used by plugins during Startup()
        /// </summary>
        /// <param name="styles"></param>
        public static void InsertStyles(List<StyleEntry> styles)
        {
            foreach (var style in styles)
                InsertStyle(style);
        }
    }
}
