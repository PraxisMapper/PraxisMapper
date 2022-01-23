using Google.OpenLocationCode;
using NetTopologySuite.Geometries;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using static PraxisCore.ConstantValues;
using static PraxisCore.DbTables;
using static PraxisCore.Place;
using SkiaSharp;
using PraxisCore.Support;
using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;

namespace PraxisCore
{
    /// <summary>
    /// All functions related to generating or expiring map tiles. Both PlusCode sized tiles for gameplay or SlippyMap tiles for a webview.
    /// </summary>
    public class MapTiles : IMapTiles
    {
        public static int MapTileSizeSquare = 512; //Default value, updated by PraxisMapper at startup. COvers Slippy tiles, not gameplay tiles.
        public static double GameTileScale = 2;
        public static double bufferSize = resolutionCell10; //How much space to add to an area to make sure elements are drawn correctly. Mostly to stop points from being clipped.
        static SKPaint eraser = new SKPaint() { Color = SKColors.Transparent, BlendMode = SKBlendMode.Src, Style = SKPaintStyle.StrokeAndFill }; //BlendMode is the important part for an Eraser.
        static Random r = new Random();

        public GeoArea MakeBufferedGeoArea(GeoArea original)
        {
            return new GeoArea(original.SouthLatitude - bufferSize, original.WestLongitude - bufferSize, original.NorthLatitude + bufferSize, original.EastLongitude + bufferSize);
        }

        /// <summary>
        /// Draw square boxes around each area to approximate how they would behave in an offline app
        /// </summary>
        /// <param name="info">the image information for drawing</param>
        /// <param name="items">the elements to draw.</param>
        /// <returns>byte array of the generated .png tile image</returns>
        public byte[] DrawOfflineEstimatedAreas(ImageStats info, List<StoredOsmElement> items)
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
                var rect = PlaceInfoToRect(pi, info);
                fillpaint.Color = TagParser.PickStaticColorForArea(pi.Name);
                canvas.DrawRect(rect, fillpaint);
                canvas.DrawRect(rect, strokePaint);
            }

            canvas.Scale(1, -1, info.imageSizeX / 2, info.imageSizeY / 2); //inverts the inverted image again!
            foreach (var pi in placeInfo)
            {
                var rect = PlaceInfoToRect(pi, info);
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
        public byte[] DrawCell8GridLines(GeoArea totalArea)
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
                var points = PolygonToSKPoints(geoLine, totalArea, degreesPerPixelX, degreesPerPixelY);
                canvas.DrawLine(points[0], points[1], paint);
                lonLineTrackerDegrees += resolutionCell8;
            }

            double latLineTrackerDegrees = imageBottom - spaceToFirstLineBottom; //This is degree coords
            while (latLineTrackerDegrees <= totalArea.NorthLatitude + resolutionCell8) //This means we should always draw at least 2 lines, even if they're off-canvas.
            {
                var geoLine = new LineString(new Coordinate[] { new Coordinate(180, latLineTrackerDegrees), new Coordinate(-180, latLineTrackerDegrees) });
                var points = PolygonToSKPoints(geoLine, totalArea, degreesPerPixelX, degreesPerPixelY);
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
        public byte[] DrawCell10GridLines(GeoArea totalArea)
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
                var points = PolygonToSKPoints(geoLine, totalArea, degreesPerPixelX, degreesPerPixelY);
                canvas.DrawLine(points[0], points[1], paint);
                lonLineTrackerDegrees += resolutionCell10;
            }

            double latLineTrackerDegrees = imageBottom - spaceToFirstLineBottom; //This is degree coords
            while (latLineTrackerDegrees <= totalArea.NorthLatitude + resolutionCell10) //This means we should always draw at least 2 lines, even if they're off-canvas.
            {
                var geoLine = new LineString(new Coordinate[] { new Coordinate(180, latLineTrackerDegrees), new Coordinate(-180, latLineTrackerDegrees) });
                var points = PolygonToSKPoints(geoLine, totalArea, degreesPerPixelX, degreesPerPixelY);
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
        /// Take a path provided by a user, draw it as a maptile. Potentially useful for exercise trackers. Resulting file must not be saved to the server as that would be user tracking.
        /// </summary>
        /// <param name="pointListAsString">a string of points separate by , and | </param>
        /// <returns>the png file with the path drawn over the mapdata in the area.</returns>
        public byte[] DrawUserPath(string pointListAsString)
        {
            //String is formatted as Lat,Lon~Lat,Lon~ repeating. Characters chosen to not be percent-encoded if submitted as part of the URL.
            //first, convert this to a list of latlon points
            string[] pointToConvert = pointListAsString.Split("|");
            List<Coordinate> coords = pointToConvert.Select(p => new Coordinate(double.Parse(p.Split(',')[0]), double.Parse(p.Split(',')[1]))).ToList();

            var mapBuffer = resolutionCell8 / 2; //Leave some area around the edges of where they went.
            GeoArea mapToDraw = new GeoArea(coords.Min(c => c.Y) - mapBuffer, coords.Min(c => c.X) - mapBuffer, coords.Max(c => c.Y) + mapBuffer, coords.Max(c => c.X) + mapBuffer);

            ImageStats info = new ImageStats(mapToDraw, 1024, 1024);

            LineString line = new LineString(coords.ToArray());
            var drawableLine = PolygonToSKPoints(line, mapToDraw, info.degreesPerPixelX, info.degreesPerPixelY);

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
        /// A code reference for how big an image would be using 11-character PlusCodes for pixels, multiplied by GameGameTileScale (default 2)
        /// </summary>
        /// <param name="code">the code provided to determine image size</param>
        /// <param name="X">out param for image width</param>
        /// <param name="Y">out param for image height</param>
        public void GetPlusCodeImagePixelSize(string code, out int X, out int Y)
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
                case 4: //This is likely to use up more RAM than most PCs have, especially at large scales.
                    X = 4 * 20 * 20 * 20;
                    Y = 5 * 20 * 20 * 20;
                    break;
                default:
                    X = 0;
                    Y = 0;
                    break;
            }
            X = (int)(X * GameTileScale);
            Y = (int)(Y * GameTileScale);

        }

        /// <summary>
        /// Get the image for a PlusCode. Can optionally draw in a specific style set.
        /// </summary>
        /// <param name="area">the PlusCode string to draw. Can be 6-11 digits long</param>
        /// <param name="styleSet">the TagParser style set to use when drawing</param>
        /// <param name="doubleRes">treat each Cell11 contained as 2x2 pixels when true, 1x1 when not.</param>
        /// <returns></returns>
        public byte[] DrawPlusCode(string area, string styleSet = "mapTiles")
        {
            //This might be a cleaner version of my V4 function, for working with CellX sized tiles..
            //This will draw at a Cell11 resolution automatically.
            //Split it into a few functions.
            //then get all the area
            //TODO: make a version that just takes paintOps?

            int imgX = 0, imgY = 0;
            GetPlusCodeImagePixelSize(area, out imgX, out imgY);

            ImageStats info = new ImageStats(OpenLocationCode.DecodeValid(area), imgX, imgY);
            info.drawPoints = true;
            var places = GetPlacesForTile(info);
            var paintOps = GetPaintOpsForStoredElements(places, styleSet, info);
            return DrawAreaAtSize(info, paintOps); //, TagParser.GetStyleBgColor(styleSet));
        }

        /// <summary>
        /// Get the image for a PlusCode. Can optionally draw in a specific style set.
        /// </summary>
        /// <param name="area">the PlusCode string to draw. Can be 6-11 digits long</param>
        /// <param name="paintOps">the list of paint operations to run through for drawing</param>
        /// <param name="styleSet">the TagParser style set to use for determining the background color.</param>
        /// <returns>a byte array for the png file of the pluscode image file</returns>
        public byte[] DrawPlusCode(string area, List<CompletePaintOp> paintOps, string styleSet = "mapTiles")
        {
            //This might be a cleaner version of my V4 function, for working with CellX sized tiles..
            //This will draw at a Cell11 resolution automatically.
            //Split it into a few functions.
            //then get all the area

            int imgX = 0, imgY = 0;
            GetPlusCodeImagePixelSize(area, out imgX, out imgY);

            ImageStats info = new ImageStats(OpenLocationCode.DecodeValid(area), imgX, imgY);
            info.drawPoints = true;
            return DrawAreaAtSize(info, paintOps); //, TagParser.GetStyleBgColor(styleSet));
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
        public byte[] DrawAreaAtSize(ImageStats stats, List<StoredOsmElement> drawnItems = null, string styleSet = null, bool filterSmallAreas = true)
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
                        path.AddPoly(PolygonToSKPoints(p.ExteriorRing, stats.area, stats.degreesPerPixelX, stats.degreesPerPixelY));
                        foreach (var hole in p.InteriorRings)
                            path.AddPoly(PolygonToSKPoints(hole, stats.area, stats.degreesPerPixelX, stats.degreesPerPixelY));
                        canvas.DrawPath(path, paint);
                        break;
                    case "MultiPolygon":
                        foreach (var p2 in ((MultiPolygon)w.elementGeometry).Geometries)
                        {
                            var p2p = p2 as Polygon;
                            path.AddPoly(PolygonToSKPoints(p2p.ExteriorRing, stats.area, stats.degreesPerPixelX, stats.degreesPerPixelY));
                            foreach (var hole in p2p.InteriorRings)
                                path.AddPoly(PolygonToSKPoints(hole, stats.area, stats.degreesPerPixelX, stats.degreesPerPixelY));
                            canvas.DrawPath(path, paint);
                        }
                        break;
                    case "LineString":
                        var firstPoint = w.elementGeometry.Coordinates.First();
                        var lastPoint = w.elementGeometry.Coordinates.Last();
                        var points = PolygonToSKPoints(w.elementGeometry, stats.area, stats.degreesPerPixelX, stats.degreesPerPixelY);
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
                            var points2 = PolygonToSKPoints(p3, stats.area, stats.degreesPerPixelX, stats.degreesPerPixelY);
                            for (var line = 0; line < points2.Length - 1; line++)
                                canvas.DrawLine(points2[line], points2[line + 1], paint);
                        }
                        break;
                    case "Point":
                        var convertedPoint = PolygonToSKPoints(w.elementGeometry, stats.area, stats.degreesPerPixelX, stats.degreesPerPixelY);
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

        public byte[] DrawAreaAtSize(ImageStats stats, List<CompletePaintOp> paintOps)
        {
            //This is the new core drawing function. Once the paint operations have been created, I just draw them here.
            //baseline image data stuff           
            SKBitmap bitmap = new SKBitmap(stats.imageSizeX, stats.imageSizeY, SKColorType.Rgba8888, SKAlphaType.Premul);
            SKCanvas canvas = new SKCanvas(bitmap);
            //canvas.Clear(bgColor);
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

                paint.StrokeWidth = (float)w.lineWidth;
                var path = new SKPath();
                switch (w.elementGeometry.GeometryType)
                {
                    case "Polygon":
                        var p = w.elementGeometry as Polygon;
                        //if (p.Envelope.Length < (stats.degreesPerPixelX * 4)) //This poly's perimeter is too small to draw
                        //continue;
                        path.AddPoly(PolygonToSKPoints(p.ExteriorRing, stats.area, stats.degreesPerPixelX, stats.degreesPerPixelY));
                        foreach (var ir in p.Holes)
                        {
                            //if (ir.Envelope.Length < (w.lineWidth * 4)) //This poly's perimeter is less than 2x2 pixels in size.
                            //continue;
                            path.AddPoly(PolygonToSKPoints(ir, stats.area, stats.degreesPerPixelX, stats.degreesPerPixelY));
                        }
                        canvas.DrawPath(path, paint);
                        break;
                    case "MultiPolygon":
                        foreach (var p2 in ((MultiPolygon)w.elementGeometry).Geometries)
                        {
                            //if (p2.Envelope.Length < (stats.degreesPerPixelX * 4)) //This poly's perimeter is too small to draw
                            //continue;
                            var p2p = p2 as Polygon;
                            path.AddPoly(PolygonToSKPoints(p2p.ExteriorRing, stats.area, stats.degreesPerPixelX, stats.degreesPerPixelY));
                            foreach (var ir in p2p.Holes)
                            {
                                //if (ir.Envelope.Length < (stats.degreesPerPixelX * 4)) //This poly's perimeter is too small to draw
                                // continue;
                                path.AddPoly(PolygonToSKPoints(ir, stats.area, stats.degreesPerPixelX, stats.degreesPerPixelY));
                            }
                            canvas.DrawPath(path, paint);
                        }
                        break;
                    case "LineString":
                        var firstPoint = w.elementGeometry.Coordinates.First();
                        var lastPoint = w.elementGeometry.Coordinates.Last();
                        var points = PolygonToSKPoints(w.elementGeometry, stats.area, stats.degreesPerPixelX, stats.degreesPerPixelY);
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
                        //if (w.lineWidth < 1) //Don't draw lines we can't see.
                        //continue;
                        for (var line = 0; line < points.Length - 1; line++)
                            canvas.DrawLine(points[line], points[line + 1], paint);
                        break;
                    case "MultiLineString":
                        //if (w.lineWidth < 1) //Don't draw lines we can't see.
                        //continue;
                        foreach (var p3 in ((MultiLineString)w.elementGeometry).Geometries)
                        {
                            //TODO: might want to see if I need to move the LineString logic here, or if multiline string are never filled areas.
                            var points2 = PolygonToSKPoints(p3, stats.area, stats.degreesPerPixelX, stats.degreesPerPixelY);
                            for (var line = 0; line < points2.Length - 1; line++)
                                canvas.DrawLine(points2[line], points2[line + 1], paint);
                        }
                        break;
                    case "Point":
                        var convertedPoint = PolygonToSKPoints(w.elementGeometry, stats.area, stats.degreesPerPixelX, stats.degreesPerPixelY);
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
                            canvas.DrawCircle(convertedPoint[0], circleRadius, TagParser.outlinePaint);
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
        public List<CompletePaintOp> GetPaintOpsForStoredElements(List<StoredOsmElement> elements, string styleSet, ImageStats stats)
        {
            var styles = TagParser.allStyleGroups[styleSet];
            var bgOp = new CompletePaintOp( Converters.GeoAreaToPolygon(stats.area), 1, new TagParserPaint() { FillOrStroke = "fill", layerId = 100, paint = styles["background"].paintOperations.FirstOrDefault().paint }, "", 1);
            var pass1 = elements.Select(d => new { d.AreaSize, d.elementGeometry, paintOp = styles[d.GameElementName].paintOperations });
            var pass2 = new List<CompletePaintOp>(elements.Count() * 2); //assuming each element will have a Fill and a Stroke operation.
            pass2.Add(bgOp);
            //pop BG in front of pass2 before filling in actual commands.
            foreach (var op in pass1)
                foreach (var po in op.paintOp)
                    if (stats.degreesPerPixelX < po.maxDrawRes && stats.degreesPerPixelX > po.minDrawRes) //dppX should be between max and min draw range.
                        pass2.Add(new CompletePaintOp(op.elementGeometry, op.AreaSize, po, "", po.LineWidth * stats.pixelsPerDegreeX));

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
        public List<CompletePaintOp> GetPaintOpsForCustomDataElements(Geometry area, string dataKey, string styleSet, ImageStats stats)
        {
            //NOTE: styleSet must == dataKey for this to work. Or should I just add that to this function?
            var db = new PraxisContext();
            var elements = db.CustomDataOsmElements.Include(d => d.storedOsmElement).Where(d => d.dataKey == dataKey && area.Intersects(d.storedOsmElement.elementGeometry)).ToList();
            var styles = TagParser.allStyleGroups[styleSet];
            var pass1 = elements.Select(d => new { d.storedOsmElement.AreaSize, d.storedOsmElement.elementGeometry, paintOp = styles[d.dataValue].paintOperations, d.dataValue });
            var pass2 = new List<CompletePaintOp>(elements.Count() * 2); //assume each element has a Fill and Stroke op separately
            foreach (var op in pass1)
                foreach (var po in op.paintOp)
                    if (stats.degreesPerPixelX < po.maxDrawRes && stats.degreesPerPixelX > po.minDrawRes) //dppX should be between max and min draw range.
                        pass2.Add(new CompletePaintOp(op.elementGeometry, op.AreaSize, po, op.dataValue, po.LineWidth * stats.pixelsPerDegreeX));

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
        public List<CompletePaintOp> GetPaintOpsForCustomDataPlusCodes(Geometry area, string dataKey, string styleSet, ImageStats stats)
        {
            var db = new PraxisContext();
            var elements = db.CustomDataPlusCodes.Where(d => d.dataKey == dataKey && area.Intersects(d.geoAreaIndex)).ToList();
            var styles = TagParser.allStyleGroups[styleSet];
            var pass1 = elements.Select(d => new { d.geoAreaIndex.Area, d.geoAreaIndex, paintOp = styles[d.dataValue].paintOperations, d.dataValue });
            var pass2 = new List<CompletePaintOp>(elements.Count() * 2); //assuming each element has a Fill and Stroke op separately
            foreach (var op in pass1)
                foreach (var po in op.paintOp)
                    if (stats.degreesPerPixelX < po.maxDrawRes && stats.degreesPerPixelX > po.minDrawRes) //dppX should be between max and min draw range.
                        pass2.Add(new CompletePaintOp(op.geoAreaIndex, op.Area, po, op.dataValue, po.LineWidth * stats.pixelsPerDegreeX));

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
        public List<CompletePaintOp> GetPaintOpsForCustomDataPlusCodesFromTagValue(Geometry area, string dataKey, string styleSet, ImageStats stats)
        {
            var db = new PraxisContext();
            var elements = db.CustomDataPlusCodes.Where(d => d.dataKey == dataKey && area.Intersects(d.geoAreaIndex)).ToList();
            var styles = TagParser.allStyleGroups[styleSet];
            var pass1 = elements.Select(d => new { d.geoAreaIndex.Area, d.geoAreaIndex, paintOp = styles["tag"].paintOperations, d.dataValue });
            var pass2 = new List<CompletePaintOp>(elements.Count() * 2); //assuming each element has a Fill and Stroke op separately
            foreach (var op in pass1)
                foreach (var po in op.paintOp)
                    if (stats.degreesPerPixelX < po.maxDrawRes && stats.degreesPerPixelX > po.minDrawRes) //dppX should be between max and min draw range.
                        pass2.Add(new CompletePaintOp(op.geoAreaIndex, op.Area, po, op.dataValue, po.LineWidth * stats.pixelsPerDegreeX));

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
        public string DrawAreaAtSizeSVG(ImageStats stats, List<StoredOsmElement> drawnItems = null, Dictionary<string, TagParserEntry> styles = null, bool filterSmallAreas = true)
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
            var pass2 = new List<CompletePaintOp>(drawnItems.Count() * 2);
            foreach (var op in pass1)
                foreach (var po in op.paintOp)
                    pass2.Add(new CompletePaintOp(op.elementGeometry, op.AreaSize, po, "", po.LineWidth * stats.pixelsPerDegreeX));


            foreach (var w in pass2.OrderByDescending(p => p.paintOp.layerId).ThenByDescending(p => p.areaSize))
            {
                paint = w.paintOp.paint;
                if (paint.Color.Alpha == 0)
                    continue; //This area is transparent, skip drawing it entirely.

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

                        path.AddPoly(PolygonToSKPoints(p, stats.area, stats.degreesPerPixelX, stats.degreesPerPixelY));
                        foreach (var hole in p.InteriorRings)
                            path.AddPoly(PolygonToSKPoints(hole, stats.area, stats.degreesPerPixelX, stats.degreesPerPixelY));
                        canvas.DrawPath(path, paint);

                        break;
                    case "MultiPolygon":
                        foreach (var p2 in ((MultiPolygon)w.elementGeometry).Geometries)
                        {
                            var p2p = p2 as Polygon;
                            path.AddPoly(PolygonToSKPoints(p2p, stats.area, stats.degreesPerPixelX, stats.degreesPerPixelY));
                            foreach (var hole in p2p.InteriorRings)
                                path.AddPoly(PolygonToSKPoints(hole, stats.area, stats.degreesPerPixelX, stats.degreesPerPixelY));
                            canvas.DrawPath(path, paint);
                        }
                        break;
                    case "LineString":
                        var firstPoint = w.elementGeometry.Coordinates.First();
                        var lastPoint = w.elementGeometry.Coordinates.Last();
                        var points = PolygonToSKPoints(w.elementGeometry, stats.area, stats.degreesPerPixelX, stats.degreesPerPixelY);
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
                            var points2 = PolygonToSKPoints(p3, stats.area, stats.degreesPerPixelX, stats.degreesPerPixelY);
                            for (var line = 0; line < points2.Length - 1; line++)
                                canvas.DrawLine(points2[line], points2[line + 1], paint);
                        }
                        break;
                    case "Point":
                        var convertedPoint = PolygonToSKPoints(w.elementGeometry, stats.area, stats.degreesPerPixelX, stats.degreesPerPixelY);
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
            s.Position = 0;
            var svgData = new StreamReader(s).ReadToEnd();
            return svgData;
        }

        /// <summary>
        /// Creates all gameplay tiles for a given area and saves them to the database (or files, if that option is set)
        /// </summary>
        /// <param name="areaToDraw">the GeoArea of the area to create tiles for.</param>
        /// <param name="saveToFiles">If true, writes to files in the output folder. If false, saves to DB.</param>
        public void PregenMapTilesForArea(GeoArea areaToDraw, bool saveToFiles = false)
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
            DateTime expiration = DateTime.Now.AddYears(10);
            foreach (var y in yCoords)
            {
                System.Diagnostics.Stopwatch thisRowSW = new System.Diagnostics.Stopwatch();
                thisRowSW.Start();
                var db = new PraxisContext();
                //Make a collision box for just this row of Cell8s, and send the loop below just the list of things that might be relevant.
                //Add a Cell8 buffer space so all elements are loaded and drawn without needing to loop through the entire area.
                GeoArea thisRow = new GeoArea(y - ConstantValues.resolutionCell8, xCoords.First() - ConstantValues.resolutionCell8, y + ConstantValues.resolutionCell8 + ConstantValues.resolutionCell8, xCoords.Last() + resolutionCell8);
                var rowList = GetPlaces(thisRow, allPlaces);
                var tilesToSave = new List<MapTile>(xCoords.Count());

                Parallel.ForEach(xCoords, x =>
                //foreach (var x in xCoords)
                {
                    //make map tile.
                    var plusCode = new OpenLocationCode(y, x, 10);
                    var plusCode8 = plusCode.CodeDigits.Substring(0, 8);
                    var plusCodeArea = OpenLocationCode.DecodeValid(plusCode8);
                    var paddedArea = MakeBufferedGeoArea(plusCodeArea);

                    var acheck = Converters.GeoAreaToPreparedPolygon(paddedArea); //Fastest search option is one preparedPolygon against a list of normal geometry.
                    var areaList = rowList.Where(a => acheck.Intersects(a.elementGeometry)).ToList(); //Get the list of areas in this maptile.

                    var info = new ImageStats(plusCodeArea, 160, 200);
                    //new setup.
                    var areaPaintOps = GetPaintOpsForStoredElements(areaList, "mapTiles", info);
                    var tile = DrawPlusCode(plusCode8, areaPaintOps, "mapTiles");

                    if (saveToFiles)
                    {
                        File.WriteAllBytes("GameTiles\\" + plusCode8 + ".png", tile);
                    }
                    else
                    {
                        var thisTile = new MapTile() { tileData = tile, PlusCode = plusCode8, CreatedOn = DateTime.Now, ExpireOn = expiration, areaCovered = acheck.Geometry, resolutionScale = 11, styleSet = "mapTiles" };
                        lock (listLock)
                            tilesToSave.Add(thisTile);
                    }
                });
                mapTileCounter += xCoords.Count();
                if (!saveToFiles)
                {
                    db.MapTiles.AddRange(tilesToSave);
                    db.SaveChanges();
                }
                Log.WriteLog(mapTileCounter + " tiles processed, " + Math.Round((mapTileCounter / totalTiles) * 100, 2) + "% complete, " + Math.Round(xCoords.Count() / thisRowSW.Elapsed.TotalSeconds, 2) + " tiles per second.");

            }//);
            progressTimer.Stop();
            Log.WriteLog("Area map tiles drawn in " + progressTimer.Elapsed.ToString() + ", averaged " + Math.Round(mapTileCounter / progressTimer.Elapsed.TotalSeconds, 2) + " tiles per second.");
        }

        /// <summary>
        /// Generates all SlippyMap tiles for a given area and zoom level, and saves them to the database.
        /// </summary>
        /// <param name="buffered">the GeoArea to generate tiles for</param>
        /// <param name="zoomLevel">the zoom level to generate tiles at.</param>
        public void PregenSlippyMapTilesForArea(GeoArea buffered, int zoomLevel)
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

                    var tile = DrawAreaAtSize(info, areaList);
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
        public byte[] LayerTiles(ImageStats info, byte[] bottomTile, byte[] topTile)
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

        /// <summary>
        /// Get the background color from a style set
        /// </summary>
        /// <param name="styleSet">the name of the style set to pull the background color from</param>
        /// <returns>the SKColor saved into the requested background paint object.</returns>
        public SKColor GetStyleBgColor(string styleSet)
        {
            return TagParser.allStyleGroups[styleSet]["background"].paintOperations.First().paint.Color;
        }

        public string GetStyleBgColorString(string styleSet)
        {
            return TagParser.allStyleGroups[styleSet]["background"].paintOperations.First().HtmlColorCode;
        }

        /// <summary>
        /// Converts an NTS Polygon into a SkiaSharp SKPoint array so that it can be drawn in SkiaSharp.
        /// </summary>
        /// <param name="place">Polygon object to be converted/drawn</param>
        /// <param name="drawingArea">GeoArea representing the image area being drawn. Usually passed from an ImageStats object</param>
        /// <param name="degreesPerPixelX">Width of each pixel in degrees</param>
        /// <param name="degreesPerPixelY">Height of each pixel in degrees</param>
        /// <returns>Array of SkPoints for the image information provided.</returns>
        public SkiaSharp.SKPoint[] PolygonToSKPoints(Geometry place, GeoArea drawingArea, double degreesPerPixelX, double degreesPerPixelY)
        {
            SkiaSharp.SKPoint[] points = place.Coordinates.Select(o => new SkiaSharp.SKPoint((float)((o.X - drawingArea.WestLongitude) * (1 / degreesPerPixelX)), (float)((o.Y - drawingArea.SouthLatitude) * (1 / degreesPerPixelY)))).ToArray();
            return points;
        }

        public SkiaSharp.SKPoint PlaceInfoToSKPoint(PraxisCore.StandaloneDbTables.PlaceInfo2 pi, ImageStats imgstats)
        {
            SkiaSharp.SKPoint point = new SkiaSharp.SKPoint();
            point.X = (float)((pi.lonCenter - imgstats.area.WestLongitude) * (1 / imgstats.degreesPerPixelX));
            point.Y = (float)((pi.latCenter - imgstats.area.SouthLatitude) * (1 / imgstats.degreesPerPixelY));
            return point;
        }

        public SkiaSharp.SKPoint[] PlaceInfoToSKPoints(PraxisCore.StandaloneDbTables.PlaceInfo2 pi, ImageStats info)
        {
            float heightMod = (float)pi.height / 2;
            float widthMod = (float)pi.width / 2;
            var points = new SkiaSharp.SKPoint[5];
            points[0] = new SkiaSharp.SKPoint((float)(pi.lonCenter + widthMod), (float)(pi.latCenter + heightMod)); //upper right corner
            points[1] = new SkiaSharp.SKPoint((float)(pi.lonCenter + widthMod), (float)(pi.latCenter - heightMod)); //lower right
            points[2] = new SkiaSharp.SKPoint((float)(pi.lonCenter - widthMod), (float)(pi.latCenter - heightMod)); //lower left
            points[3] = new SkiaSharp.SKPoint((float)(pi.lonCenter - widthMod), (float)(pi.latCenter + heightMod)); //upper left
            points[4] = new SkiaSharp.SKPoint((float)(pi.lonCenter + widthMod), (float)(pi.latCenter + heightMod)); //upper right corner again for a closed shape.

            //points is now a geometric area. Convert to image area
            points = points.Select(p => new SkiaSharp.SKPoint((float)((p.X - info.area.WestLongitude) * (1 / info.degreesPerPixelX)), (float)((p.Y - info.area.SouthLatitude) * (1 / info.degreesPerPixelY)))).ToArray();

            return points;
        }

        /// <summary>
        /// Converts the offline-standalone PlaceInfo entries into SKRects for drawing on a SlippyMap. Used to visualize the offline mode beahvior of areas.
        /// </summary>
        /// <param name="pi">PlaceInfo object to convert</param>
        /// <param name="info">ImageStats for the resulting map tile</param>
        /// <returns>The SKRect representing the standaloneDb size of the PlaceInfo</returns>
        public SkiaSharp.SKRect PlaceInfoToRect(PraxisCore.StandaloneDbTables.PlaceInfo2 pi, ImageStats info)
        {
            SkiaSharp.SKRect r = new SkiaSharp.SKRect();
            float heightMod = (float)pi.height / 2;
            float widthMod = (float)pi.width / 2;
            r.Left = (float)pi.lonCenter - widthMod;
            r.Left = (float)(r.Left - info.area.WestLongitude) * (float)(1 / info.degreesPerPixelX);
            r.Right = (float)pi.lonCenter + widthMod;
            r.Right = (float)(r.Right - info.area.WestLongitude) * (float)(1 / info.degreesPerPixelX);
            r.Top = (float)pi.latCenter + heightMod;
            r.Top = (float)(r.Top - info.area.SouthLatitude) * (float)(1 / info.degreesPerPixelY);
            r.Bottom = (float)pi.latCenter - heightMod;
            r.Bottom = (float)(r.Bottom - info.area.SouthLatitude) * (float)(1 / info.degreesPerPixelY);


            return r;
        }
    }
}