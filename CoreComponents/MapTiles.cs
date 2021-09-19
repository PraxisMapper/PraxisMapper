using Google.OpenLocationCode;
using NetTopologySuite.Geometries;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using static PraxisCore.ConstantValues;
using static PraxisCore.DbTables;
using static PraxisCore.Place;
using static PraxisCore.Singletons;
using SkiaSharp;
using PraxisCore.Support;
using OsmSharp.API;
using System.Xml.Schema;
using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;

namespace PraxisCore
{
    /// <summary>
    /// All functions related to generating or expiring map tiles. Both PlusCode sized tiles for gameplay or SlippyMap tiles for a webview.
    /// </summary>
    public static class MapTiles
    {
        public static int MapTileSizeSquare = 512; //Default value, updated by PraxisMapper at startup. COvers Slippy tiles, not gameplay tiles.
        static SKPaint eraser = new SKPaint() { Color = SKColors.Transparent, BlendMode = SKBlendMode.Src, Style = SKPaintStyle.StrokeAndFill }; //BlendMode is the important part for an Eraser.
        static Random r = new Random();

        /// <summary>
        /// Draw square boxes around each area to approximate how they would behave in an offline app
        /// </summary>
        /// <param name="info">the image information for drawing</param>
        /// <param name="items">the elements to draw.</param>
        /// <returns>byte array of the generated .png tile image</returns>
        public static byte[] DrawOfflineEstimatedAreas(ImageStats info, List<StoredOsmElement> items)
        {
            SKBitmap bitmap = new SKBitmap(info.imageSizeX, info.imageSizeY, SKColorType.Rgba8888, SKAlphaType.Premul);
            SKCanvas canvas = new SKCanvas(bitmap);
            var bgColor = SKColors.Transparent;
            canvas.Clear(bgColor);
            canvas.Scale(1, -1, info.imageSizeX / 2, info.imageSizeY / 2);
            SKPaint fillpaint = new SKPaint();
            fillpaint.IsAntialias = true;
            fillpaint.Style = SKPaintStyle.Fill;
            var strokePaint = new SKPaint();
            strokePaint.Color = SKColors.Black;
            strokePaint.TextSize = 32;
            strokePaint.StrokeWidth = 3;
            strokePaint.Style = SKPaintStyle.Stroke;
            strokePaint.TextAlign = SKTextAlign.Center;
            //TagParser.ApplyTags(items);

            var placeInfo = PraxisCore.Standalone.Standalone.GetPlaceInfo(items.Where(i =>
            i.IsGameElement
            ).ToList());

            //this is for rectangles.
            foreach (var pi in placeInfo)
            {
                var rect = Converters.PlaceInfoToRect(pi, info);
                fillpaint.Color = TagParser.PickStaticColorForArea(pi.Name);
                canvas.DrawRect(rect, fillpaint);
                canvas.DrawRect(rect, strokePaint);
            }

            canvas.Scale(1, -1, info.imageSizeX / 2, info.imageSizeY / 2); //inverts the inverted image again!
            foreach (var pi in placeInfo)
            {
                var rect = Converters.PlaceInfoToRect(pi, info);
                canvas.DrawText(pi.Name, rect.MidX, info.imageSizeY - rect.MidY, strokePaint);
            }

            var ms = new MemoryStream();
            var skms = new SkiaSharp.SKManagedWStream(ms);
            bitmap.Encode(skms, SkiaSharp.SKEncodedImageFormat.Png, 100);
            var results = ms.ToArray();
            skms.Dispose(); ms.Close(); ms.Dispose();
            return results;
        }

        /// <summary>
        /// Draws grid lines to match boundaries for 8 character PlusCodes.
        /// </summary>
        /// <param name="totalArea">the GeoArea to draw lines in</param>
        /// <returns>the byte array for the maptile png file</returns>
        public static byte[] DrawCell8GridLines(GeoArea totalArea)
        {
            int imageSizeX = MapTileSizeSquare;
            int imageSizeY = MapTileSizeSquare;
            SKBitmap bitmap = new SKBitmap(imageSizeX, imageSizeY, SKColorType.Rgba8888, SKAlphaType.Premul);
            SKCanvas canvas = new SKCanvas(bitmap);
            var bgColor = new SKColor();
            SKColor.TryParse("00000000", out bgColor);
            canvas.Clear(bgColor);
            canvas.Scale(1, -1, imageSizeX / 2, imageSizeY / 2);
            SKPaint paint = new SKPaint();
            SKColor color = new SKColor();
            SKColor.TryParse("#FF0000", out color);
            paint.Color = color;
            paint.Style = SKPaintStyle.Stroke;
            paint.StrokeWidth = 3;
            paint.IsAntialias = true;

            double degreesPerPixelX = totalArea.LongitudeWidth / imageSizeX;
            double degreesPerPixelY = totalArea.LatitudeHeight / imageSizeY;

            //This is hardcoded to Cell 8 spaced gridlines.
            var imageLeft = totalArea.WestLongitude;
            var spaceToFirstLineLeft = (imageLeft % resolutionCell8);

            var imageBottom = totalArea.SouthLatitude;
            var spaceToFirstLineBottom = (imageBottom % resolutionCell8);

            double lonLineTrackerDegrees = imageLeft - spaceToFirstLineLeft; //This is degree coords
            while (lonLineTrackerDegrees <= totalArea.EastLongitude + resolutionCell8) //This means we should always draw at least 2 lines, even if they're off-canvas.
            {
                var geoLine = new LineString(new Coordinate[] { new Coordinate(lonLineTrackerDegrees, 90), new Coordinate(lonLineTrackerDegrees, -90) });
                var points = Converters.PolygonToSKPoints(geoLine, totalArea, degreesPerPixelX, degreesPerPixelY);
                canvas.DrawLine(points[0], points[1], paint);
                lonLineTrackerDegrees += resolutionCell8;
            }

            double latLineTrackerDegrees = imageBottom - spaceToFirstLineBottom; //This is degree coords
            while (latLineTrackerDegrees <= totalArea.NorthLatitude + resolutionCell8) //This means we should always draw at least 2 lines, even if they're off-canvas.
            {
                var geoLine = new LineString(new Coordinate[] { new Coordinate(180, latLineTrackerDegrees), new Coordinate(-180, latLineTrackerDegrees) });
                var points = Converters.PolygonToSKPoints(geoLine, totalArea, degreesPerPixelX, degreesPerPixelY);
                canvas.DrawLine(points[0], points[1], paint);
                latLineTrackerDegrees += resolutionCell8;
            }

            var ms = new MemoryStream();
            var skms = new SkiaSharp.SKManagedWStream(ms);
            bitmap.Encode(skms, SkiaSharp.SKEncodedImageFormat.Png, 100);
            var results = ms.ToArray();
            skms.Dispose(); ms.Close(); ms.Dispose();
            return results;
        }

        /// <summary>
        /// Draws grid lines to match boundaries for 10 character PlusCodes.
        /// </summary>
        /// <param name="totalArea">the GeoArea to draw lines in</param>
        /// <returns>the byte array for the maptile png file</returns>
        public static byte[] DrawCell10GridLines(GeoArea totalArea)
        {
            int imageSizeX = MapTileSizeSquare;
            int imageSizeY = MapTileSizeSquare;
            SKBitmap bitmap = new SKBitmap(imageSizeX, imageSizeY, SKColorType.Rgba8888, SKAlphaType.Premul);
            SKCanvas canvas = new SKCanvas(bitmap);
            var bgColor = new SKColor();
            SKColor.TryParse("00000000", out bgColor);
            canvas.Clear(bgColor);
            canvas.Scale(1, -1, imageSizeX / 2, imageSizeY / 2);
            SKPaint paint = new SKPaint();
            SKColor color = new SKColor();
            SKColor.TryParse("#00CCFF", out color);
            paint.Color = color;
            paint.Style = SKPaintStyle.Stroke;
            paint.StrokeWidth = 1;
            paint.IsAntialias = true;

            double degreesPerPixelX = totalArea.LongitudeWidth / imageSizeX;
            double degreesPerPixelY = totalArea.LatitudeHeight / imageSizeY;

            //This is hardcoded to Cell 8 spaced gridlines.
            var imageLeft = totalArea.WestLongitude;
            var spaceToFirstLineLeft = (imageLeft % resolutionCell10);

            var imageBottom = totalArea.SouthLatitude;
            var spaceToFirstLineBottom = (imageBottom % resolutionCell10);

            double lonLineTrackerDegrees = imageLeft - spaceToFirstLineLeft; //This is degree coords
            while (lonLineTrackerDegrees <= totalArea.EastLongitude + resolutionCell10) //This means we should always draw at least 2 lines, even if they're off-canvas.
            {
                var geoLine = new LineString(new Coordinate[] { new Coordinate(lonLineTrackerDegrees, 90), new Coordinate(lonLineTrackerDegrees, -90) });
                var points = Converters.PolygonToSKPoints(geoLine, totalArea, degreesPerPixelX, degreesPerPixelY);
                canvas.DrawLine(points[0], points[1], paint);
                lonLineTrackerDegrees += resolutionCell10;
            }

            double latLineTrackerDegrees = imageBottom - spaceToFirstLineBottom; //This is degree coords
            while (latLineTrackerDegrees <= totalArea.NorthLatitude + resolutionCell10) //This means we should always draw at least 2 lines, even if they're off-canvas.
            {
                var geoLine = new LineString(new Coordinate[] { new Coordinate(180, latLineTrackerDegrees), new Coordinate(-180, latLineTrackerDegrees) });
                var points = Converters.PolygonToSKPoints(geoLine, totalArea, degreesPerPixelX, degreesPerPixelY);
                canvas.DrawLine(points[0], points[1], paint);
                latLineTrackerDegrees += resolutionCell10;
            }

            var ms = new MemoryStream();
            var skms = new SkiaSharp.SKManagedWStream(ms);
            bitmap.Encode(skms, SkiaSharp.SKEncodedImageFormat.Png, 100);
            var results = ms.ToArray();
            skms.Dispose(); ms.Close(); ms.Dispose();
            return results;
        }

        /// <summary>
        /// Force gameplay maptiles to expire and be redrawn on next access. Can be limited to a specific style set or run on all tiles.
        /// </summary>
        /// <param name="g">the area to expire intersecting maptiles with</param>
        /// <param name="styleSet">which set of maptiles to expire. All tiles if this is an empty string</param>
        public static void ExpireMapTiles(Geometry g, string styleSet = "")
        {
            //If this would be faster as raw SQL, see function below for a template on how to write that.
            //TODO: test this logic, should be faster but 
            var db = new PraxisContext();
            //MariaDB SQL, should be functional
            string SQL = "UPDATE MapTiles SET ExpireOn = CURRENT_TIMESTAMP WHERE (styleSet= '" + styleSet + "' OR '" + styleSet + "' = '') AND ST_INTERSECTS(areaCovered, ST_GEOMFROMTEXT('" + g.AsText() + "'))";
            db.Database.ExecuteSqlRaw(SQL);
        }

        /// <summary>
        /// Force gameplay maptiles to expire and be redrawn on next access. Can be limited to a specific style set or run on all tiles.
        /// </summary>
        /// <param name="elementId">the privacyID of a storedOsmElement to expire intersecting tiles for.</param>
        /// <param name="styleSet">which set of maptiles to expire. All tiles if this is an empty string</param>
        public static void ExpireMapTiles(Guid elementId, string styleSet = "")
        {
            //If this would be faster as raw SQL, see function below for a template on how to write that.
            //TODO: test this logic, should be faster but 
            var db = new PraxisContext();
            //MariaDB SQL, should be functional
            string SQL = "UPDATE MapTiles SET ExpireOn = CURRENT_TIMESTAMP WHERE (styleSet= '" + styleSet + "' OR '" + styleSet + "' = '') AND ST_INTERSECTS(areaCovered, (SELECT elementGeometry FROM StoredOsmElements WHERE privacyId = '" + elementId + "'))";
            db.Database.ExecuteSqlRaw(SQL);
        }

        /// <summary>
        /// Force SlippyMap tiles to expire and be redrawn on next access. Can be limited to a specific style set or run on all tiles.
        /// </summary>
        /// <param name="g">the area to expire intersecting maptiles with</param>
        /// <param name="styleSet">which set of SlippyMap tiles to expire. All tiles if this is an empty string</param>
        public static void ExpireSlippyMapTiles(Geometry g, string styleSet = "")
        {
            //If this would be faster as raw SQL, see function below for a template on how to write that.
            //TODO: test this logic, should be faster but 
            var db = new PraxisContext();
            //MariaDB SQL, should be functional
            string SQL = "UPDATE SlippyMapTiles SET ExpireOn = CURRENT_TIMESTAMP WHERE (styleSet= '" + styleSet + "' OR '" + styleSet + "' = '') AND ST_INTERSECTS(areaCovered, ST_GEOMFROMTEXT('" + g.AsText() + "'))";
            db.Database.ExecuteSqlRaw(SQL);
        }

        /// <summary>
        /// Force SlippyMap tiles to expire and be redrawn on next access. Can be limited to a specific style set or run on all tiles.
        /// </summary>
        /// <param name="elementId">the privacyID of a storedOsmElement to expire intersecting tiles for.</param>
        /// <param name="styleSet">which set of maptiles to expire. All tiles if this is an empty string</param>
        public static void ExpireSlippyMapTiles(Guid elementId, string styleSet = "")
        {
            //Might this be better off as raw SQL? If I expire, say, an entire state, that could be a lot of map tiles to pull into RAM just for a date to change.
            //var raw = "UPDATE SlippyMapTiles SET ExpireOn = CURRENT_TIMESTAMP WHERE ST_INTERSECTS(areaCovered, ST_GeomFromText(" + g.AsText() + "))";
            var db = new PraxisContext();
            string SQL = "UPDATE SlippyMapTiles SET ExpireOn = CURRENT_TIMESTAMP WHERE (styleSet = '" + styleSet + "' OR '" + styleSet + "' = '') AND ST_INTERSECTS(areaCovered, (SELECT elementGeometry FROM StoredOsmElements WHERE privacyId = '" + elementId + "'))";
            db.Database.ExecuteSqlRaw(SQL);
        }

        /// <summary>
        /// Take a path provided by a user, draw it as a maptile. Potentially useful for exercise trackers. Resulting file must not be saved to the server as that would be user tracking.
        /// </summary>
        /// <param name="pointListAsString">a string of points separate by , and | </param>
        /// <returns>the png file with the path drawn over the mapdata in the area.</returns>
        public static byte[] DrawUserPath(string pointListAsString)
        {
            //String is formatted as Lat,Lon~Lat,Lon~ repeating. Characters chosen to not be percent-encoded if submitted as part of the URL.
            //first, convert this to a list of latlon points
            string[] pointToConvert = pointListAsString.Split("|");
            List<Coordinate> coords = pointToConvert.Select(p => new Coordinate(double.Parse(p.Split(',')[0]), double.Parse(p.Split(',')[1]))).ToList();

            var mapBuffer = resolutionCell8 / 2; //Leave some area around the edges of where they went.
            GeoArea mapToDraw = new GeoArea(coords.Min(c => c.Y) - mapBuffer, coords.Min(c => c.X) - mapBuffer, coords.Max(c => c.Y) + mapBuffer, coords.Max(c => c.X) + mapBuffer);

            ImageStats info = new ImageStats(mapToDraw, 1024, 1024);

            LineString line = new LineString(coords.ToArray());
            var drawableLine = Converters.PolygonToSKPoints(line, mapToDraw, info.degreesPerPixelX, info.degreesPerPixelY);

            //Now, draw that path on the map.
            var places = GetPlaces(mapToDraw); //, null, false, false, degreesPerPixelX * 4 ///TODO: restore item filtering
            var baseImage = DrawAreaAtSize(info, places);

            SKBitmap sKBitmap = SKBitmap.Decode(baseImage);
            SKCanvas canvas = new SKCanvas(sKBitmap);
            SKPaint paint = new SKPaint();
            paint.Style = SKPaintStyle.Stroke;
            paint.StrokeWidth = 4; //Larger than normal lines at any zoom level.
            paint.Color = new SKColor(0, 0, 0); //Pure black, for maximum visibility.
            for (var x = 0; x < drawableLine.Length - 1; x++)
                canvas.DrawLine(drawableLine[x], drawableLine[x + 1], paint);

            var ms = new MemoryStream();
            var skms = new SKManagedWStream(ms);
            sKBitmap.Encode(skms, SKEncodedImageFormat.Png, 100);
            var results = ms.ToArray();
            skms.Dispose(); ms.Close(); ms.Dispose();
            return results;
        }

        /// <summary>
        /// A code reference for how big an image would be using 11-character PlusCodes for 1 pixel (or 2x2 if doubleRes is true)
        /// </summary>
        /// <param name="code">the code provided to determine image size</param>
        /// <param name="X">out param for image width</param>
        /// <param name="Y">out param for image height</param>
        /// <param name="doubleRes">if true, each Cell11 in the resulting image will be 2x2 pixels, or 1x1 if false.</param>
        public static void GetPlusCodeImagePixelSize(string code, out int X, out int Y, bool doubleRes = true)
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
                    X = 4 * 20 * 20;
                    Y = 5 * 20 * 20;
                    break;
                case 4: //This tends to break Skiasharp because of layering bitmaps to draw polygons with holes.
                    X = 4 * 20 * 20 * 20;
                    Y = 5 * 20 * 20 * 20;
                    break;
                default:
                    X = 0;
                    Y = 0;
                    break;
            }

            if (doubleRes)
            {
                X *= 2;
                Y *= 2;
            }
        }

        /// <summary>
        /// Get the image for a PlusCode. Can optionally draw in a specific style set.
        /// </summary>
        /// <param name="area">the PlusCode string to draw. Can be 6-11 digits long</param>
        /// <param name="styleSet">the TagParser style set to use when drawing</param>
        /// <param name="doubleRes">treat each Cell11 contained as 2x2 pixels when true, 1x1 when not.</param>
        /// <returns></returns>
        public static byte[] DrawPlusCode(string area, string styleSet = "mapTiles", bool doubleRes = true)
        {
            //This might be a cleaner version of my V4 function, for working with CellX sized tiles..
            //This will draw at a Cell11 resolution automatically.
            //Split it into a few functions.
            //then get all the area
            //TODO: make a version that just takes paintOps?

            int imgX = 0, imgY = 0;
            GetPlusCodeImagePixelSize(area, out imgX, out imgY, doubleRes);

            ImageStats info = new ImageStats(OpenLocationCode.DecodeValid(area), imgX, imgY);
            info.drawPoints = true;
            var places = GetPlacesForTile(info);
            var paintOps = GetPaintOpsForStoredElements(places, styleSet, info);
            return DrawAreaAtSize(info, paintOps, TagParser.GetStyleBgColor(styleSet));
        }

        /// <summary>
        /// Get the image for a PlusCode. Can optionally draw in a specific style set.
        /// </summary>
        /// <param name="area">the PlusCode string to draw. Can be 6-11 digits long</param>
        /// <param name="paintOps">the list of paint operations to run through for drawing</param>
        /// <param name="styleSet">the TagParser style set to use for determining the background color.</param>
        /// <param name="doubleRes">treat each Cell11 contained as 2x2 pixels when true, 1x1 when not.</param>
        /// <returns>a byte array for the png file of the pluscode image file</returns>
        public static byte[] DrawPlusCode(string area, List<CompletePaintOp> paintOps, string styleSet = "mapTiles", bool doubleRes = true)
        {
            //This might be a cleaner version of my V4 function, for working with CellX sized tiles..
            //This will draw at a Cell11 resolution automatically.
            //Split it into a few functions.
            //then get all the area

            int imgX = 0, imgY = 0;
            GetPlusCodeImagePixelSize(area, out imgX, out imgY, doubleRes);

            ImageStats info = new ImageStats(OpenLocationCode.DecodeValid(area), imgX, imgY);
            info.drawPoints = true;
            return DrawAreaAtSize(info, paintOps, TagParser.GetStyleBgColor(styleSet));
        }


        //Optional parameter allows you to pass in different stuff that the DB alone has, possibly for manual or one-off changes to styling
        //or other elements converted for maptile purposes.
        /// <summary>
        /// //This generic function takes the area to draw and creates an image for it. Can optionally be provided specific elements, a specific style set, and told to filter small areas out of the results.
        /// </summary>
        /// <param name="stats">Image information, including width and height.</param>
        /// <param name="drawnItems">the elments to draw</param>
        /// <param name="styleSet">the style rules to use when drawing</param>
        /// <param name="filterSmallAreas">if true, removes elements from the drawing that take up fewer than 8 pixels.</param>
        /// <returns></returns>
        public static byte[] DrawAreaAtSize(ImageStats stats, List<StoredOsmElement> drawnItems = null, string styleSet = null, bool filterSmallAreas = true)
        {
            //This is the new core drawing function. Takes in an area, the items to draw, and the size of the image to draw. 
            //The drawn items get their paint pulled from the TagParser's list. If I need multiple match lists, I'll need to make a way
            //to pick which list of tagparser rules to use.
            //This can work for user data by using the linked StoredOsmElements from the items in CustomDataStoredElement.
            //I need a slightly different function for using CustomDataPlusCode, or another optional parameter here

            Dictionary<string, TagParserEntry> styles;
            if (styleSet != null)
                styles = TagParser.allStyleGroups[styleSet];
            else
                styles = TagParser.allStyleGroups.First().Value;

            double minimumSize = 0;
            double minSizeSquared = 0;
            if (filterSmallAreas)
            {
                minimumSize = stats.degreesPerPixelX * 8; //don't draw small elements. THis runs on perimeter/length
                minSizeSquared = minimumSize * minimumSize;
            }

            //Single points are excluded separately so that small areas or lines can still be drawn when points aren't.
            bool includePoints = true;
            if (stats.degreesPerPixelX > ConstantValues.zoom14DegPerPixelX)
                includePoints = false;

            var db = new PraxisContext();
            var geo = Converters.GeoAreaToPolygon(stats.area);
            if (drawnItems == null)
                drawnItems = GetPlaces(stats.area, filterSize: minimumSize, includePoints: includePoints);

            //baseline image data stuff           
            SKBitmap bitmap = new SKBitmap(stats.imageSizeX, stats.imageSizeY, SKColorType.Rgba8888, SKAlphaType.Premul);
            SKCanvas canvas = new SKCanvas(bitmap);
            var bgColor = styles["background"].paintOperations.FirstOrDefault().paint; //Backgound is a named style, unmatched will be the last entry and transparent.
            canvas.Clear(bgColor.Color);
            canvas.Scale(1, -1, stats.imageSizeX / 2, stats.imageSizeY / 2);
            SKPaint paint = new SKPaint();

            var paintOps = GetPaintOpsForStoredElements(drawnItems, "mapTiles", stats);

            foreach (var w in paintOps.OrderByDescending(p => p.paintOp.layerId).ThenByDescending(p => p.areaSize))
            {
                paint = w.paintOp.paint;
                if (paint.Color.Alpha == 0)
                    continue; //This area is transparent, skip drawing it entirely.

                var path = new SKPath();
                path.FillType = SKPathFillType.EvenOdd;
                switch (w.elementGeometry.GeometryType)
                {
                    case "Polygon":
                        var p = w.elementGeometry as Polygon;
                        path.AddPoly(Converters.PolygonToSKPoints(p.ExteriorRing, stats.area, stats.degreesPerPixelX, stats.degreesPerPixelY));
                        foreach (var hole in p.InteriorRings)
                            path.AddPoly(Converters.PolygonToSKPoints(hole, stats.area, stats.degreesPerPixelX, stats.degreesPerPixelY));
                        canvas.DrawPath(path, paint);

                        //Old, slower code. Commented out pending further testing to confirm the new logic doesn't create new issues.
                        //if (p.Holes.Length == 0)
                        //{
                        //    path.AddPoly(Converters.PolygonToSKPoints(p, stats.area, stats.degreesPerPixelX, stats.degreesPerPixelY));
                        //    canvas.DrawPath(path, paint);
                        //}
                        //else
                        //{
                        //    var innerBitmap = DrawPolygon(p, paint, stats);
                        //    canvas.DrawBitmap(innerBitmap, 0, 0, paint);
                        //}
                        break;
                    case "MultiPolygon":
                        foreach (var p2 in ((MultiPolygon)w.elementGeometry).Geometries)
                        {
                            var p2p = p2 as Polygon;
                            path.AddPoly(Converters.PolygonToSKPoints(p2p.ExteriorRing, stats.area, stats.degreesPerPixelX, stats.degreesPerPixelY));
                            foreach (var hole in p2p.InteriorRings)
                                path.AddPoly(Converters.PolygonToSKPoints(hole, stats.area, stats.degreesPerPixelX, stats.degreesPerPixelY));
                            canvas.DrawPath(path, paint);

                            //Old, slower code. Commented out pending further testing to confirm the new logic doesn't create new issues.
                            //if (p.Holes.Length == 0)
                            //{
                            //    path.AddPoly(Converters.PolygonToSKPoints(p, stats.area, stats.degreesPerPixelX, stats.degreesPerPixelY));
                            //    canvas.DrawPath(path, paint);
                            //}
                            //else
                            //{
                            //    var innerBitmap = DrawPolygon(p, paint, stats);
                            //    canvas.DrawBitmap(innerBitmap, 0, 0, paint);
                            //}
                        }
                        break;
                    case "LineString":
                        var firstPoint = w.elementGeometry.Coordinates.First();
                        var lastPoint = w.elementGeometry.Coordinates.Last();
                        var points = Converters.PolygonToSKPoints(w.elementGeometry, stats.area, stats.degreesPerPixelX, stats.degreesPerPixelY);
                        if (firstPoint.Equals(lastPoint))
                        {
                            //This is a closed shape. Check to see if it's supposed to be filled in.
                            if (paint.Style == SKPaintStyle.Fill)
                            {
                                path.AddPoly(points);
                                canvas.DrawPath(path, paint);
                                continue;
                            }
                        }
                        for (var line = 0; line < points.Length - 1; line++)
                            canvas.DrawLine(points[line], points[line + 1], paint);
                        break;
                    case "MultiLineString":
                        foreach (var p3 in ((MultiLineString)w.elementGeometry).Geometries)
                        {
                            //TODO: might want to see if I need to move the LineString logic here, or if multiline string are never filled areas.
                            var points2 = Converters.PolygonToSKPoints(p3, stats.area, stats.degreesPerPixelX, stats.degreesPerPixelY);
                            for (var line = 0; line < points2.Length - 1; line++)
                                canvas.DrawLine(points2[line], points2[line + 1], paint);
                        }
                        break;
                    case "Point":
                        var convertedPoint = Converters.PolygonToSKPoints(w.elementGeometry, stats.area, stats.degreesPerPixelX, stats.degreesPerPixelY);
                        //If this type has an icon, use it. Otherwise draw a circle in that type's color.
                        if (!string.IsNullOrEmpty(w.paintOp.fileName))
                        {
                            SKBitmap icon = TagParser.cachedBitmaps[w.paintOp.fileName];
                            canvas.DrawBitmap(icon, convertedPoint[0]);
                        }
                        else
                        {
                            var circleRadius = (float)(ConstantValues.resolutionCell10 / stats.degreesPerPixelX / 2); //I want points to be drawn as 1 Cell10 in diameter.
                            canvas.DrawCircle(convertedPoint[0], circleRadius, paint);
                            canvas.DrawCircle(convertedPoint[0], circleRadius, styles["outline"].paintOperations.First().paint); //TODO: this forces an outline style to be present in the list or this crashes.
                        }
                        break;
                    default:
                        Log.WriteLog("Unknown geometry type found, not drawn.");
                        break;
                }
            }
            //}

            var ms = new MemoryStream();
            var skms = new SKManagedWStream(ms);
            bitmap.Encode(skms, SKEncodedImageFormat.Png, 100);
            var results = ms.ToArray();
            skms.Dispose(); ms.Close(); ms.Dispose();
            return results;
        }

        public static byte[] DrawAreaAtSize(ImageStats stats, List<CompletePaintOp> paintOps, SKColor bgColor)
        {
            //This is the new core drawing function. Once the paint operations have been created, I just draw them here.
            //baseline image data stuff           
            SKBitmap bitmap = new SKBitmap(stats.imageSizeX, stats.imageSizeY, SKColorType.Rgba8888, SKAlphaType.Premul);
            SKCanvas canvas = new SKCanvas(bitmap);
            canvas.Clear(bgColor);
            canvas.Scale(1, -1, stats.imageSizeX / 2, stats.imageSizeY / 2);
            SKPaint paint = new SKPaint();

            foreach (var w in paintOps.OrderByDescending(p => p.paintOp.layerId).ThenByDescending(p => p.areaSize))
            {
                paint = w.paintOp.paint;

                if (w.paintOp.fromTag) //FromTag is for when you are saving color data directly to each element, instead of tying it to a styleset.
                    paint.Color = SKColor.Parse(w.tagValue);

                if (paint.Color.Alpha == 0)
                    continue; //This area is transparent, skip drawing it entirely.

                if (w.paintOp.randomize) //To randomize the color on every Draw call.
                    paint.Color = new SKColor((byte)r.Next(0, 255), (byte)r.Next(0, 255), (byte)r.Next(0, 255), 99);

                var path = new SKPath();
                switch (w.elementGeometry.GeometryType)
                {
                    //Polygons without holes are super easy and fast: draw the path.
                    //Polygons with holes require their own bitmap to be drawn correctly and then overlaid onto the canvas.
                    //I want to use paths to fix things for performance reasons, but I have to use Bitmaps because paths apply their blend mode to
                    //ALL elements already drawn, not just the last one.
                    case "Polygon":
                        var p = w.elementGeometry as Polygon;
                        path.AddPoly(Converters.PolygonToSKPoints(p.ExteriorRing, stats.area, stats.degreesPerPixelX, stats.degreesPerPixelY));
                        foreach (var ir in p.Holes)
                            path.AddPoly(Converters.PolygonToSKPoints(ir, stats.area, stats.degreesPerPixelX, stats.degreesPerPixelY));
                        canvas.DrawPath(path, paint);

                        //if (p.Holes.Length == 0)
                        //{
                        //    path.AddPoly(Converters.PolygonToSKPoints(p, stats.area, stats.degreesPerPixelX, stats.degreesPerPixelY));
                        //    canvas.DrawPath(path, paint);
                        //}
                        //else
                        //{
                        //    var innerBitmap = DrawPolygon((Polygon)w.elementGeometry, paint, stats);
                        //    canvas.DrawBitmap(innerBitmap, 0, 0, paint);
                        //}
                        break;
                    case "MultiPolygon": 
                        foreach (var p2 in ((MultiPolygon)w.elementGeometry).Geometries)
                        {
                            var p2p = p2 as Polygon;
                            path.AddPoly(Converters.PolygonToSKPoints(p2p.ExteriorRing, stats.area, stats.degreesPerPixelX, stats.degreesPerPixelY));
                            foreach (var ir in p2p.Holes)
                                path.AddPoly(Converters.PolygonToSKPoints(ir, stats.area, stats.degreesPerPixelX, stats.degreesPerPixelY));
                            canvas.DrawPath(path, paint);

                            //if (p2p.Holes.Length == 0)
                            //{
                            //    path.AddPoly(Converters.PolygonToSKPoints(p2p, stats.area, stats.degreesPerPixelX, stats.degreesPerPixelY));
                            //    canvas.DrawPath(path, paint);
                            //}
                            //else
                            //{
                            //    var innerBitmap = DrawPolygon(p2p, paint, stats);
                            //    canvas.DrawBitmap(innerBitmap, 0, 0, paint);
                            //}
                        }
                        break;
                    case "LineString":
                        var firstPoint = w.elementGeometry.Coordinates.First();
                        var lastPoint = w.elementGeometry.Coordinates.Last();
                        var points = Converters.PolygonToSKPoints(w.elementGeometry, stats.area, stats.degreesPerPixelX, stats.degreesPerPixelY);
                        if (firstPoint.Equals(lastPoint))
                        {
                            //This is a closed shape. Check to see if it's supposed to be filled in.
                            if (paint.Style == SKPaintStyle.Fill)
                            {
                                path.AddPoly(points);
                                canvas.DrawPath(path, paint);
                                continue;
                            }
                        }
                        for (var line = 0; line < points.Length - 1; line++)
                            canvas.DrawLine(points[line], points[line + 1], paint);
                        break;
                    case "MultiLineString":
                        foreach (var p3 in ((MultiLineString)w.elementGeometry).Geometries)
                        {
                            //TODO: might want to see if I need to move the LineString logic here, or if multiline string are never filled areas.
                            var points2 = Converters.PolygonToSKPoints(p3, stats.area, stats.degreesPerPixelX, stats.degreesPerPixelY);
                            for (var line = 0; line < points2.Length - 1; line++)
                                canvas.DrawLine(points2[line], points2[line + 1], paint);
                        }
                        break;
                    case "Point":
                        var convertedPoint = Converters.PolygonToSKPoints(w.elementGeometry, stats.area, stats.degreesPerPixelX, stats.degreesPerPixelY);
                        //If this type has an icon, use it. Otherwise draw a circle in that type's color.
                        if (!string.IsNullOrEmpty(w.paintOp.fileName))
                        {
                            SKBitmap icon = TagParser.cachedBitmaps[w.paintOp.fileName];
                            canvas.DrawBitmap(icon, convertedPoint[0]);
                        }
                        else
                        {
                            var circleRadius = (float)(ConstantValues.resolutionCell10 / stats.degreesPerPixelX / 2); //I want points to be drawn as 1 Cell10 in diameter.
                            canvas.DrawCircle(convertedPoint[0], circleRadius, paint);
                            //canvas.DrawCircle(convertedPoint[0], circleRadius, styles["outline"].paintOperations.First().paint); //TODO: this needs to be a PaintOp of its own, like roads and buildings have
                        }
                        break;
                    default:
                        Log.WriteLog("Unknown geometry type found, not drawn.");
                        break;
                }
            }
            //}

            var ms = new MemoryStream();
            var skms = new SKManagedWStream(ms);
            bitmap.Encode(skms, SKEncodedImageFormat.Png, 100);
            var results = ms.ToArray();
            skms.Dispose(); ms.Close(); ms.Dispose();
            return results;
        }

        /// <summary>
        /// Creates the list of paint commands for the given elements, styles, and image area.
        /// </summary>
        /// <param name="elements">the list of StoredOsmElements to be drawn</param>
        /// <param name="styleSet">the style set to use when drwaing the elements</param>
        /// <param name="stats">the info on the resulting image for calculating ops.</param>
        /// <returns>a list of CompletePaintOps to be passed into a DrawArea function</returns>
        public static List<CompletePaintOp> GetPaintOpsForStoredElements(List<StoredOsmElement> elements, string styleSet, ImageStats stats)
        {
            var styles = TagParser.allStyleGroups[styleSet];
            var pass1 = elements.Select(d => new { d.AreaSize, d.elementGeometry, paintOp = styles[d.GameElementName].paintOperations });
            var pass2 = new List<CompletePaintOp>();
            foreach (var op in pass1)
                foreach (var po in op.paintOp)
                    if (stats.degreesPerPixelX < po.maxDrawRes && stats.degreesPerPixelX > po.minDrawRes) //dppX should be between max and min draw range.
                        pass2.Add(new CompletePaintOp(op.elementGeometry, op.AreaSize, po, ""));

            return pass2;
        }

        /// <summary>
        /// Creates the list of paint commands for the elements intersecting the given area, with the given data key attached to OSM elements and style set, for the image.
        /// </summary>
        /// <param name="area">a Polygon covering the area to draw. Intended to match the ImageStats GeoArea. May be removed in favor of using the ImageStats GeoArea later. </param>
        /// <param name="dataKey">the key to pull data from attached to any Osm Elements intersecting the area</param>
        /// <param name="styleSet">the style set to use when drawing the intersecting elements</param>
        /// <param name="stats">the info on the resulting image for calculating ops.</param>
        /// <returns>a list of CompletePaintOps to be passed into a DrawArea function</returns>
        public static List<CompletePaintOp> GetPaintOpsForCustomDataElements(Geometry area, string dataKey, string styleSet, ImageStats stats)
        {
            //NOTE: styleSet must == dataKey for this to work. Or should I just add that to this function?
            var db = new PraxisContext();
            var elements = db.CustomDataOsmElements.Include(d => d.storedOsmElement).Where(d => d.dataKey == dataKey && area.Intersects(d.storedOsmElement.elementGeometry)).ToList();
            var styles = TagParser.allStyleGroups[styleSet];
            var pass1 = elements.Select(d => new { d.storedOsmElement.AreaSize, d.storedOsmElement.elementGeometry, paintOp = styles[d.dataValue].paintOperations, d.dataValue });
            var pass2 = new List<CompletePaintOp>();
            foreach (var op in pass1)
                foreach (var po in op.paintOp)
                    if (stats.degreesPerPixelX < po.maxDrawRes && stats.degreesPerPixelX > po.minDrawRes) //dppX should be between max and min draw range.
                        pass2.Add(new CompletePaintOp(op.elementGeometry, op.AreaSize, po, op.dataValue));

            return pass2;
        }

        //This function only works if all the dataValue entries are a key in styles
        /// <summary>
        /// Creates the list of paint commands for the PlusCode cells intersecting the given area, with the given data key and style set, for the image.
        /// </summary>
        /// <param name="area">a Polygon covering the area to draw. Intended to match the ImageStats GeoArea. May be removed in favor of using the ImageStats GeoArea later. </param>
        /// <param name="dataKey">the key to pull data from attached to any Osm Elements intersecting the area</param>
        /// <param name="styleSet">the style set to use when drawing the intersecting elements</param>
        /// <param name="stats">the info on the resulting image for calculating ops.</param>
        /// <returns>a list of CompletePaintOps to be passed into a DrawArea function</returns>
        public static List<CompletePaintOp> GetPaintOpsForCustomDataPlusCodes(Geometry area, string dataKey, string styleSet, ImageStats stats)
        {
            var db = new PraxisContext();
            var elements = db.CustomDataPlusCodes.Where(d => d.dataKey == dataKey && area.Intersects(d.geoAreaIndex)).ToList();
            var styles = TagParser.allStyleGroups[styleSet];
            var pass1 = elements.Select(d => new { d.geoAreaIndex.Area, d.geoAreaIndex, paintOp = styles[d.dataValue].paintOperations, d.dataValue });
            var pass2 = new List<CompletePaintOp>();
            foreach (var op in pass1)
                foreach (var po in op.paintOp)
                    if (stats.degreesPerPixelX < po.maxDrawRes && stats.degreesPerPixelX > po.minDrawRes) //dppX should be between max and min draw range.
                        pass2.Add(new CompletePaintOp(op.geoAreaIndex, op.Area, po, op.dataValue));

            return pass2;
        }

        //Allows for 1 style to pull a color from the custom data value.
        /// <summary>
        /// Creates the list of paint commands for the PlusCode cells intersecting the given area, with the given data key and style set, for the image. In this case, the color will be the tag's value.
        /// </summary>
        /// <param name="area">a Polygon covering the area to draw. Intended to match the ImageStats GeoArea. May be removed in favor of using the ImageStats GeoArea later. </param>
        /// <param name="dataKey">the key to pull data from attached to any Osm Elements intersecting the area</param>
        /// <param name="styleSet">the style set to use when drawing the intersecting elements</param>
        /// <param name="stats">the info on the resulting image for calculating ops.</param>
        /// <returns>a list of CompletePaintOps to be passed into a DrawArea function</returns>
        public static List<CompletePaintOp> GetPaintOpsForCustomDataPlusCodesFromTagValue(Geometry area, string dataKey, string styleSet, ImageStats stats)
        {
            var db = new PraxisContext();
            var elements = db.CustomDataPlusCodes.Where(d => d.dataKey == dataKey && area.Intersects(d.geoAreaIndex)).ToList();
            var styles = TagParser.allStyleGroups[styleSet];
            var pass1 = elements.Select(d => new { d.geoAreaIndex.Area, d.geoAreaIndex, paintOp = styles["tag"].paintOperations, d.dataValue });
            var pass2 = new List<CompletePaintOp>();
            foreach (var op in pass1)
                foreach (var po in op.paintOp)
                    if (stats.degreesPerPixelX < po.maxDrawRes && stats.degreesPerPixelX > po.minDrawRes) //dppX should be between max and min draw range.
                        pass2.Add(new CompletePaintOp(op.geoAreaIndex, op.Area, po, op.dataValue));

            return pass2;
        }

        /// <summary>
        /// Creates an SVG image instead of a PNG file, but otherwise operates the same as DrawAreaAtSize.
        /// </summary>
        /// <param name="stats">the image properties to draw</param>
        /// <param name="drawnItems">the list of elements to draw. Will load from the database if null.</param>
        /// <param name="styles">a dictionary of TagParserEntries to select to draw</param>
        /// <param name="filterSmallAreas">if true, skips entries below a certain size when drawing.</param>
        /// <returns>a string containing the SVG XML</returns>
        public static string DrawAreaAtSizeSVG(ImageStats stats, List<StoredOsmElement> drawnItems = null, Dictionary<string, TagParserEntry> styles = null, bool filterSmallAreas = true)
        {
            //TODO: make this take CompletePaintOps
            //This is the new core drawing function. Takes in an area, the items to draw, and the size of the image to draw. 
            //The drawn items get their paint pulled from the TagParser's list. If I need multiple match lists, I'll need to make a way
            //to pick which list of tagparser rules to use.

            if (styles == null)
                styles = TagParser.allStyleGroups.First().Value;

            double minimumSize = 0;
            if (filterSmallAreas)
                minimumSize = stats.degreesPerPixelX; //don't draw elements under 1 pixel in size. at slippy zoom 12, this is approx. 1 pixel for a Cell10.

            var db = new PraxisContext();
            var geo = Converters.GeoAreaToPolygon(stats.area);
            if (drawnItems == null)
                drawnItems = GetPlaces(stats.area, filterSize: minimumSize);

            //baseline image data stuff           
            //SKBitmap bitmap = new SKBitmap(stats.imageSizeX, stats.imageSizeY, SKColorType.Rgba8888, SKAlphaType.Premul);
            var bounds = new SKRect(0, 0, stats.imageSizeX, stats.imageSizeY);
            MemoryStream s = new MemoryStream();
            SKCanvas canvas = SKSvgCanvas.Create(bounds, s); //output not guaranteed to be complete until the canvas is deleted?!?
            //SKCanvas canvas = new SKCanvas(bitmap);
            var bgColor = styles["background"].paintOperations.FirstOrDefault().paint; //Backgound is a named style, unmatched will be the last entry and transparent.
            canvas.Clear(bgColor.Color);
            canvas.Scale(1, -1, stats.imageSizeX / 2, stats.imageSizeY / 2);
            SKPaint paint = new SKPaint();

            //I guess what I want here is a list of an object with an elementGeometry object for the shape, and a paintOp attached to it
            var pass1 = drawnItems.Select(d => new { d.AreaSize, d.elementGeometry, paintOp = styles[d.GameElementName].paintOperations });
            var pass2 = new List<CompletePaintOp>();
            foreach (var op in pass1)
                foreach (var po in op.paintOp)
                    pass2.Add(new CompletePaintOp(op.elementGeometry, op.AreaSize, po, ""));


            foreach (var w in pass2.OrderByDescending(p => p.paintOp.layerId).ThenByDescending(p => p.areaSize))
            {
                paint = w.paintOp.paint;
                if (paint.Color.Alpha == 0)
                    continue; //This area is transparent, skip drawing it entirely.

                //TODO: uncomment this once paint types have values assigned.                
                if (stats.degreesPerPixelX > w.paintOp.maxDrawRes || stats.degreesPerPixelX < w.paintOp.minDrawRes)
                    continue; //This area isn't drawn at this scale.

                var path = new SKPath();
                switch (w.elementGeometry.GeometryType)
                {
                    //Polygons without holes are super easy and fast: draw the path.
                    //Polygons with holes require their own bitmap to be drawn correctly and then overlaid onto the canvas.
                    //I want to use paths to fix things for performance reasons, but I have to use Bitmaps because paths apply their blend mode to
                    //ALL elements already drawn, not just the last one.
                    case "Polygon":
                        var p = w.elementGeometry as Polygon;

                        path.AddPoly(Converters.PolygonToSKPoints(p, stats.area, stats.degreesPerPixelX, stats.degreesPerPixelY));
                        canvas.DrawPath(path, paint);

                        foreach (var hole in p.InteriorRings)
                        {
                            path = new SKPath();
                            path.AddPoly(Converters.PolygonToSKPoints(hole, stats.area, stats.degreesPerPixelX, stats.degreesPerPixelY));
                            canvas.DrawPath(path, eraser);
                        }

                        break;
                    case "MultiPolygon":
                        foreach (var p2 in ((MultiPolygon)w.elementGeometry).Geometries)
                        {
                            var p2p = p2 as Polygon;

                            path.AddPoly(Converters.PolygonToSKPoints(p2p, stats.area, stats.degreesPerPixelX, stats.degreesPerPixelY));
                            canvas.DrawPath(path, paint);

                            foreach (var hole in p2p.InteriorRings)
                            {
                                path = new SKPath();
                                path.AddPoly(Converters.PolygonToSKPoints(hole, stats.area, stats.degreesPerPixelX, stats.degreesPerPixelY));
                                canvas.DrawPath(path, eraser);
                            }

                        }
                        break;
                    case "LineString":
                        var firstPoint = w.elementGeometry.Coordinates.First();
                        var lastPoint = w.elementGeometry.Coordinates.Last();
                        var points = Converters.PolygonToSKPoints(w.elementGeometry, stats.area, stats.degreesPerPixelX, stats.degreesPerPixelY);
                        if (firstPoint.Equals(lastPoint))
                        {
                            //This is a closed shape. Check to see if it's supposed to be filled in.
                            if (paint.Style == SKPaintStyle.Fill)
                            {
                                path.AddPoly(points);
                                canvas.DrawPath(path, paint);
                                continue;
                            }
                        }
                        for (var line = 0; line < points.Length - 1; line++)
                            canvas.DrawLine(points[line], points[line + 1], paint);
                        break;
                    case "MultiLineString":
                        foreach (var p3 in ((MultiLineString)w.elementGeometry).Geometries)
                        {
                            //TODO: might want to see if I need to move the LineString logic here, or if multiline string are never filled areas.
                            var points2 = Converters.PolygonToSKPoints(p3, stats.area, stats.degreesPerPixelX, stats.degreesPerPixelY);
                            for (var line = 0; line < points2.Length - 1; line++)
                                canvas.DrawLine(points2[line], points2[line + 1], paint);
                        }
                        break;
                    case "Point":
                        var convertedPoint = Converters.PolygonToSKPoints(w.elementGeometry, stats.area, stats.degreesPerPixelX, stats.degreesPerPixelY);
                        //If this type has an icon, use it. Otherwise draw a circle in that type's color.
                        if (!string.IsNullOrEmpty(w.paintOp.fileName))
                        {
                            SKBitmap icon = TagParser.cachedBitmaps[w.paintOp.fileName];
                            canvas.DrawBitmap(icon, convertedPoint[0]);
                        }
                        else
                        {
                            var circleRadius = (float)(ConstantValues.resolutionCell10 / stats.degreesPerPixelX / 2); //I want points to be drawn as 1 Cell10 in diameter.
                            canvas.DrawCircle(convertedPoint[0], circleRadius, paint);
                        }
                        break;
                    default:
                        Log.WriteLog("Unknown geometry type found, not drawn.");
                        break;
                }
            }
            canvas.Flush();
            canvas.Dispose();
            canvas = null;
            //var skms = new SKManagedWStream(ms);
            s.Position = 0;
            var svgData = new StreamReader(s).ReadToEnd();
            //var results = ms.ToArray();
            //skms.Dispose(); ms.Close(); ms.Dispose();
            return svgData;
        }

        /// <summary>
        /// Creates all gameplay tiles for a given area and saves them to the database.
        /// </summary>
        /// <param name="buffered">the GeoArea of the area to create tiles for.</param>
        public static void PregenMapTilesForArea(GeoArea buffered)
        {
            //There is a very similar function for this in Standalone.cs, but this one writes back to the main DB.
            var db = new PraxisContext();
            var intersectCheck = Converters.GeoAreaToPolygon(buffered);
            //start drawing maptiles and sorting out data.
            var swCorner = new OpenLocationCode(intersectCheck.EnvelopeInternal.MinY, intersectCheck.EnvelopeInternal.MinX);
            var neCorner = new OpenLocationCode(intersectCheck.EnvelopeInternal.MaxY, intersectCheck.EnvelopeInternal.MaxX);

            //declare how many map tiles will be drawn
            var xTiles = buffered.LongitudeWidth / resolutionCell8;
            var yTiles = buffered.LatitudeHeight / resolutionCell8;
            var totalTiles = Math.Truncate(xTiles * yTiles);

            Log.WriteLog("Starting processing maptiles for " + totalTiles + " Cell8 areas.");
            long mapTileCounter = 0;
            System.Diagnostics.Stopwatch progressTimer = new System.Diagnostics.Stopwatch();
            progressTimer.Start();

            //now, for every Cell8 involved, draw and name it.
            //This is tricky to run in parallel because it's not smooth increments
            var yCoords = new List<double>();
            var yVal = swCorner.Decode().SouthLatitude;
            while (yVal <= neCorner.Decode().NorthLatitude)
            {
                yCoords.Add(yVal);
                yVal += resolutionCell8;
            }

            var xCoords = new List<double>();
            var xVal = swCorner.Decode().WestLongitude;
            while (xVal <= neCorner.Decode().EastLongitude)
            {
                xCoords.Add(xVal);
                xVal += resolutionCell8;
            }

            foreach (var y in yCoords)
            {
                //Make a collision box for just this row of Cell8s, and send the loop below just the list of things that might be relevant.
                //Add a Cell8 buffer space so all elements are loaded and drawn without needing to loop through the entire area.
                GeoArea thisRow = new GeoArea(y - ConstantValues.resolutionCell8, xCoords.First() - ConstantValues.resolutionCell8, y + ConstantValues.resolutionCell8 + ConstantValues.resolutionCell8, xCoords.Last() + resolutionCell8);
                var row = Converters.GeoAreaToPolygon(thisRow);
                var rowList = GetPlaces(thisRow);
                var tilesToSave = new ConcurrentBag<MapTile>();

                Parallel.ForEach(xCoords, x =>
                //foreach (var x in xCoords)
                {
                    //make map tile.
                    var plusCode = new OpenLocationCode(y, x, 10);
                    var plusCode8 = plusCode.CodeDigits.Substring(0, 8);
                    var plusCodeArea = OpenLocationCode.DecodeValid(plusCode8);

                    var areaForTile = new GeoArea(new GeoPoint(plusCodeArea.SouthLatitude, plusCodeArea.WestLongitude), new GeoPoint(plusCodeArea.NorthLatitude, plusCodeArea.EastLongitude));
                    var acheck = Converters.GeoAreaToPolygon(areaForTile); //this is faster than using a PreparedPolygon in testing, which was unexpected.
                    var areaList = rowList.Where(a => acheck.Intersects(a.elementGeometry)).ToList(); //This one is for the maptile

                    //Create the maptile first, so if we save it to the DB/a file we can call the lock once per loop.
                    //NOTE: this should use DrawPlusCode instead and double the res.
                    var info = new ImageStats(areaForTile, 160, 200); //Each pixel is a Cell11, we're drawing a Cell8. For Cell6 testing this is 1600x2000, just barely within android limits
                    var tile = MapTiles.DrawAreaAtSize(info, areaList);
                    tilesToSave.Add(new MapTile() { tileData = tile, PlusCode = plusCode.CodeDigits.Substring(0, 8), CreatedOn = DateTime.Now, ExpireOn = DateTime.Now.AddDays(365 * 10), areaCovered = Converters.GeoAreaToPolygon(plusCodeArea), resolutionScale = 11, styleSet = "mapTiles" });

                    mapTileCounter++;
                });
                db.MapTiles.AddRange(tilesToSave);
                db.SaveChanges();
                Log.WriteLog(mapTileCounter + " tiles processed, " + Math.Round((mapTileCounter / totalTiles) * 100, 2) + "% complete, " + Math.Round(mapTileCounter / progressTimer.Elapsed.TotalSeconds, 2) + " tiles per second.");

            }//);
            progressTimer.Stop();
            Log.WriteLog("Area map tiles drawn in " + progressTimer.Elapsed.ToString());

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
                //Parallel.ForEach(xCoords, x =>
                //foreach (var x in xCoords)
                {
                    //make map tile.
                    var info = new ImageStats(zoomLevel, x, y, MapTileSizeSquare);
                    var acheck = Converters.GeoAreaToPolygon(info.area); //this is faster than using a PreparedPolygon in testing, which was unexpected.
                    var areaList = rowList.Where(a => acheck.Intersects(a.elementGeometry)).ToList(); //This one is for the maptile

                    var tile = MapTiles.DrawAreaAtSize(info, areaList);
                    tilesToSave.Add(new SlippyMapTile() { tileData = tile, Values = x + "|" + y + "|" + zoomLevel, CreatedOn = DateTime.Now, ExpireOn = DateTime.Now.AddDays(365 * 10), areaCovered = Converters.GeoAreaToPolygon(info.area), styleSet = "mapTiles" });

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
        /// Combines 2 images into one, given the shared ImageStats for both supplied images.
        /// </summary>
        /// <param name="info">the ImageStats object used to generate both bottom and top tiles.</param>
        /// <param name="bottomTile">the tile to use as the base of the image. Expected to be opaque.</param>
        /// <param name="topTile">The tile to layer on top. Expected to be at least partly transparent or translucent.</param>
        /// <returns></returns>
        public static byte[] LayerTiles(ImageStats info, byte[] bottomTile, byte[] topTile)
        {
            SkiaSharp.SKBitmap bitmap = new SkiaSharp.SKBitmap(info.imageSizeX, info.imageSizeY, SkiaSharp.SKColorType.Rgba8888, SkiaSharp.SKAlphaType.Premul);
            SkiaSharp.SKCanvas canvas = new SkiaSharp.SKCanvas(bitmap);
            SkiaSharp.SKPaint paint = new SkiaSharp.SKPaint();
            canvas.Scale(1, 1, info.imageSizeX / 2, info.imageSizeY / 2);
            paint.IsAntialias = true;

            var baseBmp = SkiaSharp.SKBitmap.Decode(bottomTile);
            var topBmp = SkiaSharp.SKBitmap.Decode(topTile);
            canvas.DrawBitmap(baseBmp, 0, 0);
            canvas.DrawBitmap(topBmp, 0, 0);
            var ms = new MemoryStream();
            var skms = new SkiaSharp.SKManagedWStream(ms);
            bitmap.Encode(skms, SkiaSharp.SKEncodedImageFormat.Png, 100);
            var output = ms.ToArray();
            skms.Dispose(); ms.Close(); ms.Dispose();
            return output;
        }
    }
}