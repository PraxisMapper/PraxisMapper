using Google.OpenLocationCode;
using NetTopologySuite.Geometries;
using PraxisCore.Support;
using SkiaSharp;
using static PraxisCore.ConstantValues;
using static PraxisCore.DbTables;
using static PraxisCore.Place;

namespace PraxisCore
{
    /// <summary>
    /// All functions related to generating or expiring map tiles. Both PlusCode sized tiles for gameplay or SlippyMap tiles for a webview.
    /// </summary>
    public class MapTiles : IMapTiles
    {
        //These need to exist because the interface defines them.
        public static int MapTileSizeSquare = 512;
        public static double GameTileScale = 2;
        public static double bufferSize = resolutionCell10;

        static SKPaint eraser = new SKPaint() { Color = SKColors.Transparent, BlendMode = SKBlendMode.Src, Style = SKPaintStyle.StrokeAndFill }; //BlendMode is the important part for an Eraser.
        static Random r = new Random();
        static Dictionary<string, SKBitmap> cachedBitmaps = new Dictionary<string, SKBitmap>(); //Icons for points separate from pattern fills, though I suspect if I made a pattern fill with the same size as the icon I wouldn't need this.
        static Dictionary<long, SKPaint> cachedPaints = new Dictionary<long, SKPaint>(); 

        public void Initialize()
        {
            foreach (var b in TagParser.cachedBitmaps)
                cachedBitmaps.Add(b.Key, SKBitmap.Decode(b.Value));

            foreach (var g in TagParser.allStyleGroups)
                foreach(var s in g.Value)
                    foreach(var p in s.Value.PaintOperations)
                        cachedPaints.Add(p.Id, SetPaintForTPP(p));
        }

        /// <summary>
        /// Create the SKPaint object for each style and store it in the requested object.
        /// </summary>
        /// <param name="tpe">the TagParserPaint object to populate</param>
        private static SKPaint SetPaintForTPP(StylePaint tpe)
        {
            var paint = new SKPaint();

            paint.StrokeJoin = SKStrokeJoin.Round;
            paint.IsAntialias = true;
            paint.Color = SKColor.Parse(tpe.HtmlColorCode);
            if (tpe.FillOrStroke == "fill")
                paint.Style = SKPaintStyle.StrokeAndFill;
            else
                paint.Style = SKPaintStyle.Stroke;
            paint.StrokeWidth = tpe.LineWidthDegrees;
            paint.StrokeCap = SKStrokeCap.Round;
            if (tpe.LinePattern != "solid")
            {
                float[] linesAndGaps = tpe.LinePattern.Split('|').Select(t => float.Parse(t)).ToArray();
                paint.PathEffect = SKPathEffect.CreateDash(linesAndGaps, 0);
                paint.StrokeCap = SKStrokeCap.Butt;
            }
            if (!string.IsNullOrEmpty(tpe.FileName))
            {
                SKBitmap fillPattern = cachedBitmaps[tpe.FileName];
                //cachedBitmaps.TryAdd(tpe.fileName, fillPattern); //For icons.
                SKShader tiling = SKShader.CreateBitmap(fillPattern, SKShaderTileMode.Repeat, SKShaderTileMode.Repeat); //For fill patterns.
                paint.Shader = tiling;
            }
            return paint;
        }

        /// <summary>
        /// Draw square boxes around each area to approximate how they would behave in an offline app
        /// </summary>
        /// <param name="info">the image information for drawing</param>
        /// <param name="items">the elements to draw.</param>
        /// <returns>byte array of the generated .png tile image</returns>
        public byte[] DrawOfflineEstimatedAreas(ImageStats info, List<DbTables.Place> items)
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
                fillpaint.Color =  SKColor.Parse(TagParser.PickStaticColorForArea(pi.Name));
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
            var skms = new SKManagedWStream(ms);
            bitmap.Encode(skms, SKEncodedImageFormat.Png, 100);
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
            int imageSizeX = IMapTiles.SlippyTileSizeSquare;
            int imageSizeY = IMapTiles.SlippyTileSizeSquare;
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
            int imageSizeX = IMapTiles.SlippyTileSizeSquare;
            int imageSizeY = IMapTiles.SlippyTileSizeSquare;
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
            Coordinate[] coords = pointToConvert.Select(p => new Coordinate(double.Parse(p.Split(',')[0]), double.Parse(p.Split(',')[1]))).ToArray();

            var mapBuffer = resolutionCell8 / 2; //Leave some area around the edges of where they went.
            GeoArea mapToDraw = new GeoArea(coords.Min(c => c.Y) - mapBuffer, coords.Min(c => c.X) - mapBuffer, coords.Max(c => c.Y) + mapBuffer, coords.Max(c => c.X) + mapBuffer);

            ImageStats info = new ImageStats(mapToDraw, 1024, 1024);

            LineString line = new LineString(coords);
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
        public byte[] DrawAreaAtSize(ImageStats stats, List<DbTables.Place> drawnItems = null, string styleSet = "mapTiles", bool filterSmallAreas = true)
        {
            //This is the new core drawing function. Takes in an area, the items to draw, and the size of the image to draw. 
            //The drawn items get their paint pulled from the TagParser's list. If I need multiple match lists, I'll need to make a way
            //to pick which list of tagparser rules to use.
            //This can work for user data by using the linked Places from the items in PlaceGameData.
            //I need a slightly different function for using AreaGameData, or another optional parameter here

            double minimumSize = 0;
            if (filterSmallAreas)
            {
                minimumSize = stats.degreesPerPixelX * 8; //don't draw small elements. THis runs on perimeter/length
            }

            //Single points are excluded separately so that small areas or lines can still be drawn when points aren't.
            bool includePoints = true;
            if (stats.degreesPerPixelX > ConstantValues.zoom14DegPerPixelX)
                includePoints = false;

            if (drawnItems == null)
                drawnItems = GetPlaces(stats.area, filterSize: minimumSize, includePoints: includePoints);

            var paintOps = MapTileSupport.GetPaintOpsForStoredElements(drawnItems, "mapTiles", stats);
            return DrawAreaAtSize(stats, paintOps);
        }


        public byte[] DrawAreaAtSize(ImageStats stats, List<CompletePaintOp> paintOps)
        {
            //This is the new core drawing function. Once the paint operations have been created, I just draw them here.
            //baseline image data stuff           
            SKBitmap bitmap = new SKBitmap(stats.imageSizeX, stats.imageSizeY, SKColorType.Rgba8888, SKAlphaType.Premul);
            SKCanvas canvas = new SKCanvas(bitmap);
            canvas.Clear(eraser.Color);
            canvas.Scale(1, -1, stats.imageSizeX / 2, stats.imageSizeY / 2);
            SKPaint paint = new SKPaint();

            foreach (var w in paintOps.OrderByDescending(p => p.paintOp.LayerId).ThenByDescending(p => p.areaSize))
            {
                paint = cachedPaints[w.paintOp.Id]; //SetPaintForTPP(w.paintOp); // w.paintOp.paint;

                if (w.paintOp.FromTag) //FromTag is for when you are saving color data directly to each element, instead of tying it to a styleset.
                    paint.Color = SKColor.Parse(w.tagValue);

                if (w.paintOp.Randomize) //To randomize the color on every Draw call.
                    paint.Color = new SKColor((byte)r.Next(0, 255), (byte)r.Next(0, 255), (byte)r.Next(0, 255), 99);

                paint.StrokeWidth = (float)w.lineWidthPixels;
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
                            var points2 = PolygonToSKPoints(p3, stats.area, stats.degreesPerPixelX, stats.degreesPerPixelY);
                            for (var line = 0; line < points2.Length - 1; line++)
                                canvas.DrawLine(points2[line], points2[line + 1], paint);
                        }
                        break;
                    case "Point":
                        var convertedPoint = PolygonToSKPoints(w.elementGeometry, stats.area, stats.degreesPerPixelX, stats.degreesPerPixelY);
                        //If this type has an icon, use it. Otherwise draw a circle in that type's color.
                        if (!string.IsNullOrEmpty(w.paintOp.FileName))
                        {
                            SKBitmap icon = SKBitmap.Decode(TagParser.cachedBitmaps[w.paintOp.FileName]); //TODO optimize by making icons in Initialize.
                            canvas.DrawBitmap(icon, convertedPoint[0]);
                        }
                        else
                        {
                            var circleRadius = (float)(ConstantValues.resolutionCell10 / stats.degreesPerPixelX / 2); //I want points to be drawn as 1 Cell10 in diameter.
                            canvas.DrawCircle(convertedPoint[0], circleRadius, paint);
                            //TODO re-add outline paint to this DLL not TagParser.
                            //canvas.DrawCircle(convertedPoint[0], circleRadius, TagParser.outlinePaint); 
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
        /// Creates an SVG image instead of a PNG file, but otherwise operates the same as DrawAreaAtSize.
        /// </summary>
        /// <param name="stats">the image properties to draw</param>
        /// <param name="drawnItems">the list of elements to draw. Will load from the database if null.</param>
        /// <param name="styles">a dictionary of TagParserEntries to select to draw</param>
        /// <param name="filterSmallAreas">if true, skips entries below a certain size when drawing.</param>
        /// <returns>a string containing the SVG XML</returns>
        public string DrawAreaAtSizeSVG(ImageStats stats, List<DbTables.Place> drawnItems = null, Dictionary<string, StyleEntry> styles = null, bool filterSmallAreas = true)
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
            var bgColor = SKColor.Parse(styles["background"].PaintOperations.First().HtmlColorCode);
            //Backgound is a named style, unmatched will be the last entry and transparent.
            canvas.Clear(bgColor);
            canvas.Scale(1, -1, stats.imageSizeX / 2, stats.imageSizeY / 2);
            SKPaint paint = new SKPaint();

            //I guess what I want here is a list of an object with an elementGeometry object for the shape, and a paintOp attached to it
            var pass1 = drawnItems.Select(d => new { d.AreaSize, d.ElementGeometry, paintOp = styles[d.GameElementName].PaintOperations });
            var pass2 = new List<CompletePaintOp>(drawnItems.Count() * 2);
            foreach (var op in pass1)
                foreach (var po in op.paintOp)
                    pass2.Add(new CompletePaintOp(op.ElementGeometry, op.AreaSize, po, "", po.LineWidthDegrees * stats.pixelsPerDegreeX));


            foreach (var w in pass2.OrderByDescending(p => p.paintOp.LayerId).ThenByDescending(p => p.areaSize))
            {
                paint = cachedPaints[w.paintOp.Id];
                if (paint.Color.Alpha == 0)
                    continue; //This area is transparent, skip drawing it entirely.

                if (stats.degreesPerPixelX > w.paintOp.MaxDrawRes || stats.degreesPerPixelX < w.paintOp.MinDrawRes)
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
                            var points2 = PolygonToSKPoints(p3, stats.area, stats.degreesPerPixelX, stats.degreesPerPixelY);
                            for (var line = 0; line < points2.Length - 1; line++)
                                canvas.DrawLine(points2[line], points2[line + 1], paint);
                        }
                        break;
                    case "Point":
                        var convertedPoint = PolygonToSKPoints(w.elementGeometry, stats.area, stats.degreesPerPixelX, stats.degreesPerPixelY);
                        //If this type has an icon, use it. Otherwise draw a circle in that type's color.
                        if (!string.IsNullOrEmpty(w.paintOp.FileName))
                        {
                            SKBitmap icon = SKBitmap.Decode(TagParser.cachedBitmaps[w.paintOp.FileName]); //TODO optimize by creating in Initialize
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
            var color = SKColor.Parse(TagParser.allStyleGroups[styleSet]["background"].PaintOperations.First().HtmlColorCode);
            return color;
        }

        //public string GetStyleBgColorString(string styleSet)
        //{
            //return TagParser.allStyleGroups[styleSet]["background"].paintOperations.First().HtmlColorCode;
        //}

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