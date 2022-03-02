using Google.OpenLocationCode;
using NetTopologySuite.Geometries;
using PraxisCore.Support;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
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
        public int MapTileSizeSquare = 512; //Default value, updated by PraxisMapper at startup. COvers Slippy tiles, not gameplay tiles.
        public double GameTileScale = 2;
        public double bufferSize = resolutionCell10; //How much space to add to an area to make sure elements are drawn correctly. Mostly to stop points from being clipped.
        //static SKPaint eraser = new SKPaint() { Color = SKColors.Transparent, BlendMode = SKBlendMode.Src, Style = SKPaintStyle.StrokeAndFill }; //BlendMode is the important part for an Eraser.
        static Random r = new Random();
        public static Dictionary<string, Image> cachedBitmaps = new Dictionary<string, Image>(); //Icons for points separate from pattern fills, though I suspect if I made a pattern fill with the same size as the icon I wouldn't need this.
        public static Dictionary<long, IBrush> cachedPaints = new Dictionary<long, IBrush>();
        public static Dictionary<long, IPen> cachedGameTilePens = new Dictionary<long, IPen>();

        static DrawingOptions dOpts;

        public void Initialize()
        {
            IMapTiles.GameTileScale = GameTileScale;
            IMapTiles.MapTileSizeSquare = MapTileSizeSquare;
            IMapTiles.bufferSize = bufferSize;

            foreach (var b in TagParser.cachedBitmaps)
                cachedBitmaps.Add(b.Key, Image.Load(b.Value));

            foreach (var g in TagParser.allStyleGroups)
                foreach (var s in g.Value)
                    foreach (var p in s.Value.PaintOperations)
                        cachedPaints.Add(p.Id, SetPaintForTPP(p)); //this fails if loaded from defaults since all IDs are 0.

            dOpts = new DrawingOptions();
            ShapeOptions so = new ShapeOptions();
            so.IntersectionRule = IntersectionRule.OddEven; //already the default;
            GraphicsOptions go = new GraphicsOptions();
            go.Antialias = true;
            go.AntialiasSubpixelDepth = 16; //defaults to 16, would 4 improve speed? would 25 or 64 improve quality? (not visible so from early testing.)
            dOpts.GraphicsOptions = go;
            dOpts.ShapeOptions = so;
        }

        /// <summary>
        /// Create the Brush object for each style and store it for later use.
        /// </summary>
        /// <param name="tpe">the TagParserPaint object to populate</param>
        private static IBrush SetPaintForTPP(TagParserPaint tpe)
        {
            //SkiaSharp now implements rounding line ends, but they're only for Pens
            //(which only work on lines), and my stuff all currently uses a Brush.

            //It's possible that I want pens instead of brushes for lines with patterns?
            string htmlColor = tpe.HtmlColorCode;
            if (htmlColor.Length == 8)
                htmlColor = htmlColor.Substring(2, 6) + htmlColor.Substring(0, 2);
            IBrush paint = new SolidBrush(Rgba32.ParseHex(htmlColor));

            if (!string.IsNullOrEmpty(tpe.FileName))
                paint = new ImageBrush(cachedBitmaps[tpe.FileName]);

            return paint;
        }

        //private static IPen SetPenForTPPGameTile(TagParserPaint tpe)
        //{
        //    //These pens are saved with a fixed drawing with for using a Cell11 as a pixel (modified by tile scale value)
        //    var widthMod = resolutionCell11Lon * IMapTiles.GameTileScale;

        //    string htmlColor = tpe.HtmlColorCode;
        //    if (htmlColor.Length == 8)
        //        htmlColor = htmlColor.Substring(2, 6) + htmlColor.Substring(0, 2);



        //    //Pen p = new Pen(Rgba32.ParseHex(htmlColor), tpe.LineWidth * widthMod )

        //    //return paint;
        //}

        /// <summary>
        /// Draw square boxes around each area to approximate how they would behave in an offline app
        /// </summary>
        /// <param name="info">the image information for drawing</param>
        /// <param name="items">the elements to draw.</param>
        /// <returns>byte array of the generated .png tile image</returns>
        public byte[] DrawOfflineEstimatedAreas(ImageStats info, List<DbTables.Place> items)
        {
            //TODO retest this.
            var image = new Image<Rgba32>(info.imageSizeX, info.imageSizeY);
            var bgColor = Rgba32.ParseHex("00000000");
            image.Mutate(x => x.Fill(bgColor));
            var fillColor = Rgba32.ParseHex("000000");
            var strokeColor = Rgba32.ParseHex("000000");

            var placeInfo = Standalone.Standalone.GetPlaceInfo(items.Where(i =>
            i.IsGameElement
            ).ToList());

            //this is for rectangles.
            foreach (var pi in placeInfo)
            {
                var rect = PlaceInfoToRect(pi, info);
                fillColor = Rgba32.ParseHex(TagParser.PickStaticColorForArea(pi.Name));
                image.Mutate(x => x.Fill(fillColor, rect));
                image.Mutate(x => x.Draw(strokeColor, 3, rect));
            }

            image.Mutate(x => x.Flip(FlipMode.Vertical)); ; //inverts the inverted image again!
            foreach (var pi in placeInfo)
            {
                //NOTE: would be better to load fonts once and share that for the app's lifetime.
                var fonts = new SixLabors.Fonts.FontCollection();
                var family = fonts.Add("fontHere.ttf");
                var font = family.CreateFont(12, FontStyle.Regular);
                var rect = PlaceInfoToRect(pi, info);
                image.Mutate(x => x.DrawText(pi.Name, font, strokeColor, new PointF((float)(pi.lonCenter * info.pixelsPerDegreeX), (float)(pi.latCenter * info.pixelsPerDegreeY))));
            }

            image.Mutate(x => x.Flip(FlipMode.Vertical)); //Plus codes are south-to-north, so invert the image to make it correct.
            var ms = new MemoryStream();
            image.SaveAsPng(ms);
            return ms.ToArray();
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
            Image<Rgba32> image = new Image<Rgba32>(imageSizeX, imageSizeY);
            var bgColor = Rgba32.ParseHex("00000000");
            image.Mutate(x => x.Fill(bgColor));

            var lineColor = Rgba32.ParseHex("FF0000");
            var StrokeWidth = 3;

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
                var points = PolygonToDrawingLine(geoLine, totalArea, degreesPerPixelX, degreesPerPixelY);
                image.Mutate(x => x.Draw(lineColor, StrokeWidth, new SixLabors.ImageSharp.Drawing.Path(points)));
                lonLineTrackerDegrees += resolutionCell8;
            }

            double latLineTrackerDegrees = imageBottom - spaceToFirstLineBottom; //This is degree coords
            while (latLineTrackerDegrees <= totalArea.NorthLatitude + resolutionCell8) //This means we should always draw at least 2 lines, even if they're off-canvas.
            {
                var geoLine = new LineString(new Coordinate[] { new Coordinate(180, latLineTrackerDegrees), new Coordinate(-180, latLineTrackerDegrees) });
                var points = PolygonToDrawingLine(geoLine, totalArea, degreesPerPixelX, degreesPerPixelY);
                image.Mutate(x => x.Draw(lineColor, StrokeWidth, new SixLabors.ImageSharp.Drawing.Path(points)));
                latLineTrackerDegrees += resolutionCell8;
            }

            image.Mutate(x => x.Flip(FlipMode.Vertical)); //Plus codes are south-to-north, so invert the image to make it correct.
            var ms = new MemoryStream();
            image.SaveAsPng(ms);
            return ms.ToArray();
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
            Image<Rgba32> image = new Image<Rgba32>(imageSizeX, imageSizeY);
            var bgColor = Rgba32.ParseHex("00000000");
            image.Mutate(x => x.Fill(bgColor));

            var lineColor = Rgba32.ParseHex("00CCFF");
            var StrokeWidth = 1;

            double degreesPerPixelX = totalArea.LongitudeWidth / imageSizeX;
            double degreesPerPixelY = totalArea.LatitudeHeight / imageSizeY;

            //This is hardcoded to Cell 10 spaced gridlines.
            var imageLeft = totalArea.WestLongitude;
            var spaceToFirstLineLeft = (imageLeft % resolutionCell10);

            var imageBottom = totalArea.SouthLatitude;
            var spaceToFirstLineBottom = (imageBottom % resolutionCell10);

            double lonLineTrackerDegrees = imageLeft - spaceToFirstLineLeft; //This is degree coords
            while (lonLineTrackerDegrees <= totalArea.EastLongitude + resolutionCell10) //This means we should always draw at least 2 lines, even if they're off-canvas.
            {
                var geoLine = new LineString(new Coordinate[] { new Coordinate(lonLineTrackerDegrees, 90), new Coordinate(lonLineTrackerDegrees, -90) });
                var points = PolygonToDrawingLine(geoLine, totalArea, degreesPerPixelX, degreesPerPixelY);
                image.Mutate(x => x.Draw(lineColor, StrokeWidth, new SixLabors.ImageSharp.Drawing.Path(points)));
                lonLineTrackerDegrees += resolutionCell10;
            }

            double latLineTrackerDegrees = imageBottom - spaceToFirstLineBottom; //This is degree coords
            while (latLineTrackerDegrees <= totalArea.NorthLatitude + resolutionCell10) //This means we should always draw at least 2 lines, even if they're off-canvas.
            {
                var geoLine = new LineString(new Coordinate[] { new Coordinate(180, latLineTrackerDegrees), new Coordinate(-180, latLineTrackerDegrees) });
                var points = PolygonToDrawingLine(geoLine, totalArea, degreesPerPixelX, degreesPerPixelY);
                image.Mutate(x => x.Draw(lineColor, StrokeWidth, new SixLabors.ImageSharp.Drawing.Path(points)));
                latLineTrackerDegrees += resolutionCell10;
            }

            image.Mutate(x => x.Flip(FlipMode.Vertical)); //Plus codes are south-to-north, so invert the image to make it correct.
            var ms = new MemoryStream();
            image.SaveAsPng(ms);
            return ms.ToArray();
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
            var drawableLine = PolygonToDrawingLine(line, mapToDraw, info.degreesPerPixelX, info.degreesPerPixelY);

            //Now, draw that path on the map.
            var places = GetPlaces(mapToDraw);
            var baseImage = DrawAreaAtSize(info, places);

            Image<Rgba32> image = new Image<Rgba32>(info.imageSizeX, info.imageSizeY);
            Rgba32 strokeColor = Rgba32.ParseHex("000000");
            image.Mutate(x => x.Draw(strokeColor, 4, new SixLabors.ImageSharp.Drawing.Path(drawableLine)));

            image.Mutate(x => x.Flip(FlipMode.Vertical)); //Plus codes are south-to-north, so invert the image to make it correct.
            var ms = new MemoryStream();
            image.SaveAsPng(ms);
            return ms.ToArray();
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
            //This can work for user data by using the linked StoredOsmElements from the items in CustomDataStoredElement.
            //I need a slightly different function for using CustomDataPlusCode, or another optional parameter here

            //This should just get the paint ops then call the core drawing function.
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

            var paintOps = MapTileSupport.GetPaintOpsForStoredElements(drawnItems, styleSet, stats);
            return DrawAreaAtSize(stats, paintOps);
        }

        public byte[] DrawAreaAtSize(ImageStats stats, List<CompletePaintOp> paintOps)
        {
            //THIS is the core drawing function, and other version should call this so there's 1 function that handles the inner loop.
            //baseline image data stuff           
            var image = new Image<Rgba32>(stats.imageSizeX, stats.imageSizeY);
            foreach (var w in paintOps.OrderByDescending(p => p.paintOp.LayerId).ThenByDescending(p => p.areaSize))
            {
                //I need paints for fill commands and images. 
                var paint = cachedPaints[w.paintOp.Id];

                if (w.paintOp.Randomize) //To randomize the color on every Draw call.
                    w.paintOp.HtmlColorCode = "99" + ((byte)r.Next(0, 255)).ToString() +  ((byte)r.Next(0, 255)).ToString() + ((byte)r.Next(0, 255)).ToString();

                //TODO: use stats to see if this image is scaled to gameTile values, and if so then use cached pre-made pens?
                Pen pen;
                if (String.IsNullOrWhiteSpace(w.paintOp.LinePattern) || w.paintOp.LinePattern == "solid")
                    pen = new Pen(Rgba32.ParseHex(w.paintOp.HtmlColorCode), w.paintOp.LineWidth);
                else
                {
                    float[] linesAndGaps = w.paintOp.LinePattern.Split('|').Select(t => float.Parse(t)).ToArray();
                    pen = new Pen(Rgba32.ParseHex(w.paintOp.HtmlColorCode), w.paintOp.LineWidth, linesAndGaps);
                }

                //ImageSharp doesn;t like humungous areas (16k+ nodes takes a couple minutes), so we have to crop them down here
                Geometry thisGeometry = w.elementGeometry; //default
                //This block below is fairly imporant because of Path.Clip() performance. I would still prefer to do this over the original way of handling holes in paths (draw bitmap of outer polygons, erase holes with eraser paint, draw that bitmap over maptile)
                //it doesn't ALWAYS cause problems if I skip this, but when it does it takes forever to draw some tiles. Keep this in even if it only seems to happen with debug mode on.
                if (w.elementGeometry.Coordinates.Length > 800)
                    thisGeometry = w.elementGeometry.Intersection(Converters.GeoAreaToPolygon(GeometrySupport.MakeBufferedGeoArea(stats.area, resolutionCell10)));
                if (thisGeometry.Coordinates.Length == 0) //After trimming, linestrings may not have any points in the drawing area.
                    continue;

                switch (thisGeometry.GeometryType)
                {
                    case "Polygon":
                        //after trimming this might not work out as well. Don't draw broken/partial polygons? or only as lines?
                        if (thisGeometry.Coordinates.Length > 2)
                        {
                            var drawThis = PolygonToDrawingPolygon(thisGeometry, stats.area, stats.degreesPerPixelX, stats.degreesPerPixelY);
                            if (w.paintOp.FillOrStroke == "fill")
                                image.Mutate(x => x.Fill(dOpts, paint, drawThis));
                            else
                                image.Mutate(x => x.Draw(dOpts, pen, drawThis));
                        }
                        break;
                    case "MultiPolygon":
                        foreach (NetTopologySuite.Geometries.Polygon p2 in ((MultiPolygon)thisGeometry).Geometries)
                        {
                            var drawThis2 = PolygonToDrawingPolygon(p2, stats.area, stats.degreesPerPixelX, stats.degreesPerPixelY);
                            if (w.paintOp.FillOrStroke == "fill")
                                image.Mutate(x => x.Fill(dOpts, paint, drawThis2));
                            else
                                image.Mutate(x => x.Draw(dOpts, pen, drawThis2));
                        }
                        break;
                    case "LineString":
                        var firstPoint = thisGeometry.Coordinates.First();
                        var lastPoint = thisGeometry.Coordinates.Last();
                        var line = LineToDrawingLine(thisGeometry, stats.area, stats.degreesPerPixelX, stats.degreesPerPixelY);

                        if (firstPoint.Equals(lastPoint) && w.paintOp.FillOrStroke == "fill")
                            image.Mutate(x => x.Fill(dOpts, paint, new SixLabors.ImageSharp.Drawing.Polygon(new LinearLineSegment(line))));
                        else
                            image.Mutate(x => x.DrawLines(dOpts, pen, line));
                        break;
                    case "MultiLineString":
                        foreach (var p3 in ((MultiLineString)thisGeometry).Geometries)
                        {
                            var line2 = LineToDrawingLine(p3, stats.area, stats.degreesPerPixelX, stats.degreesPerPixelY);
                            image.Mutate(x => x.DrawLines(dOpts, pen, line2));
                        }
                        break;
                    case "Point":
                        var convertedPoint = PointToPointF(thisGeometry, stats.area, stats.degreesPerPixelX, stats.degreesPerPixelY);
                        //If this type has an icon, use it. Otherwise draw a circle in that type's color.
                        if (!string.IsNullOrEmpty(w.paintOp.FileName))
                        {
                            //TODO test that this draws in correct position.
                            Image i2 = cachedBitmaps[w.paintOp.FileName];
                            image.Mutate(x => x.DrawImage(i2, (SixLabors.ImageSharp.Point)convertedPoint, 1));
                        }
                        else
                        {
                            var circleRadius = (float)w.lineWidth; //(float)(ConstantValues.resolutionCell10 / stats.degreesPerPixelX / 2); //I want points to be drawn as 1 Cell10 in diameter.
                            var shape = new SixLabors.ImageSharp.Drawing.EllipsePolygon(
                                PointToPointF(thisGeometry, stats.area, stats.degreesPerPixelX, stats.degreesPerPixelY),
                                new SizeF(circleRadius, circleRadius));
                            image.Mutate(x => x.Fill(dOpts, paint, shape));
                            image.Mutate(x => x.Draw(dOpts, Color.Black, 1, shape)); //NOTE: this gets overlapped by other elements in the same (or higher) layers, maybe outlines should be their own layer again.
                        }
                        break;
                    default:
                        Log.WriteLog("Unknown geometry type found, not drawn.");
                        break;
                }
            }

            image.Mutate(x => x.Flip(FlipMode.Vertical)); //Plus codes are south-to-north, so invert the image to make it correct. TODO: could I use the matrix to skip this step?
            image.Mutate(x => x.BoxBlur(1)); //This does help smooth out some of the rough edges on the game tiles. 
            var ms = new MemoryStream();
            image.SaveAsPng(ms);
            return ms.ToArray();
        }

        /// <summary>
        /// ImageSharp doesn't support this at all. Throws NotImplementedException when called.
        /// </summary>
        public string DrawAreaAtSizeSVG(ImageStats stats, List<DbTables.Place> drawnItems = null, Dictionary<string, TagParserEntry> styles = null, bool filterSmallAreas = true)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Draws 1 tile overtop the other.
        /// </summary>
        /// <param name="info">Unused in this implementation, but requires per the interface.</param>
        /// <param name="bottomTile">the tile to use as the base of the image. Expected to be opaque.</param>
        /// <param name="topTile">The tile to layer on top. Expected to be at least partly transparent or translucent.</param>
        /// <returns></returns>
        public byte[] LayerTiles(ImageStats info, byte[] bottomTile, byte[] topTile)
        {
            Image i1 = Image.Load(bottomTile);
            Image i2 = Image.Load(topTile);

            i1.Mutate(x => x.DrawImage(i2, 1));
            var ms = new MemoryStream();
            i1.SaveAsPng(ms);
            return ms.ToArray();
        }

        /// <summary>
        /// Get the background color from a style set
        /// </summary>
        /// <param name="styleSet">the name of the style set to pull the background color from</param>
        /// <returns>the Rgba32 saved into the requested background paint object.</returns>
        public Rgba32 GetStyleBgColorString(string styleSet)
        {
            var color = Rgba32.ParseHex(TagParser.allStyleGroups[styleSet]["background"].PaintOperations.First().HtmlColorCode);
            return color;
        }

        public IPath PolygonToDrawingPolygon(Geometry place, GeoArea drawingArea, double resolutionX, double resolutionY)
        {
            var lineSegmentList = new List<LinearLineSegment>();
            NetTopologySuite.Geometries.Polygon p = (NetTopologySuite.Geometries.Polygon)place;
            var typeConvertedPoints = p.ExteriorRing.Coordinates.Select(o => new SixLabors.ImageSharp.PointF((float)((o.X - drawingArea.WestLongitude) * (1 / resolutionX)), (float)((o.Y - drawingArea.SouthLatitude) * (1 / resolutionY))));
            var path = new SixLabors.ImageSharp.Drawing.Path(new LinearLineSegment(typeConvertedPoints.ToArray())).AsClosedPath();

            foreach (var hole in p.InteriorRings)
            {
                typeConvertedPoints = hole.Coordinates.Select(o => new SixLabors.ImageSharp.PointF((float)((o.X - drawingArea.WestLongitude) * (1 / resolutionX)), (float)((o.Y - drawingArea.SouthLatitude) * (1 / resolutionY))));
                var tempHole = new SixLabors.ImageSharp.Drawing.Path(new LinearLineSegment(typeConvertedPoints.ToArray())).AsClosedPath();
                path = path.Clip(tempHole);
            }
            return path;
        }

        public LinearLineSegment PolygonToDrawingLine(Geometry place, GeoArea drawingArea, double resolutionX, double resolutionY)
        {
            //NOTE: this doesn't handle holes if you add them to the end in the reverse order. Those must be handled by a function in ImageSharp.
            var typeConvertedPoints = place.Coordinates.Select(o => new SixLabors.ImageSharp.PointF((float)((o.X - drawingArea.WestLongitude) * (1 / resolutionX)), (float)((o.Y - drawingArea.SouthLatitude) * (1 / resolutionY))));
            LinearLineSegment part = new LinearLineSegment(typeConvertedPoints.ToArray());
            var x = new SixLabors.ImageSharp.Drawing.Path();
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

        public Rectangle PlaceInfoToRect(PraxisCore.StandaloneDbTables.PlaceInfo2 pi, ImageStats info)
        {
            //TODO test this.
            Rectangle r = new Rectangle();
            float heightMod = (float)pi.height / 2;
            float widthMod = (float)pi.width / 2;
            r.Width = (int)(pi.width * info.pixelsPerDegreeX);
            r.Height = (int)(pi.height * info.pixelsPerDegreeY);
            r.X = (int)(pi.lonCenter * info.pixelsPerDegreeX);
            r.Y = (int)(pi.latCenter * info.pixelsPerDegreeY);

            return r;
        }
    }
}
