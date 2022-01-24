using Google.OpenLocationCode;
using NetTopologySuite.Geometries;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using static PraxisCore.ConstantValues;
using static PraxisCore.DbTables;
using static PraxisCore.Place;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using PraxisCore.Support;
using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Drawing.Processing;

namespace PraxisCore
{
    /// <summary>
    /// All functions related to generating or expiring map tiles. Both PlusCode sized tiles for gameplay or SlippyMap tiles for a webview.
    /// </summary>
    public class MapTiles : IMapTiles
    {
        public int MapTileSizeSquare = 512; //Default value, updated by PraxisMapper at startup. COvers Slippy tiles, not gameplay tiles.
        public double GameTileScale = 2;
        public double bufferSize = resolutionCell10; //How much space to add to an area to make sure elements are drawn correctly. Mostly to stop points from being clipped.
        //static SKPaint eraser = new SKPaint() { Color = SKColors.Transparent, BlendMode = SKBlendMode.Src, Style = SKPaintStyle.StrokeAndFill }; //BlendMode is the important part for an Eraser.
        static Random r = new Random();
        public static Dictionary<string, Image> cachedBitmaps = new Dictionary<string, Image>(); //Icons for points separate from pattern fills, though I suspect if I made a pattern fill with the same size as the icon I wouldn't need this.

        public void Initialize()
        {
            //TODO: replace TagParser optimizations here.
        }

        /// <summary>
        /// Draw square boxes around each area to approximate how they would behave in an offline app
        /// </summary>
        /// <param name="info">the image information for drawing</param>
        /// <param name="items">the elements to draw.</param>
        /// <returns>byte array of the generated .png tile image</returns>
        public byte[] DrawOfflineEstimatedAreas(ImageStats info, List<StoredOsmElement> items)
        {
            throw new NotImplementedException();
            //SKBitmap bitmap = new SKBitmap(info.imageSizeX, info.imageSizeY, SKColorType.Rgba8888, SKAlphaType.Premul);
            //SKCanvas canvas = new SKCanvas(bitmap);
            //var bgColor = SKColors.Transparent;
            //canvas.Clear(bgColor);
            //canvas.Scale(1, -1, info.imageSizeX / 2, info.imageSizeY / 2);
            //SKPaint fillpaint = new SKPaint();
            //fillpaint.IsAntialias = true;
            //fillpaint.Style = SKPaintStyle.Fill;
            //var strokePaint = new SKPaint();
            //strokePaint.Color = SKColors.Black;
            //strokePaint.TextSize = 32;
            //strokePaint.StrokeWidth = 3;
            //strokePaint.Style = SKPaintStyle.Stroke;
            //strokePaint.TextAlign = SKTextAlign.Center;
            ////TagParser.ApplyTags(items);

            //var placeInfo = PraxisCore.Standalone.Standalone.GetPlaceInfo(items.Where(i =>
            //i.IsGameElement
            //).ToList());

            ////this is for rectangles.
            //foreach (var pi in placeInfo)
            //{
            //    var rect = Converters.PlaceInfoToRect(pi, info);
            //    fillpaint.Color = TagParser.PickStaticColorForArea(pi.Name);
            //    canvas.DrawRect(rect, fillpaint);
            //    canvas.DrawRect(rect, strokePaint);
            //}

            //canvas.Scale(1, -1, info.imageSizeX / 2, info.imageSizeY / 2); //inverts the inverted image again!
            //foreach (var pi in placeInfo)
            //{
            //    var rect = Converters.PlaceInfoToRect(pi, info);
            //    canvas.DrawText(pi.Name, rect.MidX, info.imageSizeY - rect.MidY, strokePaint);
            //}

            //var ms = new MemoryStream();
            //var skms = new SkiaSharp.SKManagedWStream(ms);
            //bitmap.Encode(skms, SkiaSharp.SKEncodedImageFormat.Png, 100);
            //var results = ms.ToArray();
            //skms.Dispose(); ms.Close(); ms.Dispose();
            //return results;
        }

        /// <summary>
        /// Draws grid lines to match boundaries for 8 character PlusCodes.
        /// </summary>
        /// <param name="totalArea">the GeoArea to draw lines in</param>
        /// <returns>the byte array for the maptile png file</returns>
        public byte[] DrawCell8GridLines(GeoArea totalArea)
        {
            throw new NotImplementedException();
            //int imageSizeX = MapTileSizeSquare;
            //int imageSizeY = MapTileSizeSquare;
            //SKBitmap bitmap = new SKBitmap(imageSizeX, imageSizeY, SKColorType.Rgba8888, SKAlphaType.Premul);
            //SKCanvas canvas = new SKCanvas(bitmap);
            //var bgColor = new SKColor();
            //SKColor.TryParse("00000000", out bgColor);
            //canvas.Clear(bgColor);
            //canvas.Scale(1, -1, imageSizeX / 2, imageSizeY / 2);
            //SKPaint paint = new SKPaint();
            //SKColor color = new SKColor();
            //SKColor.TryParse("#FF0000", out color);
            //paint.Color = color;
            //paint.Style = SKPaintStyle.Stroke;
            //paint.StrokeWidth = 3;
            //paint.IsAntialias = true;

            //double degreesPerPixelX = totalArea.LongitudeWidth / imageSizeX;
            //double degreesPerPixelY = totalArea.LatitudeHeight / imageSizeY;

            ////This is hardcoded to Cell 8 spaced gridlines.
            //var imageLeft = totalArea.WestLongitude;
            //var spaceToFirstLineLeft = (imageLeft % resolutionCell8);

            //var imageBottom = totalArea.SouthLatitude;
            //var spaceToFirstLineBottom = (imageBottom % resolutionCell8);

            //double lonLineTrackerDegrees = imageLeft - spaceToFirstLineLeft; //This is degree coords
            //while (lonLineTrackerDegrees <= totalArea.EastLongitude + resolutionCell8) //This means we should always draw at least 2 lines, even if they're off-canvas.
            //{
            //    var geoLine = new LineString(new Coordinate[] { new Coordinate(lonLineTrackerDegrees, 90), new Coordinate(lonLineTrackerDegrees, -90) });
            //    var points = Converters.PolygonToSKPoints(geoLine, totalArea, degreesPerPixelX, degreesPerPixelY);
            //    canvas.DrawLine(points[0], points[1], paint);
            //    lonLineTrackerDegrees += resolutionCell8;
            //}

            //double latLineTrackerDegrees = imageBottom - spaceToFirstLineBottom; //This is degree coords
            //while (latLineTrackerDegrees <= totalArea.NorthLatitude + resolutionCell8) //This means we should always draw at least 2 lines, even if they're off-canvas.
            //{
            //    var geoLine = new LineString(new Coordinate[] { new Coordinate(180, latLineTrackerDegrees), new Coordinate(-180, latLineTrackerDegrees) });
            //    var points = Converters.PolygonToSKPoints(geoLine, totalArea, degreesPerPixelX, degreesPerPixelY);
            //    canvas.DrawLine(points[0], points[1], paint);
            //    latLineTrackerDegrees += resolutionCell8;
            //}

            //var ms = new MemoryStream();
            //var skms = new SkiaSharp.SKManagedWStream(ms);
            //bitmap.Encode(skms, SkiaSharp.SKEncodedImageFormat.Png, 100);
            //var results = ms.ToArray();
            //skms.Dispose(); ms.Close(); ms.Dispose();
            //return results;
        }

        /// <summary>
        /// Draws grid lines to match boundaries for 10 character PlusCodes.
        /// </summary>
        /// <param name="totalArea">the GeoArea to draw lines in</param>
        /// <returns>the byte array for the maptile png file</returns>
        public byte[] DrawCell10GridLines(GeoArea totalArea)
        {
            throw new NotImplementedException();
            //int imageSizeX = MapTileSizeSquare;
            //int imageSizeY = MapTileSizeSquare;
            //SKBitmap bitmap = new SKBitmap(imageSizeX, imageSizeY, SKColorType.Rgba8888, SKAlphaType.Premul);
            //SKCanvas canvas = new SKCanvas(bitmap);
            //var bgColor = new SKColor();
            //SKColor.TryParse("00000000", out bgColor);
            //canvas.Clear(bgColor);
            //canvas.Scale(1, -1, imageSizeX / 2, imageSizeY / 2);
            //SKPaint paint = new SKPaint();
            //SKColor color = new SKColor();
            //SKColor.TryParse("#00CCFF", out color);
            //paint.Color = color;
            //paint.Style = SKPaintStyle.Stroke;
            //paint.StrokeWidth = 1;
            //paint.IsAntialias = true;

            //double degreesPerPixelX = totalArea.LongitudeWidth / imageSizeX;
            //double degreesPerPixelY = totalArea.LatitudeHeight / imageSizeY;

            ////This is hardcoded to Cell 8 spaced gridlines.
            //var imageLeft = totalArea.WestLongitude;
            //var spaceToFirstLineLeft = (imageLeft % resolutionCell10);

            //var imageBottom = totalArea.SouthLatitude;
            //var spaceToFirstLineBottom = (imageBottom % resolutionCell10);

            //double lonLineTrackerDegrees = imageLeft - spaceToFirstLineLeft; //This is degree coords
            //while (lonLineTrackerDegrees <= totalArea.EastLongitude + resolutionCell10) //This means we should always draw at least 2 lines, even if they're off-canvas.
            //{
            //    var geoLine = new LineString(new Coordinate[] { new Coordinate(lonLineTrackerDegrees, 90), new Coordinate(lonLineTrackerDegrees, -90) });
            //    var points = Converters.PolygonToSKPoints(geoLine, totalArea, degreesPerPixelX, degreesPerPixelY);
            //    canvas.DrawLine(points[0], points[1], paint);
            //    lonLineTrackerDegrees += resolutionCell10;
            //}

            //double latLineTrackerDegrees = imageBottom - spaceToFirstLineBottom; //This is degree coords
            //while (latLineTrackerDegrees <= totalArea.NorthLatitude + resolutionCell10) //This means we should always draw at least 2 lines, even if they're off-canvas.
            //{
            //    var geoLine = new LineString(new Coordinate[] { new Coordinate(180, latLineTrackerDegrees), new Coordinate(-180, latLineTrackerDegrees) });
            //    var points = Converters.PolygonToSKPoints(geoLine, totalArea, degreesPerPixelX, degreesPerPixelY);
            //    canvas.DrawLine(points[0], points[1], paint);
            //    latLineTrackerDegrees += resolutionCell10;
            //}

            //var ms = new MemoryStream();
            //var skms = new SkiaSharp.SKManagedWStream(ms);
            //bitmap.Encode(skms, SkiaSharp.SKEncodedImageFormat.Png, 100);
            //var results = ms.ToArray();
            //skms.Dispose(); ms.Close(); ms.Dispose();
            //return results;
        }

        

        /// <summary>
        /// Take a path provided by a user, draw it as a maptile. Potentially useful for exercise trackers. Resulting file must not be saved to the server as that would be user tracking.
        /// </summary>
        /// <param name="pointListAsString">a string of points separate by , and | </param>
        /// <returns>the png file with the path drawn over the mapdata in the area.</returns>
        public byte[] DrawUserPath(string pointListAsString)
        {
            throw new NotImplementedException();
            ////String is formatted as Lat,Lon~Lat,Lon~ repeating. Characters chosen to not be percent-encoded if submitted as part of the URL.
            ////first, convert this to a list of latlon points
            //string[] pointToConvert = pointListAsString.Split("|");
            //List<Coordinate> coords = pointToConvert.Select(p => new Coordinate(double.Parse(p.Split(',')[0]), double.Parse(p.Split(',')[1]))).ToList();

            //var mapBuffer = resolutionCell8 / 2; //Leave some area around the edges of where they went.
            //GeoArea mapToDraw = new GeoArea(coords.Min(c => c.Y) - mapBuffer, coords.Min(c => c.X) - mapBuffer, coords.Max(c => c.Y) + mapBuffer, coords.Max(c => c.X) + mapBuffer);

            //ImageStats info = new ImageStats(mapToDraw, 1024, 1024);

            //LineString line = new LineString(coords.ToArray());
            //var drawableLine = Converters.PolygonToSKPoints(line, mapToDraw, info.degreesPerPixelX, info.degreesPerPixelY);

            ////Now, draw that path on the map.
            //var places = GetPlaces(mapToDraw); //, null, false, false, degreesPerPixelX * 4 ///TODO: restore item filtering
            //var baseImage = DrawAreaAtSize(info, places);

            //SKBitmap sKBitmap = SKBitmap.Decode(baseImage);
            //SKCanvas canvas = new SKCanvas(sKBitmap);
            //SKPaint paint = new SKPaint();
            //paint.Style = SKPaintStyle.Stroke;
            //paint.StrokeWidth = 4; //Larger than normal lines at any zoom level.
            //paint.Color = new SKColor(0, 0, 0); //Pure black, for maximum visibility.
            //for (var x = 0; x < drawableLine.Length - 1; x++)
            //    canvas.DrawLine(drawableLine[x], drawableLine[x + 1], paint);

            //var ms = new MemoryStream();
            //var skms = new SKManagedWStream(ms);
            //sKBitmap.Encode(skms, SKEncodedImageFormat.Png, 100);
            //var results = ms.ToArray();
            //skms.Dispose(); ms.Close(); ms.Dispose();
            //return results;
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

            //This should just get hte paint ops then call the core drawing function.
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

            if (drawnItems == null)
                drawnItems = GetPlaces(stats.area, filterSize: minimumSize, includePoints: includePoints);

            var paintOps = MapTileSupport.GetPaintOpsForStoredElements(drawnItems, "mapTiles", stats);

            return DrawAreaAtSize(stats, paintOps);

            Dictionary<string, TagParserEntry> styles;
            if (styleSet != null)
                styles = TagParser.allStyleGroups[styleSet];
            else
                styles = TagParser.allStyleGroups.First().Value;

            

            

            var db = new PraxisContext();
            var geo = Converters.GeoAreaToPolygon(stats.area);
            

            //baseline image data stuff           
            var image = new Image<Rgba32>(stats.imageSizeX, stats.imageSizeY);
            var bgColor = Rgba32.ParseHex(styles["background"].paintOperations.FirstOrDefault().HtmlColorCode); //Backgound is a named style, unmatched will be the last entry and transparent.
           
            

            foreach (var w in paintOps.OrderByDescending(p => p.paintOp.layerId).ThenByDescending(p => p.areaSize))
            {
                var color = Rgba32.ParseHex(w.paintOp.HtmlColorCode);
                if (color.A == 0)
                    continue; //This area is transparent, skip drawing it entirely.

                //var path = new SKPath();
                //path.FillType = SKPathFillType.EvenOdd;
                switch (w.elementGeometry.GeometryType)
                {
                    case "Polygon":
                        var drawThis = PolygonToDrawingPolygon(w.elementGeometry, stats.area, stats.degreesPerPixelX, stats.degreesPerPixelY);
                        if (w.paintOp.FillOrStroke == "fill")
                            image.Mutate(x => x.Fill(color, drawThis));
                        else
                            image.Mutate(x => x.Draw(color, w.paintOp.LineWidth, drawThis));
                        break;
                    case "MultiPolygon":
                        var lines = new List<LinearLineSegment>();
                        foreach (var p2 in ((MultiPolygon)w.elementGeometry).Geometries)
                        {
                            var drawThis1 = PolygonToDrawingLine(w.elementGeometry, stats.area, stats.degreesPerPixelX, stats.degreesPerPixelY);
                            lines.Add(drawThis1);
                        }
                        var mp = new SixLabors.ImageSharp.Drawing.Polygon(lines);
                        //TODO: check this, consider switching shape options to OddEven if it doesn't draw holes right?
                        if (w.paintOp.FillOrStroke == "fill")
                            image.Mutate(x => x.Fill(color, mp));
                        else
                            image.Mutate(x => x.Draw(color, w.paintOp.LineWidth, mp));
                        break;
                    case "LineString":
                        var firstPoint = w.elementGeometry.Coordinates.First();
                        var lastPoint = w.elementGeometry.Coordinates.Last();
                        var line = LineToDrawingLine(w.elementGeometry, stats.area, stats.degreesPerPixelX, stats.degreesPerPixelY);

                        if (firstPoint.Equals(lastPoint) && w.paintOp.FillOrStroke == "fill")
                            image.Mutate(x => x.Fill(color, new SixLabors.ImageSharp.Drawing.Polygon(new LinearLineSegment(line))));
                        else
                            image.Mutate(x => x.DrawLines(color, w.paintOp.LineWidth, line));
                        break;
                    case "MultiLineString":
                        foreach (var p3 in ((MultiLineString)w.elementGeometry).Geometries)
                        {
                            var line2 = LineToDrawingLine(p3, stats.area, stats.degreesPerPixelX, stats.degreesPerPixelY);
                            image.Mutate(x => x.DrawLines(color, w.paintOp.LineWidth, line2));
                        }
                        break;
                    case "Point":
                        var convertedPoint = PointToPointF(w.elementGeometry, stats.area, stats.degreesPerPixelX, stats.degreesPerPixelY);
                        //If this type has an icon, use it. Otherwise draw a circle in that type's color.
                        if (!string.IsNullOrEmpty(w.paintOp.fileName))
                        {
                            //TODO bitmaps on bitmaps in ImageSharp
                            //SKBitmap icon = TagParser.cachedBitmaps[w.paintOp.fileName];
                            //canvas.DrawBitmap(icon, convertedPoint[0]);
                        }
                        else
                        {
                            var circleRadius = (float)(ConstantValues.resolutionCell10 / stats.degreesPerPixelX / 2); //I want points to be drawn as 1 Cell10 in diameter.
                            var shape = new SixLabors.ImageSharp.Drawing.EllipsePolygon(
                                PointToPointF(w.elementGeometry, stats.area, stats.degreesPerPixelX, stats.degreesPerPixelY), 
                                new SizeF(circleRadius, circleRadius)); 
                            image.Mutate(x => x.Fill(color, shape));
                            image.Mutate(x => x.Draw(Color.Black, 1, shape)); //TODO: double check colors and sizes for outlines

                            //canvas.DrawCircle(convertedPoint[0], circleRadius, styles["outline"].paintOperations.First().paint); //TODO: this forces an outline style to be present in the list or this crashes.
                        }
                        break;
                    default:
                        Log.WriteLog("Unknown geometry type found, not drawn.");
                        break;
                }
            }

            image.Mutate(x => x.Flip(FlipMode.Vertical)); //Plus codes are south-to-north, so invert the image to make it correct.
            var ms = new MemoryStream();
            image.SaveAsPng(ms);
            return ms.ToArray();
        }

        public byte[] DrawAreaAtSize(ImageStats stats, List<CompletePaintOp> paintOps)
        {

            //baseline image data stuff           
            var image = new Image<Rgba32>(stats.imageSizeX, stats.imageSizeY);
            foreach (var w in paintOps.OrderByDescending(p => p.paintOp.layerId).ThenByDescending(p => p.areaSize))
            {
                var color = Rgba32.ParseHex(w.paintOp.HtmlColorCode);
                if (color.A == 0)
                    continue; //This area is transparent, skip drawing it entirely.

                switch (w.elementGeometry.GeometryType)
                {
                    case "Polygon":
                        var drawThis = PolygonToDrawingPolygon(w.elementGeometry, stats.area, stats.degreesPerPixelX, stats.degreesPerPixelY);
                        if (w.paintOp.FillOrStroke == "fill")
                            image.Mutate(x => x.Fill(color, drawThis));
                        else
                            image.Mutate(x => x.Draw(color, w.paintOp.LineWidth, drawThis));
                        break;
                    case "MultiPolygon":
                        var lines = new List<LinearLineSegment>();
                        foreach (var p2 in ((MultiPolygon)w.elementGeometry).Geometries)
                        {
                            var drawThis1 = PolygonToDrawingLine(w.elementGeometry, stats.area, stats.degreesPerPixelX, stats.degreesPerPixelY);
                            lines.Add(drawThis1);
                        }
                        var mp = new SixLabors.ImageSharp.Drawing.Polygon(lines);
                        //TODO: check this, consider switching shape options to OddEven if it doesn't draw holes right?
                        if (w.paintOp.FillOrStroke == "fill")
                            image.Mutate(x => x.Fill(color, mp));
                        else
                            image.Mutate(x => x.Draw(color, w.paintOp.LineWidth, mp));
                        break;
                    case "LineString":
                        var firstPoint = w.elementGeometry.Coordinates.First();
                        var lastPoint = w.elementGeometry.Coordinates.Last();
                        var line = LineToDrawingLine(w.elementGeometry, stats.area, stats.degreesPerPixelX, stats.degreesPerPixelY);

                        if (firstPoint.Equals(lastPoint) && w.paintOp.FillOrStroke == "fill")
                            image.Mutate(x => x.Fill(color, new SixLabors.ImageSharp.Drawing.Polygon(new LinearLineSegment(line))));
                        else
                            image.Mutate(x => x.DrawLines(color, w.paintOp.LineWidth, line));
                        break;
                    case "MultiLineString":
                        foreach (var p3 in ((MultiLineString)w.elementGeometry).Geometries)
                        {
                            var line2 = LineToDrawingLine(p3, stats.area, stats.degreesPerPixelX, stats.degreesPerPixelY);
                            image.Mutate(x => x.DrawLines(color, w.paintOp.LineWidth, line2));
                        }
                        break;
                    case "Point":
                        var convertedPoint = PointToPointF(w.elementGeometry, stats.area, stats.degreesPerPixelX, stats.degreesPerPixelY);
                        //If this type has an icon, use it. Otherwise draw a circle in that type's color.
                        if (!string.IsNullOrEmpty(w.paintOp.fileName))
                        {
                            //this needs another Image
                            Image i2 = Image.Load(w.paintOp.fileName);
                            //image.Mutate(x => x.DrawImage(w.paintOp.fileName, 1);
                            //TODO bitmaps on bitmaps in ImageSharp
                            //SKBitmap icon = TagParser.cachedBitmaps[w.paintOp.fileName];
                            //canvas.DrawBitmap(icon, convertedPoint[0]);
                        }
                        else
                        {
                            var circleRadius = (float)(ConstantValues.resolutionCell10 / stats.degreesPerPixelX / 2); //I want points to be drawn as 1 Cell10 in diameter.
                            var shape = new SixLabors.ImageSharp.Drawing.EllipsePolygon(
                                PointToPointF(w.elementGeometry, stats.area, stats.degreesPerPixelX, stats.degreesPerPixelY),
                                new SizeF(circleRadius, circleRadius));
                            image.Mutate(x => x.Fill(color, shape));
                            image.Mutate(x => x.Draw(Color.Black, 1, shape)); //TODO: double check colors and sizes for outlines

                            //canvas.DrawCircle(convertedPoint[0], circleRadius, styles["outline"].paintOperations.First().paint); //TODO: this forces an outline style to be present in the list or this crashes.
                        }
                        break;
                    default:
                        Log.WriteLog("Unknown geometry type found, not drawn.");
                        break;
                }
            }

            image.Mutate(x => x.Flip(FlipMode.Vertical)); //Plus codes are south-to-north, so invert the image to make it correct.
            var ms = new MemoryStream();
            image.SaveAsPng(ms);
            return ms.ToArray();

            //This is the new core drawing function. Once the paint operations have been created, I just draw them here.
            //baseline image data stuff           
            //SKBitmap bitmap = new SKBitmap(stats.imageSizeX, stats.imageSizeY, SKColorType.Rgba8888, SKAlphaType.Premul);
            //SKCanvas canvas = new SKCanvas(bitmap);
            //canvas.Clear(bgColor);
            //canvas.Scale(1, -1, stats.imageSizeX / 2, stats.imageSizeY / 2);
            //SKPaint paint = new SKPaint();

            //foreach (var w in paintOps.OrderByDescending(p => p.paintOp.layerId).ThenByDescending(p => p.areaSize))
            //{
            //    paint = w.paintOp.paint;

            //    if (w.paintOp.fromTag) //FromTag is for when you are saving color data directly to each element, instead of tying it to a styleset.
            //        paint.Color = SKColor.Parse(w.tagValue);

            //    if (paint.Color.Alpha == 0)
            //        continue; //This area is transparent, skip drawing it entirely.

            //    if (w.paintOp.randomize) //To randomize the color on every Draw call.
            //        paint.Color = new SKColor((byte)r.Next(0, 255), (byte)r.Next(0, 255), (byte)r.Next(0, 255), 99);

            //    paint.StrokeWidth = (float)w.lineWidth;
            //    var path = new SKPath();
            //    switch (w.elementGeometry.GeometryType)
            //    {
            //        case "Polygon":
            //            var p = w.elementGeometry as Polygon;
            //            //if (p.Envelope.Length < (stats.degreesPerPixelX * 4)) //This poly's perimeter is too small to draw
            //            //continue;
            //            path.AddPoly(Converters.PolygonToSKPoints(p.ExteriorRing, stats.area, stats.degreesPerPixelX, stats.degreesPerPixelY));
            //            foreach (var ir in p.Holes)
            //            {
            //                //if (ir.Envelope.Length < (w.lineWidth * 4)) //This poly's perimeter is less than 2x2 pixels in size.
            //                //continue;
            //                path.AddPoly(Converters.PolygonToSKPoints(ir, stats.area, stats.degreesPerPixelX, stats.degreesPerPixelY));
            //            }
            //            canvas.DrawPath(path, paint);
            //            break;
            //        case "MultiPolygon":
            //            foreach (var p2 in ((MultiPolygon)w.elementGeometry).Geometries)
            //            {
            //                //if (p2.Envelope.Length < (stats.degreesPerPixelX * 4)) //This poly's perimeter is too small to draw
            //                //continue;
            //                var p2p = p2 as Polygon;
            //                path.AddPoly(Converters.PolygonToSKPoints(p2p.ExteriorRing, stats.area, stats.degreesPerPixelX, stats.degreesPerPixelY));
            //                foreach (var ir in p2p.Holes)
            //                {
            //                    //if (ir.Envelope.Length < (stats.degreesPerPixelX * 4)) //This poly's perimeter is too small to draw
            //                    // continue;
            //                    path.AddPoly(Converters.PolygonToSKPoints(ir, stats.area, stats.degreesPerPixelX, stats.degreesPerPixelY));
            //                }
            //                canvas.DrawPath(path, paint);
            //            }
            //            break;
            //        case "LineString":
            //            var firstPoint = w.elementGeometry.Coordinates.First();
            //            var lastPoint = w.elementGeometry.Coordinates.Last();
            //            var points = Converters.PolygonToSKPoints(w.elementGeometry, stats.area, stats.degreesPerPixelX, stats.degreesPerPixelY);
            //            if (firstPoint.Equals(lastPoint))
            //            {
            //                //This is a closed shape. Check to see if it's supposed to be filled in.
            //                if (paint.Style == SKPaintStyle.Fill)
            //                {
            //                    path.AddPoly(points);
            //                    canvas.DrawPath(path, paint);
            //                    continue;
            //                }
            //            }
            //            //if (w.lineWidth < 1) //Don't draw lines we can't see.
            //            //continue;
            //            for (var line = 0; line < points.Length - 1; line++)
            //                canvas.DrawLine(points[line], points[line + 1], paint);
            //            break;
            //        case "MultiLineString":
            //            //if (w.lineWidth < 1) //Don't draw lines we can't see.
            //            //continue;
            //            foreach (var p3 in ((MultiLineString)w.elementGeometry).Geometries)
            //            {
            //                //TODO: might want to see if I need to move the LineString logic here, or if multiline string are never filled areas.
            //                var points2 = Converters.PolygonToSKPoints(p3, stats.area, stats.degreesPerPixelX, stats.degreesPerPixelY);
            //                for (var line = 0; line < points2.Length - 1; line++)
            //                    canvas.DrawLine(points2[line], points2[line + 1], paint);
            //            }
            //            break;
            //        case "Point":
            //            var convertedPoint = Converters.PolygonToSKPoints(w.elementGeometry, stats.area, stats.degreesPerPixelX, stats.degreesPerPixelY);
            //            //If this type has an icon, use it. Otherwise draw a circle in that type's color.
            //            if (!string.IsNullOrEmpty(w.paintOp.fileName))
            //            {
            //                SKBitmap icon = TagParser.cachedBitmaps[w.paintOp.fileName];
            //                canvas.DrawBitmap(icon, convertedPoint[0]);
            //            }
            //            else
            //            {
            //                var circleRadius = (float)(ConstantValues.resolutionCell10 / stats.degreesPerPixelX / 2); //I want points to be drawn as 1 Cell10 in diameter.
            //                canvas.DrawCircle(convertedPoint[0], circleRadius, paint);
            //                canvas.DrawCircle(convertedPoint[0], circleRadius, TagParser.outlinePaint);
            //            }
            //            break;
            //        default:
            //            Log.WriteLog("Unknown geometry type found, not drawn.");
            //            break;
            //    }
            //}
            ////}

            //var ms = new MemoryStream();
            //var skms = new SKManagedWStream(ms);
            //bitmap.Encode(skms, SKEncodedImageFormat.Png, 100);
            //var results = ms.ToArray();
            //skms.Dispose(); ms.Close(); ms.Dispose();
            //return results;
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
            throw new NotImplementedException();
            ////TODO: make this take CompletePaintOps
            ////This is the new core drawing function. Takes in an area, the items to draw, and the size of the image to draw. 
            ////The drawn items get their paint pulled from the TagParser's list. If I need multiple match lists, I'll need to make a way
            ////to pick which list of tagparser rules to use.

            //if (styles == null)
            //    styles = TagParser.allStyleGroups.First().Value;

            //double minimumSize = 0;
            //if (filterSmallAreas)
            //    minimumSize = stats.degreesPerPixelX; //don't draw elements under 1 pixel in size. at slippy zoom 12, this is approx. 1 pixel for a Cell10.

            //var db = new PraxisContext();
            //var geo = Converters.GeoAreaToPolygon(stats.area);
            //if (drawnItems == null)
            //    drawnItems = GetPlaces(stats.area, filterSize: minimumSize);

            ////baseline image data stuff           
            ////SKBitmap bitmap = new SKBitmap(stats.imageSizeX, stats.imageSizeY, SKColorType.Rgba8888, SKAlphaType.Premul);
            //var bounds = new SKRect(0, 0, stats.imageSizeX, stats.imageSizeY);
            //MemoryStream s = new MemoryStream();
            //SKCanvas canvas = SKSvgCanvas.Create(bounds, s); //output not guaranteed to be complete until the canvas is deleted?!?
            ////SKCanvas canvas = new SKCanvas(bitmap);
            //var bgColor = styles["background"].paintOperations.FirstOrDefault().paint; //Backgound is a named style, unmatched will be the last entry and transparent.
            //canvas.Clear(bgColor.Color);
            //canvas.Scale(1, -1, stats.imageSizeX / 2, stats.imageSizeY / 2);
            //SKPaint paint = new SKPaint();

            ////I guess what I want here is a list of an object with an elementGeometry object for the shape, and a paintOp attached to it
            //var pass1 = drawnItems.Select(d => new { d.AreaSize, d.elementGeometry, paintOp = styles[d.GameElementName].paintOperations });
            //var pass2 = new List<CompletePaintOp>(drawnItems.Count() * 2);
            //foreach (var op in pass1)
            //    foreach (var po in op.paintOp)
            //        pass2.Add(new CompletePaintOp(op.elementGeometry, op.AreaSize, po, "", po.LineWidth * stats.pixelsPerDegreeX));


            //foreach (var w in pass2.OrderByDescending(p => p.paintOp.layerId).ThenByDescending(p => p.areaSize))
            //{
            //    paint = w.paintOp.paint;
            //    if (paint.Color.Alpha == 0)
            //        continue; //This area is transparent, skip drawing it entirely.

            //    if (stats.degreesPerPixelX > w.paintOp.maxDrawRes || stats.degreesPerPixelX < w.paintOp.minDrawRes)
            //        continue; //This area isn't drawn at this scale.

            //    var path = new SKPath();
            //    switch (w.elementGeometry.GeometryType)
            //    {
            //        //Polygons without holes are super easy and fast: draw the path.
            //        //Polygons with holes require their own bitmap to be drawn correctly and then overlaid onto the canvas.
            //        //I want to use paths to fix things for performance reasons, but I have to use Bitmaps because paths apply their blend mode to
            //        //ALL elements already drawn, not just the last one.
            //        case "Polygon":
            //            var p = w.elementGeometry as Polygon;

            //            path.AddPoly(Converters.PolygonToSKPoints(p, stats.area, stats.degreesPerPixelX, stats.degreesPerPixelY));
            //            foreach (var hole in p.InteriorRings)
            //                path.AddPoly(Converters.PolygonToSKPoints(hole, stats.area, stats.degreesPerPixelX, stats.degreesPerPixelY));
            //            canvas.DrawPath(path, paint);

            //            break;
            //        case "MultiPolygon":
            //            foreach (var p2 in ((MultiPolygon)w.elementGeometry).Geometries)
            //            {
            //                var p2p = p2 as Polygon;
            //                path.AddPoly(Converters.PolygonToSKPoints(p2p, stats.area, stats.degreesPerPixelX, stats.degreesPerPixelY));
            //                foreach (var hole in p2p.InteriorRings)
            //                    path.AddPoly(Converters.PolygonToSKPoints(hole, stats.area, stats.degreesPerPixelX, stats.degreesPerPixelY));
            //                canvas.DrawPath(path, paint);
            //            }
            //            break;
            //        case "LineString":
            //            var firstPoint = w.elementGeometry.Coordinates.First();
            //            var lastPoint = w.elementGeometry.Coordinates.Last();
            //            var points = Converters.PolygonToSKPoints(w.elementGeometry, stats.area, stats.degreesPerPixelX, stats.degreesPerPixelY);
            //            if (firstPoint.Equals(lastPoint))
            //            {
            //                //This is a closed shape. Check to see if it's supposed to be filled in.
            //                if (paint.Style == SKPaintStyle.Fill)
            //                {
            //                    path.AddPoly(points);
            //                    canvas.DrawPath(path, paint);
            //                    continue;
            //                }
            //            }
            //            for (var line = 0; line < points.Length - 1; line++)
            //                canvas.DrawLine(points[line], points[line + 1], paint);
            //            break;
            //        case "MultiLineString":
            //            foreach (var p3 in ((MultiLineString)w.elementGeometry).Geometries)
            //            {
            //                //TODO: might want to see if I need to move the LineString logic here, or if multiline string are never filled areas.
            //                var points2 = Converters.PolygonToSKPoints(p3, stats.area, stats.degreesPerPixelX, stats.degreesPerPixelY);
            //                for (var line = 0; line < points2.Length - 1; line++)
            //                    canvas.DrawLine(points2[line], points2[line + 1], paint);
            //            }
            //            break;
            //        case "Point":
            //            var convertedPoint = Converters.PolygonToSKPoints(w.elementGeometry, stats.area, stats.degreesPerPixelX, stats.degreesPerPixelY);
            //            //If this type has an icon, use it. Otherwise draw a circle in that type's color.
            //            if (!string.IsNullOrEmpty(w.paintOp.fileName))
            //            {
            //                SKBitmap icon = TagParser.cachedBitmaps[w.paintOp.fileName];
            //                canvas.DrawBitmap(icon, convertedPoint[0]);
            //            }
            //            else
            //            {
            //                var circleRadius = (float)(ConstantValues.resolutionCell10 / stats.degreesPerPixelX / 2); //I want points to be drawn as 1 Cell10 in diameter.
            //                canvas.DrawCircle(convertedPoint[0], circleRadius, paint);
            //            }
            //            break;
            //        default:
            //            Log.WriteLog("Unknown geometry type found, not drawn.");
            //            break;
            //    }
            //}
            //canvas.Flush();
            //canvas.Dispose();
            //canvas = null;
            //s.Position = 0;
            //var svgData = new StreamReader(s).ReadToEnd();
            //return svgData;
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
        /// <returns>the Rgba32 saved into the requested background paint object.</returns>
        public Rgba32 GetStyleBgColorString(string styleSet)
        {
            return Rgba32.ParseHex(TagParser.allStyleGroups[styleSet]["background"].paintOperations.First().HtmlColorCode);
        }

        public SixLabors.ImageSharp.Drawing.Polygon PolygonToDrawingPolygon(Geometry place, GeoArea drawingArea, double resolutionX, double resolutionY)
        {
            var typeConvertedPoints = place.Coordinates.Select(o => new SixLabors.ImageSharp.PointF((float)((o.X - drawingArea.WestLongitude) * (1 / resolutionX)), (float)((o.Y - drawingArea.SouthLatitude) * (1 / resolutionY))));
            SixLabors.ImageSharp.Drawing.LinearLineSegment part = new SixLabors.ImageSharp.Drawing.LinearLineSegment(typeConvertedPoints.ToArray());
            var output = new SixLabors.ImageSharp.Drawing.Polygon(part);
            return output;
        }

        public SixLabors.ImageSharp.Drawing.LinearLineSegment PolygonToDrawingLine(Geometry place, GeoArea drawingArea, double resolutionX, double resolutionY)
        {
            //TODO: does this handle holes nicely? It should if I add them to the path.
            var typeConvertedPoints = place.Coordinates.Select(o => new SixLabors.ImageSharp.PointF((float)((o.X - drawingArea.WestLongitude) * (1 / resolutionX)), (float)((o.Y - drawingArea.SouthLatitude) * (1 / resolutionY))));
            SixLabors.ImageSharp.Drawing.LinearLineSegment part = new SixLabors.ImageSharp.Drawing.LinearLineSegment(typeConvertedPoints.ToArray());
            return part;
        }

        public SixLabors.ImageSharp.PointF[] LineToDrawingLine(Geometry place, GeoArea drawingArea, double resolutionX, double resolutionY)
        {
            var typeConvertedPoints = place.Coordinates.Select(o => new SixLabors.ImageSharp.PointF((float)((o.X - drawingArea.WestLongitude) * (1 / resolutionX)), (float)((o.Y - drawingArea.SouthLatitude) * (1 / resolutionY)))).ToList();
            return typeConvertedPoints.ToArray();
        }

        public SixLabors.ImageSharp.PointF PointToPointF(Geometry place, GeoArea drawingArea, double resolutionX, double resolutionY)
        {
            var coord = place.Coordinate;
            return new SixLabors.ImageSharp.PointF((float)((coord.X - drawingArea.WestLongitude) * (1 / resolutionX)), (float)((coord.Y - drawingArea.SouthLatitude) * (1 / resolutionY)));
        }
    }
}
