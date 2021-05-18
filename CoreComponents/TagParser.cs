using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using OsmSharp.Tags;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static CoreComponents.DbTables;
using static CoreComponents.Singletons;

namespace CoreComponents
{
    public static class TagParser
    {
        public static List<TagParserEntry> styles;

        public static void Initialize()
        {
            //Load TPE entries from DB for app.
            var db = new PraxisContext();
            styles = db.TagParserEntries.Include(t => t.TagParserMatchRules).ToList();
            if (styles == null || styles.Count() == 0)
                styles = Singletons.defaultTagParserEntries;

            foreach (var s in styles)
                SetPaintForTPE(s);
        }

        public static void SetPaintForTPE(TagParserEntry tpe)
        {
            var paint = new SKPaint();
            paint.Color = SKColor.Parse(tpe.HtmlColorCode);
            if (tpe.FillOrStroke == "fill")
                paint.Style = SKPaintStyle.Fill;
            else
                paint.Style = SKPaintStyle.Stroke;
            paint.StrokeWidth = tpe.LineWidth;
            if (tpe.LinePattern != "solid")
            {
                float[] linesAndGaps = tpe.LinePattern.Split('|').Select(t => float.Parse(t)).ToArray();
                paint.PathEffect = SKPathEffect.CreateDash(linesAndGaps, 0);
                paint.StrokeCap = SKStrokeCap.Butt;
            }
            paint.StrokeJoin = SKStrokeJoin.Round;
            tpe.paint = paint;
        }

        public static SKPaint GetStyleForOsmWay(List<WayTags> tags)
        {
            if (tags == null || tags.Count() == 0)
            {
                var style = styles.Last(); //background color must be last if I want un-matched areas to be hidden, its own color if i want areas with no ways at all to show up.
                return style.paint;
            }

            foreach (var drawingRules in styles)
            {
                if (MatchOnTags(drawingRules, tags))
                {
                    return drawingRules.paint;
                }
            }
            return null;
        }

        public static bool MatchOnTags(TagParserEntry tpe, List<WayTags> tags)
        {
            int rulesCount = tpe.TagParserMatchRules.Count();
            bool[] rulesMatch = new bool[rulesCount];
            int matches = 0;
            bool OrMatched = false;

            //Step 1: check all the rules against these tags.
            for (var i = 0; i < tpe.TagParserMatchRules.Count(); i++) // var entry in tpe.TagParserMatchRules)
            {
                var entry = tpe.TagParserMatchRules.ElementAt(i);
                if (entry.Value == "*") //The Key needs to exist, but any value counts.
                {
                    if (tags.Any(t => t.Key == entry.Key))
                    {
                        matches++;
                        rulesMatch[i] = true;
                        continue;
                    }
                }

                switch (entry.MatchType)
                {
                    case "any":
                    case "or":
                    case "not": //Not uses the same check here, we check in step 2 if not was't matched. 
                        if (!tags.Any(t => t.Key == entry.Key))
                            continue;

                        var possibleValues = entry.Value.Split("|");
                        var actualValue = tags.Where(t => t.Key == entry.Key).Select(t => t.Value).FirstOrDefault();
                        if (possibleValues.Contains(actualValue))
                        {
                            matches++;
                            rulesMatch[i] = true;
                        }
                        break;
                    case "equals":
                        if (!tags.Any(t => t.Key == entry.Key))
                            continue;
                        if (tags.Where(t => t.Key == entry.Key).Select(t => t.Value).FirstOrDefault() == entry.Value)
                        {
                            matches++;
                            rulesMatch[i] = true;
                        }

                        break;
                    case "default":
                        //Always matches. Can only be on one entry, which is the last entry and the default color if nothing else matches.
                        return true;
                }
            }

            //Step 2: walk through and confirm we have all the rules correct or not for this set.
            //Now we have to check if we have 1 OR match, AND none of the mandatory ones failed, and no NOT conditions.
            int orCounter = 0;
            for (int i = 0; i < rulesMatch.Length; i++)
            {
                var rule = tpe.TagParserMatchRules.ElementAt(i);

                if (rulesMatch[i] == true && rule.MatchType == "not") //We don't want to match this!
                    return false;

                if (rulesMatch[i] == false && (rule.MatchType == "equals" || rule.MatchType == "any"))
                    return false;

                if (rule.MatchType == "or")
                {
                    orCounter++;
                    if (rulesMatch[i] == true)
                        OrMatched = true;
                }
            }

            //Now, we should have bailed out if any mandatory thing didn't match. Should only be on whether or not any of our Or checks passsed.
            if (orCounter == 0 || OrMatched == true)
                return true;

            return false;
        }

        public static void UpdateDbForStyleChange()
        {
            var db = new PraxisContext();
            foreach (var sw in db.StoredWays)
            {
                var paintStyle = GetStyleForOsmWay(sw.WayTags.ToList());
                if (sw.wayGeometry.GeometryType == "LinearRing" && paintStyle.Style == SKPaintStyle.Fill)
                {
                    var poly = factory.CreatePolygon((LinearRing)sw.wayGeometry);
                    sw.wayGeometry = poly;
                }
            }
        }

        public static List<WayTags> getFilteredTags(TagsCollectionBase rawTags)
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
                !t.Key.StartsWith("junction:") &&
                !t.Key.StartsWith("maxspeed") &&
                !t.Key.StartsWith("mtb:") &&
                !t.Key.StartsWith("nist:") &&
                !t.Key.StartsWith("not:") &&
                !t.Key.StartsWith("old_name:") &&
                !t.Key.StartsWith("parking:") &&
                !t.Key.StartsWith("payment:") &&
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
                !t.Key.StartsWith("was:")
                )
                .Select(t => new WayTags() { Key = t.Key, Value = t.Value }).ToList();
        }
    }
}
