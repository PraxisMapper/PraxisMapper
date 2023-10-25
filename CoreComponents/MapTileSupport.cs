using Google.OpenLocationCode;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using PraxisCore.Styles;
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

        /// <summary>
        /// The size to draw Slippy map tiles. Some implementations expect 256x256 tiles, some can support larger ones with configuration values.
        /// </summary>
        public static int SlippyTileSizeSquare = 512;
        /// <summary>
        /// How much to multiple the bounds of a game tile by. Game tiles are Cell8 sized images, with each Cell11 as a single pixel (80x100) at a multiplier of 1.
        /// </summary>
        public static double GameTileScale = 4;
        /// <summary>
        /// How much additional space to add by default when buffering a GeoArea. If you would draw items that do not intersect with the current tile, they must intersect with the buffered area.
        /// EX: Points drawn as a circle need to have a buffer of at least the circle's radius in order to not be clipped out.
        /// </summary>
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

        /// <summary>
        /// A code reference for how big an image would be using 11-character PlusCodes for pixels, multiplied by GameTileScale (default 2)
        /// </summary>
        /// <param name="code"></param>
        /// <param name="X"></param>
        /// <param name="Y"></param>
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

        /// <summary>
        /// Gets an PNG image of the requested PlusCode, drawn in the requested style set.
        /// </summary>
        /// <param name="area"></param>
        /// <param name="styleSet"></param>
        /// <returns></returns>
        public static byte[] DrawPlusCode(ReadOnlySpan<char> area, string styleSet = "mapTiles")
        {
            //This might be a cleaner version of my V4 function, for working with CellX sized tiles..
            //This will draw at a Cell11 resolution automatically.
            //Split it into a few functions.
            //then get all the area

            var info = new ImageStats(area);
            return MapTiles.DrawAreaAtSize(info, styleSet: styleSet);
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
            return MapTiles.DrawAreaAtSize(info, styleSet: styleSet);
        }

        /// <summary>
        /// Get the image for a PlusCode, given the paint operations to draw.
        /// </summary>
        /// <param name="area">the PlusCode string to draw. Can be 6-11 digits long</param>
        /// <param name="paintOps">the list of paint operations to run through for drawing</param>
        /// <returns>a byte array for the png file of the pluscode image file</returns>
        public static byte[] DrawPlusCode(string area, List<CompletePaintOp> paintOps)
        {
            var info = new ImageStats(area);
            return MapTiles.DrawAreaAtSize(info, paintOps);
        }

        /// <summary>
        /// Get the image for a PlusCode, given the paint operations to draw.
        /// </summary>
        /// <param name="area"></param>
        /// <param name="paintOps"></param>
        /// <returns></returns>
        public static byte[] DrawPlusCode(ReadOnlySpan<char> area, List<CompletePaintOp> paintOps)
        {
            var info = new ImageStats(area);
            return MapTiles.DrawAreaAtSize(info, paintOps);
        }

        /// <summary>
        /// The internal function used by GetPaintOps to generate the final list of paint operations.
        /// </summary>
        /// <param name="list"></param>
        /// <param name="place"></param>
        /// <param name="midOps"></param>
        /// <param name="stats"></param>
        private static void GetPaintOpsInner(ref List<CompletePaintOp> list, DbTables.Place place, ICollection<StylePaint> midOps, ImageStats stats)
        {
            string tagColor = "";
            foreach (var po in midOps)
                if (stats.degreesPerPixelX < po.MaxDrawRes
                    && stats.degreesPerPixelX > po.MinDrawRes //dppX should be between max and min draw range.
                    && !(po.HtmlColorCode.Length == 8 && po.HtmlColorCode.StartsWith("00")) //color is NOT transparent.
                    && (!po.FromTag || TagParser.GetTagValue(place, place.StyleName, out tagColor))
                    && (!po.StaticColorFromName || TagParser.PickStaticColorByName(TagParser.GetName(place), out tagColor))
                    )
                             list.Add(new CompletePaintOp(
                        place.ElementGeometry,
                        place.DrawSizeHint,
                        po,
                        tagColor,
                        po.FixedWidth == 0 ? po.LineWidthDegrees * stats.pixelsPerDegreeX : po.FixedWidth,
                        po.StaticColorFromName
                        )
                    );
        }

        /// <summary>
        /// Returns the paint operations needed to draw an image, given the Places to draw, the style set to draw in, and the information on the output area and image.
        /// </summary>
        /// <param name="places"></param>
        /// <param name="styleSet"></param>
        /// <param name="stats"></param>
        /// <returns></returns>
        public static List<CompletePaintOp> GetPaintOpsForPlaces(List<DbTables.Place> places, string styleSet, ImageStats stats)
        {
            var styles = TagParser.allStyleGroups[styleSet];
            var bgOp = new CompletePaintOp(stats.area.ToPolygon(), 1, styles["background"].PaintOperations.First(), "background", 1, false);
            var pass1 = places.Select(d => new { place = d, paintOp = styles[d.StyleName].PaintOperations });
            var pass2 = new List<CompletePaintOp>(places.Count);
            pass2.Add(bgOp);
            foreach (var op in pass1)
                GetPaintOpsInner(ref pass2, op.place, op.paintOp, stats);

            return pass2;
        }

        /// <summary>
        /// Returns the paint operations needed to draw an image from AreaData entries, given the style set to draw in. The Area will be the one from ImageStats.
        /// </summary>
        /// <param name="styleSet"></param>
        /// <param name="stats"></param>
        /// <returns></returns>
        public static List<CompletePaintOp> GetPaintOpsForAreas(string styleSet, ImageStats stats)
        {
            var styles = TagParser.allStyleGroups[styleSet];
            using var db = new PraxisContext();
            db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
            db.ChangeTracker.AutoDetectChangesEnabled = false;

            var searchPoly = GeometrySupport.MakeBufferedGeoArea(stats.area).ToPolygon();
            var drawPoly = stats.area.ToPolygon();
            var elements = db.AreaData.Where(d => searchPoly.Intersects(d.AreaCovered)).ToList(); //Each of these will be a single tag/value and a plusCode.

            var bgOp = new CompletePaintOp(drawPoly, 1, styles["background"].PaintOperations.First(), "background", 1, false);
            var pass1 = elements.Select(d => new { place = d.ToPlace(styleSet), paintOp = styles[TagParser.GetStyleEntry(new List<AreaData>() { d }, styleSet).Name].PaintOperations });
            var pass2 = new List<CompletePaintOp>(elements.Count);
            pass2.Add(bgOp);
            foreach (var op in pass1)
                GetPaintOpsInner(ref pass2, op.place, op.paintOp, stats);

            return pass2;
        }

        public static List<CompletePaintOp> GetPaintOpsForGeometry(Geometry geometry, ICollection<StylePaint> style, ImageStats stats, string tagValue = "")
        {
            var result = style.Select(s =>  new CompletePaintOp(geometry, 
                        GeometrySupport.CalculateDrawSizeHint(geometry, style),
                        s,
                        tagValue,
                        s.FixedWidth == 0 ? s.LineWidthDegrees * stats.pixelsPerDegreeX : s.FixedWidth,
                        s.StaticColorFromName
                        )).ToList();

            return result;
        }


        /// <summary>
        /// Creates all gameplay tiles for a given area and saves them to the database (or files, if that option is set)
        /// </summary>
        /// <param name="areaToDraw">the GeoArea of the area to create tiles for.</param>
        /// <param name="saveToFiles">If true, writes to files in the output folder. If false, saves to DB.</param>
        public static void PregenMapTilesForArea(GeoArea areaToDraw, bool saveToFiles = false)
        {
            //There is a very similar function for this in Standalone.cs, but this one writes back to the main DB.
            var intersectCheck = areaToDraw.ToPolygon();
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
                using var db = new PraxisContext();
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
            using var db = new PraxisContext();
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            var intersectCheck = buffered.ToPolygon();

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
                var row = thisRow.ToPolygon();
                var rowList = GetPlaces(thisRow);
                var tilesToSave = new ConcurrentBag<SlippyMapTile>();

                Parallel.For(swCornerLon, neCornerLon + 1, (x) =>
                {
                    //make map tile.
                    var info = new ImageStats(zoomLevel, x, y, MapTileSupport.SlippyTileSizeSquare);
                    var acheck = info.area.ToPolygon(); //this is faster than using a PreparedPolygon in testing, which was unexpected.
                    var areaList = rowList.Where(a => acheck.Intersects(a.ElementGeometry)).ToList(); //This one is for the maptile

                    var tile = MapTiles.DrawAreaAtSize(info, areaList);
                    tilesToSave.Add(new SlippyMapTile() { TileData = tile, Values = x + "|" + y + "|" + zoomLevel, ExpireOn = DateTime.UtcNow.AddDays(3650), AreaCovered = GeometrySupport.MakeBufferedGeoArea(info.area).ToPolygon(), StyleSet = "mapTiles" });

                    mapTileCounter++;
                });
                db.SlippyMapTiles.AddRange(tilesToSave);
                db.SaveChanges();
                Log.WriteLog(mapTileCounter + " tiles processed, " + Math.Round(((mapTileCounter / (double)totalTiles * 100)), 2) + "% complete");

            }//);
            progressTimer.Stop();
            Log.WriteLog("Zoom " + zoomLevel + " map tiles drawn in " + progressTimer.Elapsed.ToString());
        }

        /// <summary>
        /// Saves a MapTile to the database, given its PlusCode, style set, and the png image data.
        /// </summary>
        /// <param name="code"></param>
        /// <param name="styleSet"></param>
        /// <param name="image"></param>
        /// <returns></returns>
        public static long SaveMapTile(string code, string styleSet, byte[] image, DateTime? expires = null)
        {
            using var db = new PraxisContext();
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            var existingResults = db.MapTiles.FirstOrDefault(mt => mt.PlusCode == code && mt.StyleSet == styleSet);
            if (existingResults == null)
            {
                existingResults = new MapTile() { PlusCode = code, StyleSet = styleSet, AreaCovered = code.ToPolygon() };
                db.MapTiles.Add(existingResults);
            }
            else
                db.Entry(existingResults).State = EntityState.Modified;

            if (expires == null)
                existingResults.ExpireOn = DateTime.UtcNow.AddYears(10);
            else
                existingResults.ExpireOn = expires.Value;
            existingResults.TileData = image;
            existingResults.GenerationID++;
            db.SaveChanges();
            return existingResults.GenerationID;
        }

        /// <summary>
        /// Save a Slippy maptile to the database, given the key, style set, and image data.
        /// </summary>
        public static long SaveSlippyMapTile(ImageStats info, string tileKey, string styleSet, byte[] image, DateTime? expires = null)
        {
            using var db = new PraxisContext();
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            var existingResults = db.SlippyMapTiles.FirstOrDefault(mt => mt.Values == tileKey && mt.StyleSet == styleSet);
            if (existingResults == null)
            {
                existingResults = new SlippyMapTile() { Values = tileKey, StyleSet = styleSet, AreaCovered = GeometrySupport.MakeBufferedGeoArea(info.area).ToPolygon() };
                db.SlippyMapTiles.Add(existingResults);
            }
            else
                db.Entry(existingResults).State = EntityState.Modified;

            if (expires == null)
                existingResults.ExpireOn = DateTime.UtcNow.AddYears(10);
            else
                existingResults.ExpireOn = expires.Value;

            existingResults.TileData = image;
            existingResults.GenerationID++;
            db.SaveChanges();

            return existingResults.GenerationID;
        }

        /// <summary>
        /// Loads an existing image from the database, if one is present, given the Pluscode and style set to load.
        /// </summary>
        /// <param name="code"></param>
        /// <param name="styleSet"></param>
        /// <returns></returns>
        public static byte[] GetExistingTileImage(string code, string styleSet)
        {
            using var db = new PraxisContext();
            db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            var existingResults = db.MapTiles.FirstOrDefault(mt => mt.PlusCode == code && mt.StyleSet == styleSet);
            if (existingResults == null || existingResults.ExpireOn < DateTime.UtcNow)
                return null;

            return existingResults.TileData;
        }

        public static byte[] GetExistingSlippyTile(string tileKey, string styleSet)
        {
            using var db = new PraxisContext();
            db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            var existingResults = db.SlippyMapTiles.FirstOrDefault(mt => mt.Values == tileKey && mt.StyleSet == styleSet);
            if (existingResults == null || existingResults.ExpireOn < DateTime.UtcNow)
                return null;

            return existingResults.TileData;
        }

        /// <summary>
        /// Rescales iStats to fit within the maximum edge and maximum pixel count provided.
        /// </summary>
        /// <param name="istats"></param>
        /// <param name="maxEdge"></param>
        /// <param name="maxPixels"></param>
        /// <returns></returns>
        public static ImageStats ScaleBoundsCheck(ImageStats istats, int maxEdge, long maxPixels)
        {
            //sanity check: we don't want to draw stuff that won't fit in memory, so check for size and cap it if needed
            if ((long)istats.imageSizeX * istats.imageSizeY > maxPixels)
            {
                var ratio = (double)istats.imageSizeX / istats.imageSizeY; //W:H,
                int newSize = (istats.imageSizeY > maxEdge ? maxEdge : istats.imageSizeY);
                istats = new ImageStats(istats.area, (int)(newSize * ratio), newSize);
            }

            return istats;
        }
    }
}