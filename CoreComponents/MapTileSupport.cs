using Google.OpenLocationCode;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using PraxisCore.Support;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using static PraxisCore.ConstantValues;
using static PraxisCore.DbTables;
using static PraxisCore.Place;

namespace PraxisCore
{
    /// <summary>
    /// All functions related to map tiles that don't directly involve the drawing engine itself.
    /// </summary>

    //These functions are ones related to MapTiles that Don't actually do any drawing, and don't need duplicated between the multiple drawing engines.
    public static class MapTileSupport
    {
        public static IMapTiles MapTiles; //This needs set at startup.

        public static int SlippyTileSizeSquare = 512;
        public static double GameTileScale = 4;
        public static double BufferSize = ConstantValues.resolutionCell10;

        /// <summary>
        /// A code reference for how big an image would be using 11-character PlusCodes for pixels, multiplied by GameTileScale (default 2)
        /// </summary>
        /// <param name="code">the code provided to determine image size</param>
        /// <param name="X">out param for image width</param>
        /// <param name="Y">out param for image height</param>
        public static void GetPlusCodeImagePixelSize(string code, out int X, out int Y)
        {
            switch (code.Length)
            {
                case 10:
                    X = 4;
                    Y = 5;
                    break;
                case 8:
                    X = 80;
                    Y = 100;
                    break;
                case 6:
                    X = 1600;
                    Y = 2000;
                    break;
                case 4: //This is likely to use up more RAM than most PCs have, especially at large scales.
                    X = 32000;
                    Y = 40000;
                    break;
                default:
                    X = 0;
                    Y = 0;
                    break;
            }
            X = (int)(X * MapTileSupport.GameTileScale);
            Y = (int)(Y * MapTileSupport.GameTileScale);
        }

        public static void GetPlusCodeImagePixelSize(ReadOnlySpan<char> code, out int X, out int Y)
        {
            switch (code.Length)
            {
                case 10:
                    X = 4;
                    Y = 5;
                    break;
                case 8:
                    X = 80;
                    Y = 100;
                    break;
                case 6:
                    X = 1600;
                    Y = 2000;
                    break;
                case 4: //This is likely to use up more RAM than most PCs have, especially at large scales.
                    X = 32000;
                    Y = 40000;
                    break;
                default:
                    X = 0;
                    Y = 0;
                    break;
            }
            X = (int)(X * MapTileSupport.GameTileScale);
            Y = (int)(Y * MapTileSupport.GameTileScale);
        }

        public static byte[] DrawPlusCode(ReadOnlySpan<char> area, string styleSet = "mapTiles")
        {
            //This might be a cleaner version of my V4 function, for working with CellX sized tiles..
            //This will draw at a Cell11 resolution automatically.
            //Split it into a few functions.
            //then get all the area

            var info = new ImageStats(area);
            var places = GetPlaces(info);
            var paintOps = GetPaintOpsForPlaces(places, styleSet, info);
            return MapTiles.DrawAreaAtSize(info, paintOps);
        }

        /// <summary>
        /// Get the image for a PlusCode. Can optionally draw in a specific style set.
        /// </summary>
        /// <param name="area">the PlusCode string to draw. Can be 6-11 digits long</param>
        /// <param name="styleSet">the TagParser style set to use when drawing</param>
        /// <param name="doubleRes">treat each Cell11 contained as 2x2 pixels when true, 1x1 when not.</param>
        /// <returns></returns>
        public static byte[] DrawPlusCode(string area, string styleSet = "mapTiles")
        {
            //This might be a cleaner version of my V4 function, for working with CellX sized tiles..
            //This will draw at a Cell11 resolution automatically.
            //Split it into a few functions.
            //then get all the area

            var info = new ImageStats(area);
            var places = GetPlaces(info);
            var paintOps = GetPaintOpsForPlaces(places, styleSet, info);
            return MapTiles.DrawAreaAtSize(info, paintOps);
        }

        /// <summary>
        /// Get the image for a PlusCode. Can optionally draw in a specific style set.
        /// </summary>
        /// <param name="area">the PlusCode string to draw. Can be 6-11 digits long</param>
        /// <param name="paintOps">the list of paint operations to run through for drawing</param>
        /// <returns>a byte array for the png file of the pluscode image file</returns>
        public static byte[] DrawPlusCode(string area, List<CompletePaintOp> paintOps)
        {
            var info = new ImageStats(area);
            return MapTiles.DrawAreaAtSize(info, paintOps);
        }

        public static byte[] DrawPlusCode(ReadOnlySpan<char> area, List<CompletePaintOp> paintOps)
        {
            var info = new ImageStats(area);
            return MapTiles.DrawAreaAtSize(info, paintOps);
        }

        //future version of GetPaintOps that should let me consolidate functions in the future
        private static void GetPaintOpsInner(ref List<CompletePaintOp> list, DbTables.Place place, ICollection<StylePaint> midOps, ImageStats stats, string dataKey = "")
        {
            foreach (var po in midOps)
                if (stats.degreesPerPixelX < po.MaxDrawRes
                    && stats.degreesPerPixelX > po.MinDrawRes //dppX should be between max and min draw range.
                    && !(po.HtmlColorCode.Length == 8 && po.HtmlColorCode.StartsWith("00")) //color is NOT transparent.
                    )
                    list.Add(new CompletePaintOp(
                        place.ElementGeometry,
                        place.DrawSizeHint,
                        po,
                        po.FromTag ? TagParser.GetTagValue(place, dataKey) : "",
                        po.FixedWidth == 0 ? po.LineWidthDegrees * stats.pixelsPerDegreeX : po.FixedWidth)
                    );
        }

        private static void GetPaintOps(ref List<CompletePaintOp> list, double areaSize, Geometry elementGeometry, ICollection<StylePaint> midOps, ImageStats stats)
        {
            foreach (var po in midOps)
                if (stats.degreesPerPixelX < po.MaxDrawRes
                    && stats.degreesPerPixelX > po.MinDrawRes //dppX should be between max and min draw range.
                    && !(po.HtmlColorCode.Length == 8 && po.HtmlColorCode.StartsWith("00")) //color is NOT transparent.
                    )
                    list.Add(new CompletePaintOp(elementGeometry, areaSize, po, "", po.FixedWidth == 0 ? po.LineWidthDegrees * stats.pixelsPerDegreeX :  po.FixedWidth));
        }

        //and this is the future replacement version of GetPAintOpsForPlaces that should consolidate some of the calls. Needs a version for areas still.
        public static List<CompletePaintOp> GetPaintOps(List<DbTables.Place> places, string styleSet, ImageStats stats, string dataKey = "")
        {
            //TODO: add support here for reading FromTag styles? This needs to be checked.
            // -- should be done the step below, for inner ops.
            //TODO: add DataValue parameter when drawing from data. SHOULD happen automatically now that i add placeData to the tags, since that will set the style name.
            // --This will let me remove the PaintTown version of GetPaintOps
            // --the dataKey used to be passed in, and the value of the tag in dataKey was used as the style name

            //TagParser will already have run for the style set, so everything will have StyleName filled in. That will also include entries from Data instead of Tags.
            //so the only check should be to see if the style is FromTag, and then load it from the tag?
            //This might be done, actually, and the issue isn't that this logic needed extra work as much as drawing by areas is a special case and TagParser needed to load Data.
            //May want a version that takes areas and draws those, but that should stick to the naming schemes of just GetPaintOps and let parameters decide.

            var styles = TagParser.allStyleGroups[styleSet];
            var bgOp = new CompletePaintOp(Converters.GeoAreaToPolygon(stats.area), 1, styles["background"].PaintOperations.First(), "background", 1);
            var pass1 = places.Select(d => new { place = d, paintOp = styles[d.StyleName].PaintOperations });
            var pass2 = new List<CompletePaintOp>(places.Count * 2);
            pass2.Add(bgOp);
            foreach (var op in pass1)
                GetPaintOpsInner(ref pass2, op.place, op.paintOp, stats, dataKey);

            return pass2;
        }

        /// <summary>
        /// Creates the list of paint commands for the given elements, styles, and image area.
        /// </summary>
        /// <param name="places">the list of Places to be drawn</param>
        /// <param name="styleSet">the style set to use when drwaing the elements</param>
        /// <param name="stats">the info on the resulting image for calculating ops.</param>
        /// <returns>a list of CompletePaintOps to be passed into a DrawArea function</returns>
        public static List<CompletePaintOp> GetPaintOpsForPlaces(List<DbTables.Place> places, string styleSet, ImageStats stats)
        {
            var styles = TagParser.allStyleGroups[styleSet];
            var bgOp = new CompletePaintOp(Converters.GeoAreaToPolygon(stats.area), 1, styles["background"].PaintOperations.First(), "background", 1);
            var pass1 = places.Select(d => new { d.DrawSizeHint, d.ElementGeometry, paintOp = styles[d.StyleName].PaintOperations });
            var pass2 = new List<CompletePaintOp>(places.Count * 2);
            pass2.Add(bgOp);
            foreach (var op in pass1)
                GetPaintOps(ref pass2, op.DrawSizeHint, op.ElementGeometry, op.paintOp, stats);

            return pass2;
        }

        /// <summary>
        /// Creates the list of paint commands for the elements intersecting the given area, with the given data key attached to OSM elements and style set, for the image.
        /// </summary>
        /// <param name="dataKey">the key to pull data from attached to any Osm Elements intersecting the area</param>
        /// <param name="styleSet">the style set to use when drawing the intersecting elements</param>
        /// <param name="stats">the info on the resulting image for calculating ops.</param>
        /// <returns>a list of CompletePaintOps to be passed into a DrawArea function</returns>
        public static List<CompletePaintOp> GetPaintOpsForPlacesData(string dataKey, string styleSet, ImageStats stats)
        {
            //NOTE: this is being passed in an Area as a Geometry. The name needs clarified to show its drawing a maptile based on the gameplay data for places in that area.
            var db = new PraxisContext();
            var poly = Converters.GeoAreaToPolygon(GeometrySupport.MakeBufferedGeoArea(stats.area));
            var elements = db.PlaceData.Include(d => d.Place).Where(d => d.DataKey == dataKey && poly.Intersects(d.Place.ElementGeometry)).ToList();
            var styles = TagParser.allStyleGroups[styleSet];
            var bgOp = new CompletePaintOp(Converters.GeoAreaToPolygon(stats.area), 1, styles["background"].PaintOperations.First(), "background", 1);
            var pass1 = elements.Select(d => new { d.Place.DrawSizeHint, d.Place.ElementGeometry, paintOp = styles[d.DataValue.ToUTF8String()].PaintOperations, d.DataValue });
            var pass2 = new List<CompletePaintOp>(elements.Count * 2);
            pass2.Add(bgOp);
            foreach (var op in pass1)
                GetPaintOps(ref pass2, op.DrawSizeHint, op.ElementGeometry, op.paintOp, stats);

            return pass2;
        }

        public static List<CompletePaintOp> GetPaintOpsForPlacesParseTags(List<DbTables.Place> places, string styleSet, ImageStats stats)
        {
            //This one will be slightly slower since it runs TagParser on each entry, but that lets us have a fallback value if we don't find a specific entry.
            var styles = TagParser.allStyleGroups[styleSet];
            var bgOp = new CompletePaintOp(Converters.GeoAreaToPolygon(stats.area), 1, styles["background"].PaintOperations.First(), "background", 1);
            var pass1 = places.Select(d => new { d.DrawSizeHint, d.ElementGeometry, paintOp = styles[TagParser.GetStyleName(d, styleSet)].PaintOperations });
            var pass2 = new List<CompletePaintOp>(places.Count * 2);
            pass2.Add(bgOp);
            foreach (var op in pass1)
                GetPaintOps(ref pass2, op.DrawSizeHint, op.ElementGeometry, op.paintOp, stats);

            return pass2;
        }

        //This function only works if all the dataValue entries are a key in styles
        /// <summary>
        /// Creates the list of paint commands for the PlusCode cells intersecting the given area, with the given data key and style set, for the image.
        /// </summary>
        /// <param name="dataKey">the key to pull data from attached to any Osm Elements intersecting the area</param>
        /// <param name="styleSet">the style set to use when drawing the intersecting elements</param>
        /// <param name="stats">the info on the resulting image for calculating ops.</param>
        /// <returns>a list of CompletePaintOps to be passed into a DrawArea function</returns>
        public static List<CompletePaintOp> GetPaintOpsForAreaData(string dataKey, string styleSet, ImageStats stats)
        {
            var db = new PraxisContext();
            var poly = Converters.GeoAreaToPolygon(GeometrySupport.MakeBufferedGeoArea(stats.area));
            var elements = db.AreaData.Where(d => d.DataKey == dataKey && poly.Intersects(d.GeoAreaIndex)).ToList();
            var styles = TagParser.allStyleGroups[styleSet];
            var bgOp = new CompletePaintOp(Converters.GeoAreaToPolygon(stats.area), 1, styles["background"].PaintOperations.First(), "background", 1);
            var pass1 = elements.Select(d => new { d.GeoAreaIndex.Area, d.GeoAreaIndex, paintOp = styles[d.DataValue.ToUTF8String()].PaintOperations, d.DataValue });
            var pass2 = new List<CompletePaintOp>(elements.Count * 2);
            pass2.Add(bgOp);
            foreach (var op in pass1)
                GetPaintOps(ref pass2, op.Area, op.GeoAreaIndex, op.paintOp, stats);

            return pass2;
        }

        //Allows for 1 style to pull a color from the custom data value.
        /// <summary>
        /// Creates the list of paint commands for the PlusCode cells intersecting the given area, with the given data key and style set, for the image. In this case, the color will be the tag's value.
        /// </summary>
        /// <param name="dataKey">the key to pull data from attached to any Osm Elements intersecting the area</param>
        /// <param name="styleSet">the style set to use when drawing the intersecting elements</param>
        /// <param name="stats">the info on the resulting image for calculating ops.</param>
        /// <returns>a list of CompletePaintOps to be passed into a DrawArea function</returns>
        public static List<CompletePaintOp> GetPaintOpsForAreaDataByTag(string dataKey, string styleSet, ImageStats stats)
        {
            var db = new PraxisContext();
            var poly = Converters.GeoAreaToPolygon(GeometrySupport.MakeBufferedGeoArea(stats.area));
            var elements = db.AreaData.Where(d => d.DataKey == dataKey && poly.Intersects(d.GeoAreaIndex)).ToList();
            var styles = TagParser.allStyleGroups[styleSet];
            var bgOp = new CompletePaintOp(Converters.GeoAreaToPolygon(stats.area), 1, styles["background"].PaintOperations.First(), "background", 1);
            var pass1 = elements.Select(d => new { d.GeoAreaIndex.Area, d.GeoAreaIndex, paintOp = styles["tag"].PaintOperations, d.DataValue });
            var pass2 = new List<CompletePaintOp>(elements.Count * 2); //assuming each element has a Fill and Stroke op separately
            pass2.Add(bgOp);
            foreach (var op in pass1)
                GetPaintOps(ref pass2, op.Area, op.GeoAreaIndex, op.paintOp, stats);

            return pass2;
        }

        /// <summary>
        /// Creates all gameplay tiles for a given area and saves them to the database (or files, if that option is set)
        /// </summary>
        /// <param name="areaToDraw">the GeoArea of the area to create tiles for.</param>
        /// <param name="saveToFiles">If true, writes to files in the output folder. If false, saves to DB.</param>
        public static void PregenMapTilesForArea(GeoArea areaToDraw, bool saveToFiles = false)
        {
            //There is a very similar function for this in Standalone.cs, but this one writes back to the main DB.
            var intersectCheck = Converters.GeoAreaToPolygon(areaToDraw);
            //start drawing maptiles and sorting out data.
            var swCorner = new OpenLocationCode(intersectCheck.EnvelopeInternal.MinY, intersectCheck.EnvelopeInternal.MinX);
            var neCorner = new OpenLocationCode(intersectCheck.EnvelopeInternal.MaxY, intersectCheck.EnvelopeInternal.MaxX);

            //declare how many map tiles will be drawn
            var xTiles = areaToDraw.LongitudeWidth / resolutionCell8;
            var yTiles = areaToDraw.LatitudeHeight / resolutionCell8;
            var totalTiles = Math.Truncate(xTiles * yTiles);

            Log.WriteLog("Starting processing maptiles for " + totalTiles + " Cell8 areas.");
            long mapTileCounter = 0;
            System.Diagnostics.Stopwatch progressTimer = new System.Diagnostics.Stopwatch();
            progressTimer.Start();

            //now, for every Cell8 involved, draw and name it.
            var yCoords = new List<double>((int)(intersectCheck.EnvelopeInternal.Height / resolutionCell8) + 1);
            var yVal = swCorner.Decode().SouthLatitude;
            while (yVal <= neCorner.Decode().NorthLatitude)
            {
                yCoords.Add(yVal);
                yVal += resolutionCell8;
            }

            var xCoords = new List<double>((int)(intersectCheck.EnvelopeInternal.Width / resolutionCell8) + 1);
            var xVal = swCorner.Decode().WestLongitude;
            while (xVal <= neCorner.Decode().EastLongitude)
            {
                xCoords.Add(xVal);
                xVal += resolutionCell8;
            }

            var allPlaces = GetPlaces(areaToDraw);

            object listLock = new object();
            DateTime expiration = DateTime.UtcNow.AddYears(10);
            foreach (var y in yCoords)
            {
                System.Diagnostics.Stopwatch thisRowSW = new System.Diagnostics.Stopwatch();
                thisRowSW.Start();
                var db = new PraxisContext();
                db.ChangeTracker.AutoDetectChangesEnabled = false;
                //Make a collision box for just this row of Cell8s, and send the loop below just the list of things that might be relevant.
                //Add a Cell8 buffer space so all elements are loaded and drawn without needing to loop through the entire area.
                GeoArea thisRow = new GeoArea(y - ConstantValues.resolutionCell8, xCoords.First() - ConstantValues.resolutionCell8, y + ConstantValues.resolutionCell8 + ConstantValues.resolutionCell8, xCoords.Last() + resolutionCell8);
                var rowList = GetPlaces(thisRow, allPlaces, skipTags: true);
                var tilesToSave = new List<MapTile>(xCoords.Count);

                Parallel.ForEach(xCoords, x =>
                {
                    //make map tile.
                    var plusCode = new OpenLocationCode(y, x, 10);
                    var plusCode8 = plusCode.CodeDigits.Substring(0, 8);
                    var plusCodeArea = OpenLocationCode.DecodeValid(plusCode8);
                    var paddedArea = GeometrySupport.MakeBufferedGeoArea(plusCodeArea);

                    var acheck = Converters.GeoAreaToPreparedPolygon(paddedArea); //Fastest search option is one preparedPolygon against a list of normal geometry.
                    var areaList = rowList.Where(a => acheck.Intersects(a.ElementGeometry)).ToList(); //Get the list of areas in this maptile.

                    var info = new ImageStats(plusCode8);
                    //new setup.
                    var areaPaintOps = GetPaintOpsForPlaces(areaList, "mapTiles", info);
                    var tile = DrawPlusCode(plusCode8, areaPaintOps);

                    if (saveToFiles)
                    {
                        File.WriteAllBytes("GameTiles\\" + plusCode8 + ".png", tile);
                    }
                    else
                    {
                        var thisTile = new MapTile() { TileData = tile, PlusCode = plusCode8, ExpireOn = expiration, AreaCovered = acheck.Geometry, StyleSet = "mapTiles" };
                        lock (listLock)
                            tilesToSave.Add(thisTile);
                    }
                });
                mapTileCounter += xCoords.Count;
                if (!saveToFiles)
                {
                    db.MapTiles.AddRange(tilesToSave);
                    db.SaveChanges();
                }
                Log.WriteLog(mapTileCounter + " tiles processed, " + Math.Round((mapTileCounter / totalTiles) * 100, 2) + "% complete, " + Math.Round(xCoords.Count / thisRowSW.Elapsed.TotalSeconds, 2) + " tiles per second.");

            }//);
            progressTimer.Stop();
            Log.WriteLog("Area map tiles drawn in " + progressTimer.Elapsed.ToString() + ", averaged " + Math.Round(mapTileCounter / progressTimer.Elapsed.TotalSeconds, 2) + " tiles per second.");
        }

        /// <summary>
        /// Generates all SlippyMap tiles for a given area and zoom level, and saves them to the database.
        /// </summary>
        /// <param name="buffered">the GeoArea to generate tiles for</param>
        /// <param name="zoomLevel">the zoom level to generate tiles at.</param>
        public static void PregenSlippyMapTilesForArea(GeoArea buffered, int zoomLevel)
        {
            //There is a very similar function for this in Standalone.cs, but this one writes back to the main DB.
            var db = new PraxisContext();
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            var intersectCheck = Converters.GeoAreaToPolygon(buffered);

            //start drawing maptiles and sorting out data.
            var swCornerLon = Converters.GetSlippyXFromLon(intersectCheck.EnvelopeInternal.MinX, zoomLevel);
            var neCornerLon = Converters.GetSlippyXFromLon(intersectCheck.EnvelopeInternal.MaxX, zoomLevel);
            var swCornerLat = Converters.GetSlippyYFromLat(intersectCheck.EnvelopeInternal.MinY, zoomLevel);
            var neCornerLat = Converters.GetSlippyYFromLat(intersectCheck.EnvelopeInternal.MaxY, zoomLevel);

            //declare how many map tiles will be drawn
            var xTiles = neCornerLon - swCornerLon + 1;
            var yTiles = swCornerLat - neCornerLat + 1;
            var totalTiles = xTiles * yTiles;

            Log.WriteLog("Starting processing " + totalTiles + " maptiles for zoom level " + zoomLevel);
            long mapTileCounter = 0;
            System.Diagnostics.Stopwatch progressTimer = new System.Diagnostics.Stopwatch();
            progressTimer.Start();

            //foreach (var y in yCoords)
            for (var y = neCornerLat; y <= swCornerLat; y++)
            {
                //Make a collision box for just this row of Cell8s, and send the loop below just the list of things that might be relevant.
                //Add a Cell8 buffer space so all elements are loaded and drawn without needing to loop through the entire area.
                GeoArea thisRow = new GeoArea(Converters.SlippyYToLat(y + 1, zoomLevel) - ConstantValues.resolutionCell8,
                    Converters.SlippyXToLon(swCornerLon, zoomLevel) - ConstantValues.resolutionCell8,
                    Converters.SlippyYToLat(y, zoomLevel) + ConstantValues.resolutionCell8,
                    Converters.SlippyXToLon(neCornerLon, zoomLevel) + resolutionCell8);
                var row = Converters.GeoAreaToPolygon(thisRow);
                var rowList = GetPlaces(thisRow);
                var tilesToSave = new ConcurrentBag<SlippyMapTile>();

                Parallel.For(swCornerLon, neCornerLon + 1, (x) =>
                {
                    //make map tile.
                    var info = new ImageStats(zoomLevel, x, y, MapTileSupport.SlippyTileSizeSquare);
                    var acheck = Converters.GeoAreaToPolygon(info.area); //this is faster than using a PreparedPolygon in testing, which was unexpected.
                    var areaList = rowList.Where(a => acheck.Intersects(a.ElementGeometry)).ToList(); //This one is for the maptile

                    var tile = MapTiles.DrawAreaAtSize(info, areaList);
                    tilesToSave.Add(new SlippyMapTile() { TileData = tile, Values = x + "|" + y + "|" + zoomLevel, ExpireOn = DateTime.UtcNow.AddDays(3650), AreaCovered = Converters.GeoAreaToPolygon(info.area), StyleSet = "mapTiles" });

                    mapTileCounter++;
                });
                db.SlippyMapTiles.AddRange(tilesToSave);
                db.SaveChanges();
                Log.WriteLog(mapTileCounter + " tiles processed, " + Math.Round(((mapTileCounter / (double)totalTiles * 100)), 2) + "% complete");

            }//);
            progressTimer.Stop();
            Log.WriteLog("Zoom " + zoomLevel + " map tiles drawn in " + progressTimer.Elapsed.ToString());
        }

        public static long SaveMapTile(string code, string styleSet, byte[] image)
        {
            var db = new PraxisContext();
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            var existingResults = db.MapTiles.FirstOrDefault(mt => mt.PlusCode == code && mt.StyleSet == styleSet);
            if (existingResults == null) {
                existingResults = new MapTile() { PlusCode = code, StyleSet = styleSet, AreaCovered = Converters.GeoAreaToPolygon(GeometrySupport.MakeBufferedGeoArea(code.ToGeoArea())) };
                db.MapTiles.Add(existingResults);
            }
            else
                db.Entry(existingResults).State = EntityState.Modified;

            existingResults.ExpireOn = DateTime.UtcNow.AddYears(10);
            existingResults.TileData = image;
            existingResults.GenerationID++;
            db.SaveChanges();
            return existingResults.GenerationID;
        }

        public static byte[] GetExistingTileImage(string code, string styleSet)
        {
            var db = new PraxisContext();
            var existingResults = db.MapTiles.FirstOrDefault(mt => mt.PlusCode == code && mt.StyleSet == styleSet);
            if (existingResults == null || existingResults.ExpireOn < DateTime.UtcNow)
                return null;

            return existingResults.TileData;
        }
    }
}