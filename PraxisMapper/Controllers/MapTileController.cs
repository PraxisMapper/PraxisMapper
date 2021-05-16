using CoreComponents;
using Google.OpenLocationCode;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using NetTopologySuite.Geometries;
using PraxisMapper.Classes;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using static CoreComponents.DbTables;
using static CoreComponents.Place;

namespace PraxisMapper.Controllers
{
    [Route("[controller]")]
    [ApiController]
    public class MapTileController : Controller
    {
        private readonly IConfiguration Configuration;
        private static MemoryCache cache;

        //TODO: consider playing with the SKSVGCanvas to see if SVG maptiles are faster/smaller/better in any ways
        //though the real delay is loading data from DB/disk, not drawing the loaded shapes.

        public MapTileController(IConfiguration configuration)
        {
            Configuration = configuration;

            if (cache == null && Configuration.GetValue<bool>("enableCaching") == true)
            {
                var options = new MemoryCacheOptions();
                options.SizeLimit = 1024;
                cache = new MemoryCache(options);
            }
        }

        [HttpGet]
        [Route("/[controller]/DrawSlippyTile/{x}/{y}/{zoom}/{layer}")]
        public FileContentResult DrawSlippyTile(int x, int y, int zoom, int layer)
        {
            //slippymaps don't use coords. They use a grid from -180W to 180E, 85.0511N to -85.0511S (they might also use radians, not degrees, for an additional conversion step)
            //with 2^zoom level tiles in place. so, i need to do some math to get a coordinate
            //X: -180 + ((360 / 2^zoom) * X)
            //Y: 8
            //Remember to invert Y to match PlusCodes going south to north.
            //BUT Also, PlusCodes have 20^(zoom/2) tiles, and Slippy maps have 2^zoom tiles, this doesn't even line up nicely.
            //Slippy Map tiles might just have to be their own thing.
            //I will also say these are 512x512 images.
            //TODO: should I set a longer timeout for these webtiles, and expire them when something in them gets updated?
            //This is much harder to detect for slippy maps, since I have to re-caculate the X and Y on a bunch of zoom levels for it.

            try
            {
                PerformanceTracker pt = new PerformanceTracker("DrawSlippyTile");
                string tileKey = x.ToString() + "|" + y.ToString() + "|" + zoom.ToString();
                var db = new PraxisContext();
                var existingResults = db.SlippyMapTiles.Where(mt => mt.Values == tileKey && mt.mode == layer).FirstOrDefault();
                if (existingResults == null || existingResults.SlippyMapTileId == null || existingResults.ExpireOn < DateTime.Now)
                {
                    //Create this entry
                    //requires a list of colors to use, which might vary per app

                    //MapTiles.GetSlippyResolutions(x, y, zoom, ou)
                    var n = Math.Pow(2, zoom);

                    var lon_degree_w = x / n * 360 - 180;
                    var lon_degree_e = (x + 1) / n * 360 - 180;
                    var lat_rads_n = Math.Atan(Math.Sinh(Math.PI * (1 - 2 * y / n)));
                    var lat_degree_n = lat_rads_n * 180 / Math.PI;
                    var lat_rads_s = Math.Atan(Math.Sinh(Math.PI * (1 - 2 * (y + 1) / n)));
                    var lat_degree_s = lat_rads_s * 180 / Math.PI;

                    var relevantArea = new GeoArea(lat_degree_s, lon_degree_w, lat_degree_n, lon_degree_e);
                    var areaHeightDegrees = lat_degree_n - lat_degree_s;
                    var areaWidthDegrees = 360 / n;

                    var filterSize = areaHeightDegrees / 128; //Height is always <= width, so use that divided by vertical resolution to get 1 pixel's size in degrees. Don't load stuff smaller than that.
                                                              //Test: set to 128 instead of 512: don't load stuff that's not 4 pixels ~.008 degrees at zoom 8.

                    var dataLoadArea = new GeoArea(relevantArea.SouthLatitude - ConstantValues.resolutionCell10, relevantArea.WestLongitude - ConstantValues.resolutionCell10, relevantArea.NorthLatitude + ConstantValues.resolutionCell10, relevantArea.EastLongitude + ConstantValues.resolutionCell10);
                    DateTime expires = DateTime.Now;
                    byte[] results = null;
                    switch (layer)
                    {
                        case 1: //Base map tile
                            //add some padding so we don't clip off points at the edge of a tile
                            var places = GetPlaces(dataLoadArea, includeGenerated: false, filterSize: filterSize); //NOTE: in this case, we want generated areas to be their own slippy layer, so the config setting is ignored here.
                            results = MapTiles.DrawAreaMapTileSlippySkia(ref places, relevantArea, areaHeightDegrees, areaWidthDegrees);
                            expires = DateTime.Now.AddYears(10); //Assuming you are going to manually update/clear tiles when you reload base data
                            break;
                        case 2: //PaintTheTown overlay. 
                            results = MapTiles.DrawPaintTownSlippyTileSkia(relevantArea, 2);
                            expires = DateTime.Now.AddMinutes(1); //We want this to be live-ish, but not overwhelming, so we cache this for 60 seconds.
                            break;
                        case 3: //MultiplayerAreaControl overlay.
                            results = MapTiles.DrawMPAreaMapTileSlippySkia(relevantArea, areaHeightDegrees, areaWidthDegrees);
                            expires = DateTime.Now.AddYears(10); //These expire when an area inside gets claimed now, so we can let this be permanent.
                            break;
                        case 4: //GeneratedMapData areas.
                            var places2 = GetGeneratedPlaces(dataLoadArea); //NOTE: this overlay doesn't need to check the config, since it doesn't create them, just displays them as their own layer.
                            results = MapTiles.DrawAreaMapTileSlippySkia(ref places2, relevantArea, areaHeightDegrees, areaWidthDegrees, true);
                            expires = DateTime.Now.AddYears(10); //again, assuming these don't change unless you manually updated entries.
                            break;
                        case 5: //Custom objects (scavenger hunt). Should be points loaded up, not an overlay?
                            //this isnt supported yet as a game mode.
                            break;
                        case 6: //Admin boundaries. Will need to work out rules on how to color/layer these. Possibly multiple layers, 1 per level? Probably not helpful for game stuff.
                            var placesAdmin = GetAdminBoundaries(dataLoadArea);
                            results = MapTiles.DrawAdminBoundsMapTileSlippy(ref placesAdmin, relevantArea, areaHeightDegrees, areaWidthDegrees);
                            expires = DateTime.Now.AddYears(10); //Assuming you are going to manually update/clear tiles when you reload base data
                            break;
                        case 7: //This might be the layer that shows game areas on the map. Draw outlines of them. Means games will also have a Geometry object attached to them for indexing.
                            //7 is currently testing for V4 data setup, drawing all OSM Ways on the map tile.
                            results = SlippyTestV4(x, y, zoom, 7);
                            //expires = DateTime.Now.AddMinutes(10); //Assuming you are going to manually update/clear tiles when you reload base data
                            break;
                        case 8: //This might be what gets called to load an actual game. The ID will be the game in question, so X and Y values could be ignored?
                            break;
                        case 9: //Draw Cell8 boundaries as lines. I thought about not saving these to the DB, but i can get single-ms time on reading an existing file instead of double-digit ms recalculating them.
                            results = MapTiles.DrawCell8GridLines(relevantArea);
                            break;
                        case 10: //Draw Cell10 boundaries as lines. I thought about not saving these to the DB, but i can get single-ms time on reading an existing file instead of double-digit ms recalculating them.
                            results = MapTiles.DrawCell10GridLines(relevantArea);
                            break;
                        case 11: //Admin bounds as a base layer. Countries only. Or states?
                            var placesAdminStates = GetAdminBoundaries(dataLoadArea);
                            placesAdminStates = placesAdminStates.Where(p => p.type == "admin4").ToList();
                            results = MapTiles.DrawAdminBoundsMapTileSlippy(ref placesAdminStates, relevantArea, areaHeightDegrees, areaWidthDegrees);
                            expires = DateTime.Now.AddYears(10); //Assuming you are going to manually update/clear tiles when you reload base data
                            break;
                    }
                    if (existingResults == null)
                        db.SlippyMapTiles.Add(new SlippyMapTile() { Values = tileKey, CreatedOn = DateTime.Now, mode = layer, tileData = results, ExpireOn = expires, areaCovered = Converters.GeoAreaToPolygon(dataLoadArea) });
                    else
                    {
                        existingResults.CreatedOn = DateTime.Now;
                        existingResults.ExpireOn = expires;
                        existingResults.tileData = results;
                    }
                    db.SaveChanges();
                    pt.Stop(tileKey + "|" + layer);
                    return File(results, "image/png");
                }

                pt.Stop(tileKey + "|" + layer);
                return File(existingResults.tileData, "image/png");
            }
            catch (Exception ex)
            {
                ErrorLogger.LogError(ex);
                return null;
            }
        }

        [HttpGet]
        [Route("/[controller]/CheckTileExpiration/{PlusCode}/{mode}")]
        public string CheckTileExpiration(string PlusCode, int mode) //For simplicity, maptiles expire after the Date part of a DateTime. Intended for base tiles.
        {
            //I pondered making this a boolean, but the client needs the expiration date to know if it's newer or older than it's version. Not if the server needs to redraw the tile. That happens on load.
            //I think, what I actually need, is the CreatedOn, and if it's newer than the client's tile, replace it.
            PerformanceTracker pt = new PerformanceTracker("CheckTileExpiration");
            var db = new PraxisContext();
            var mapTileExp = db.MapTiles.Where(m => m.PlusCode == PlusCode && m.mode == mode).Select(m => m.ExpireOn).FirstOrDefault();
            pt.Stop();
            return mapTileExp.ToShortDateString();
        }

        [HttpGet]
        [Route("/[controller]/DrawPath")]
        public byte[] DrawPath()
        {
            //NOTE: URL limitations block this from being a usable REST style path, so this one may require reading data bindings from the body instead
            string path = new System.IO.StreamReader(Request.Body).ReadToEnd();
            return MapTiles.DrawUserPath(path);
        }

        [HttpGet]
        [Route("/[controller]/DrawSlippyTileV4Test/{x}/{y}/{zoom}/{layer}")]
        public byte[] SlippyTestV4(int x, int y, int zoom, int layer)
        {
            //this would be part of app startup if i go with this.
            foreach (var tpe in Singletons.defaultTagParserEntries)
            {
                SetPaintForTPE(tpe);
            }

            Random r = new Random();
            //FileStream fs = new FileStream(filename, FileMode.Open);
            //get location in lat/long format.
            //Delaware is 2384, 3138,13
            //Cedar point, where I had issues before, is 35430/48907/17
            //int x = 2384;
            //int y = 3138;
            //int zoom = 13;
            //int x = 35430;
            //int y = 48907;
            //int zoom = 17;

            var n = Math.Pow(2, zoom);

            var lon_degree_w = x / n * 360 - 180;
            var lon_degree_e = (x + 1) / n * 360 - 180;
            var lat_rads_n = Math.Atan(Math.Sinh(Math.PI * (1 - 2 * y / n)));
            var lat_degree_n = lat_rads_n * 180 / Math.PI;
            var lat_rads_s = Math.Atan(Math.Sinh(Math.PI * (1 - 2 * (y + 1) / n)));
            var lat_degree_s = lat_rads_s * 180 / Math.PI;

            var relevantArea = new GeoArea(lat_degree_s, lon_degree_w, lat_degree_n, lon_degree_e);
            var areaHeightDegrees = lat_degree_n - lat_degree_s;
            var areaWidthDegrees = 360 / n;

            //Get relevant data from PBF 
            //(can change that to other data source later)
            //var elements = GetData((float)relevantArea.WestLongitude, (float)relevantArea.EastLongitude, (float)relevantArea.SouthLatitude, (float)relevantArea.NorthLatitude, fs);
            var db = new PraxisContext();
            //db.ChangeTracker.LazyLoadingEnabled = false;
            var geo = Converters.GeoAreaToPolygon(relevantArea);
            var drawnItems = db.StoredWays.Include(c => c.WayTags).Where(w => w.wayGeometry.Intersects(geo)).OrderByDescending(w => w.wayGeometry.Area).ThenByDescending(w => w.wayGeometry.Length).ToList();

            var styles = Singletons.defaultTagParserEntries;

            //TEST LOGIC:
            //take each relation, complete it, draw it in a random color.
            //Take each area, complete it, draw it in a random color.
            //Nodes that meet standalone drawing criteria get a solid white dot.

            //baseline image data stuff
            int imageSizeX = 512;
            int imageSizeY = 512;
            double degreesPerPixelX = relevantArea.LongitudeWidth / imageSizeX;
            double degreesPerPixelY = relevantArea.LatitudeHeight / imageSizeX;
            SKBitmap bitmap = new SKBitmap(512, 512, SKColorType.Rgba8888, SKAlphaType.Premul);
            SKCanvas canvas = new SKCanvas(bitmap);
            var bgColor = new SKColor();
            SKColor.TryParse("00FFFFFF", out bgColor);
            canvas.Clear(bgColor);
            canvas.Scale(1, -1, imageSizeX / 2, imageSizeY / 2);
            SKPaint paint = new SKPaint();
            SKColor color = new SKColor();
            //var nodes = elements.Where(e => e.Type == OsmSharp.OsmGeoType.Node).Select(e => (OsmSharp.Node)e).ToLookup(k => k.Id);
            //var rels = elements.Where(e => e.Type == OsmSharp.OsmGeoType.Relation).Select(e => (OsmSharp.Relation)e).ToList();
            //foreach (var r2 in rels)
            //{
            //    //Draw this?
            //}
            //var ways = elements.Where(e => e.Type == OsmSharp.OsmGeoType.Way).Select(e => (OsmSharp.Way)e).ToList(); //How to order these?
            //var mapDatas = new List<MapData>();
            foreach (var w in drawnItems)
            {

                //TODO: assign each Place a Style, or maybe its own Paint element, so I can look that up once and reuse the results.
                //instead of looping over each Style every time.
                var tempList = new List<WayTags>();
                if (w.WayTags != null)
                    tempList = w.WayTags.ToList();
                paint = GetStyleForOsmWay(tempList, ref styles); 
                var path = new SKPath();
                switch (w.wayGeometry.GeometryType)
                {
                    case "Polygon":
                        path.AddPoly(Converters.PolygonToSKPoints(w.wayGeometry, relevantArea, degreesPerPixelX, degreesPerPixelY));
                        //paint.Style = SKPaintStyle.Fill;
                        //paint.Color = new SKColor((byte)r.Next(0, 255), (byte)r.Next(0, 255), (byte)r.Next(0, 255));
                        canvas.DrawPath(path, paint);
                        break;
                    case "MultiPolygon":
                        //paint.Color = new SKColor((byte)r.Next(0, 255), (byte)r.Next(0, 255), (byte)r.Next(0, 255));
                        //paint.Style = SKPaintStyle.Fill;
                        foreach (var p in ((MultiPolygon)w.wayGeometry).Geometries)
                        {
                            var path2 = new SKPath();
                            path2.AddPoly(Converters.PolygonToSKPoints(p, relevantArea, degreesPerPixelX, degreesPerPixelY));
                            canvas.DrawPath(path2, paint);
                        }
                        break;
                    case "LineString":
                        //paint.Style = SKPaintStyle.Stroke;
                        //paint.Color = new SKColor((byte)r.Next(0, 255), (byte)r.Next(0, 255), (byte)r.Next(0, 255));
                        var points = Converters.PolygonToSKPoints(w.wayGeometry, relevantArea, degreesPerPixelX, degreesPerPixelY);
                        for (var line = 0; line < points.Length - 1; line++)
                            canvas.DrawLine(points[line], points[line + 1], paint);
                        break;
                    case "MultiLineString":
                        foreach (var p in ((MultiLineString)w.wayGeometry).Geometries)
                        {
                            //paint.Style = SKPaintStyle.Stroke;
                            //paint.Color = new SKColor((byte)r.Next(0, 255), (byte)r.Next(0, 255), (byte)r.Next(0, 255));
                            var points2 = Converters.PolygonToSKPoints(p, relevantArea, degreesPerPixelX, degreesPerPixelY);
                            for (var line = 0; line < points2.Length - 1; line++)
                                canvas.DrawLine(points2[line], points2[line + 1], paint);
                        }
                        break;
                    case "Point":
                        //paint.Style = SKPaintStyle.Fill;
                        var circleRadius = (float)(.000125 / degreesPerPixelX / 2); //I want points to be drawn as 1 Cell10 in diameter.
                        var convertedPoint = Converters.PolygonToSKPoints(w.wayGeometry, relevantArea, degreesPerPixelX, degreesPerPixelY);
                        //paint.Color = new SKColor((byte)r.Next(0, 255), (byte)r.Next(0, 255), (byte)r.Next(0, 255));
                        canvas.DrawCircle(convertedPoint[0], circleRadius, paint);
                        break;
                }

                //draw all the WayData entry.
                //var path = new SKPath();
                //path.AddPoly(Converters.PolygonToSKPoints(place.place, relevantArea, degreesPerPixelX, degreesPerPixelY));
                //paint.Style = SKPaintStyle.Fill;
                //paint.Color = new SKColor((byte)r.Next(0, 255), (byte)r.Next(0, 255), (byte)r.Next(0, 255));
                //canvas.DrawPath(path, paint);
            }

            //Save object to .png file.
            var ms = new MemoryStream();
            var skms = new SKManagedWStream(ms);
            bitmap.Encode(skms, SKEncodedImageFormat.Png, 100);
            var results = ms.ToArray();
            skms.Dispose(); ms.Close(); ms.Dispose();
            //File.WriteAllBytes("test.png", results);
            return results;
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
            tpe.paint = paint;
        }

        public static SKPaint GetStyleForOsmWay(List<WayTags> tags, ref List<TagParserEntry> styles)
        {
            //TODO: make this faster, i should make the SKPaint objects once at startup, then return those here instead of generating them per thing I need to draw.

            //Drawing order: 
            //these styles should be drawing in Descending Index order (Continents are 88, then countries and states, then roads, etc etc.... airport taxiways are 1).

            //Search logic:
            //SourceLayer is the general name of the category of stuff to draw. Transportation, for example.
            //Filter has some rules on what things this specific entry should apply to.
            //Most specific filter should apply, but if its the category than use the base category values.

            //I might just want to write my own rules, and possibly make then DB entries? simplify them down to just Skia rules?
            //also, if it doesn't find any rules, draw background color.

            //now, attempt to see if these tags apply in any way to our style list.
            //foreach (var rules in styleLayerList.Layers)
            //{
            //    //get rules
            //    var filterRules = rules.Filter;
            //    var areaName = rules.SourceLayer;
            //}
            if (tags == null || tags.Count() == 0)
            {
                var style = styles.Last(); //background must be last.
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
                    case "not": //Not uses the same check here, we check in step 2 if not was't matched. //I think not uses the pipe split. TODO doublecheck.
                        if (!tags.Any(t => t.Key == entry.Key)) //Not requires this tag to explicitly exist, possibly. //entry.MatchType != "not" &&  
                            continue;

                        var possibleValues = entry.Value.Split("|");
                        var actualValue = tags.Where(t => t.Key == entry.Key).Select(t => t.Value).FirstOrDefault();
                        if (possibleValues.Contains(actualValue))
                        {
                            matches++;
                            rulesMatch[i] = true;
                            //if (entry.MatchType == "or") //Othewise, Or and Any logic are the same.
                            // OrMatched = true; 
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

            //if (rulesMatch.All(r => r == true))
            //return true;

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

            //Now, we should have bailed out if any mandatory thing didn't match right. Should only be on whether or not any of our Or checks passsed.
            if (orCounter == 0 || OrMatched == true)
                return true;

            return false;
        }
    }
}
