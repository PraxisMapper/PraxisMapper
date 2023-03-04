using Google.OpenLocationCode;
using NetTopologySuite.Geometries;
using PraxisCore.Standalone;
using PraxisCore.Support;
using SixLabors.Fonts;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using static PraxisCore.ConstantValues;
using static PraxisCore.DbTables;
using static PraxisCore.Place;

namespace PraxisCore {
    /// <summary>
    /// All functions related to generating or expiring map tiles. Both PlusCode sized tiles for gameplay or SlippyMap tiles for a webview.
    /// </summary>
    public class MapTiles : IMapTiles {
        public static Dictionary<string, Image> cachedBitmaps = new Dictionary<string, Image>(); //Icons for points separate from pattern fills, though I suspect if I made a pattern fill with the same size as the icon I wouldn't need this.
        public static Dictionary<long, IBrush> cachedPaints = new Dictionary<long, IBrush>();
        public static Dictionary<long, IPen> cachedGameTilePens = new Dictionary<long, IPen>();

        static DrawingOptions dOpts;

        public void Initialize() {
            foreach (var b in TagParser.cachedBitmaps)
                cachedBitmaps.Add(b.Key, Image.Load(b.Value));

            int maxId = 1;
            foreach (var g in TagParser.allStyleGroups)
                foreach (var s in g.Value)
                    foreach (var p in s.Value.PaintOperations) {
                        if (p.Id == 0) {
                            p.Id = maxId++;
                        }
                        cachedPaints.Add(p.Id, SetPaintForTPP(p));
                        cachedGameTilePens.Add(p.Id, SetPenForGameTile(p));
                    }

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
        private static IBrush SetPaintForTPP(StylePaint tpe) {
            string htmlColor = tpe.HtmlColorCode;
            if (htmlColor.Length == 8)
                htmlColor = string.Concat(htmlColor.AsSpan(2, 6), htmlColor.AsSpan(0, 2));
            IBrush paint = new SolidBrush(Rgba32.ParseHex(htmlColor));

            if (!string.IsNullOrEmpty(tpe.FileName))
                paint = new ImageBrush(cachedBitmaps[tpe.FileName]);

            return paint;
        }

        private static IPen SetPenForGameTile(StylePaint tpe) {
            //These pens are saved with a fixed drawing width to match game tiles.
            int imgX = 0, imgY = 0;
            MapTileSupport.GetPlusCodeImagePixelSize("22334455", out imgX, out imgY);
            var info = new ImageStats(OpenLocationCode.DecodeValid("22334455"), imgX, imgY);

            var widthMod = resolutionCell11Lon * MapTileSupport.GameTileScale;

            string htmlColor = tpe.HtmlColorCode;
            if (htmlColor.Length == 8)
                htmlColor = string.Concat(htmlColor.AsSpan(2, 6), htmlColor.AsSpan(0, 2));

            Pen p;

            if (String.IsNullOrWhiteSpace(tpe.LinePattern) || tpe.LinePattern == "solid")
                p = new Pen(Rgba32.ParseHex(htmlColor), tpe.FixedWidth != 0 ? tpe.FixedWidth : tpe.LineWidthDegrees * (float)info.pixelsPerDegreeX);
            else {
                float[] linesAndGaps = tpe.LinePattern.Split('|').Select(t => float.Parse(t)).ToArray();
                p = new Pen(Rgba32.ParseHex(htmlColor), tpe.FixedWidth != 0 ? tpe.FixedWidth : tpe.LineWidthDegrees * (float)info.pixelsPerDegreeX, linesAndGaps);
            }

            p.EndCapStyle = EndCapStyle.Round;
            p.JointStyle = JointStyle.Round;
            return p;
        }

        /// <summary>
        /// Draw square boxes around each area to approximate how they would behave in an offline app
        /// </summary>
        /// <param name="info">the image information for drawing</param>
        /// <param name="items">the elements to draw.</param>
        /// <returns>byte array of the generated .png tile image</returns>
        public byte[] DrawOfflineEstimatedAreas(ImageStats info, List<DbTables.Place> items) {
            var image = new Image<Rgba32>(info.imageSizeX, info.imageSizeY);
            var bgColor = Rgba32.ParseHex("00000000");
            image.Mutate(x => x.Fill(bgColor));
            var fillColor = Rgba32.ParseHex("000000");
            var strokeColor = Rgba32.ParseHex("000000");

            var placeInfo = Standalone.Standalone.GetPlaceInfo(items.Where(i => i.IsGameElement).ToList());

            //this is for rectangles.
            foreach (var pi in placeInfo) {
                var rect = PlaceInfoToRect(pi, info);
                fillColor = Rgba32.ParseHex(TagParser.PickStaticColorForArea(pi.Name));
                image.Mutate(x => x.Fill(fillColor, rect));
                image.Mutate(x => x.Draw(strokeColor, 3, rect));
            }

            var fonts = new SixLabors.Fonts.FontCollection();
            var family = fonts.Add("fontHere.ttf");
            var font = family.CreateFont(12, FontStyle.Regular);

            image.Mutate(x => x.Flip(FlipMode.Vertical)); ; //inverts the inverted image again!
            foreach (var pi in placeInfo) {
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
        public byte[] DrawCell8GridLines(GeoArea totalArea) {
            int imageSizeX = MapTileSupport.SlippyTileSizeSquare;
            int imageSizeY = MapTileSupport.SlippyTileSizeSquare;
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
        public byte[] DrawCell10GridLines(GeoArea totalArea) {

            int imageSizeX = MapTileSupport.SlippyTileSizeSquare;
            int imageSizeY = MapTileSupport.SlippyTileSizeSquare;
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
        public byte[] DrawUserPath(string pointListAsString) {
            //String is formatted as Lat,Lon~Lat,Lon~ repeating. Characters chosen to not be percent-encoded if submitted as part of the URL.
            //first, convert this to a list of latlon points
            string[] pointToConvert = pointListAsString.Split("|");
            Coordinate[] coords = pointToConvert.Select(p => new Coordinate(double.Parse(p.Split(',')[0]), double.Parse(p.Split(',')[1]))).ToArray();

            var mapBuffer = resolutionCell8 / 2; //Leave some area around the edges of where they went.
            GeoArea mapToDraw = new GeoArea(coords.Min(c => c.Y) - mapBuffer, coords.Min(c => c.X) - mapBuffer, coords.Max(c => c.Y) + mapBuffer, coords.Max(c => c.X) + mapBuffer);

            ImageStats info = new ImageStats(mapToDraw, 1024, 1024);

            LineString line = new LineString(coords);
            var drawableLine = PolygonToDrawingLine(line, mapToDraw, info.degreesPerPixelX, info.degreesPerPixelY);

            //Now, draw that path on the map.
            var baseImage = DrawAreaAtSize(info);

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
        public byte[] DrawAreaAtSize(ImageStats stats, List<DbTables.Place> drawnItems = null, string styleSet = "mapTiles") {
            //This is the new core drawing function. Takes in an area, the items to draw, and the size of the image to draw. 
            //The drawn items get their paint pulled from the TagParser's list. If I need multiple match lists, I'll need to make a way
            //to pick which list of tagparser rules to use.
            //This can work for user data by using the linked Places from the items in PlaceGameData.
            //I need a slightly different function for using AreaGameData, or another optional parameter here

            if (drawnItems == null)
                drawnItems = GetPlaces(stats.area, filterSize: stats.filterSize);

            var paintOps = MapTileSupport.GetPaintOpsForPlaces(drawnItems, styleSet, stats);
            return DrawAreaAtSize(stats, paintOps);
        }

        public byte[] DrawAreaAtSize(ImageStats stats, List<CompletePaintOp> paintOps) {
            //This is the new core drawing function. Once the paint operations have been created, I just draw them here.
            //baseline image data stuff
            var image = new Image<Rgba32>(stats.imageSizeX, stats.imageSizeY);
            var trimPoly = stats.area.ToPolygon();
            foreach (var w in paintOps.OrderByDescending(p => p.paintOp.LayerId).ThenByDescending(p => p.drawSizeHint)) {
                //I need paints for fill commands and images. pens for lines.
                var paint = cachedPaints[w.paintOp.Id];
                var pen = cachedGameTilePens[w.paintOp.Id];

                if (w.paintOp.Randomize) { //To randomize the color on every Draw call.
                    w.paintOp.HtmlColorCode = "99" + ((byte)Random.Shared.Next(0, 255)).ToString() + ((byte)Random.Shared.Next(0, 255)).ToString() + ((byte)Random.Shared.Next(0, 255)).ToString();
                    paint = SetPaintForTPP(w.paintOp);
                    pen = new Pen(Rgba32.ParseHex(w.paintOp.HtmlColorCode), (float)w.lineWidthPixels);
                }

                if (w.paintOp.FromTag) {  //FromTag is for when you are saving color data directly to each element, instead of tying it to a styleset.
                    w.paintOp.HtmlColorCode = w.tagValue;
                    paint = SetPaintForTPP(w.paintOp);
                    pen = new Pen(Rgba32.ParseHex(w.paintOp.HtmlColorCode), (float)w.lineWidthPixels);
                }

                if (stats.area.LongitudeWidth != resolutionCell8) {
                    //recreate pen for this operation instead of using cached pen.
                    if (String.IsNullOrWhiteSpace(w.paintOp.LinePattern) || w.paintOp.LinePattern == "solid")
                        pen = new Pen(Rgba32.ParseHex(w.paintOp.HtmlColorCode), (float)w.lineWidthPixels);
                    else {
                        float[] linesAndGaps = w.paintOp.LinePattern.Split('|').Select(t => float.Parse(t)).ToArray();
                        pen = new Pen(Rgba32.ParseHex(w.paintOp.HtmlColorCode), (float)w.lineWidthPixels, linesAndGaps);
                    }
                }

                //ImageSharp doesn't like humungous areas (16k+ nodes takes a couple minutes), so we have to crop them down here
                Geometry thisGeometry = w.elementGeometry; //default
                //This block below is fairly imporant because of Path.Clip() performance
                //it doesn't ALWAYS cause problems if I skip this, but when it does it takes forever to draw some tiles. Keep this in even if it only seems to happen with debug mode on.
                if (w.elementGeometry.Coordinates.Length > 800) //800 seems to be the sweet spot between 'cropping geometry is slow' and 'drawing big geometry is slow.'
                    thisGeometry = w.elementGeometry.Intersection(trimPoly);
                if (!thisGeometry.Coordinates.Any()) //After trimming, linestrings may not have any points in the drawing area.
                    continue;

                switch (thisGeometry.GeometryType) {
                    case "Polygon":
                        //after trimming this might not work out as well. Don't draw broken/partial polygons? or only as lines?
                        if (thisGeometry.Coordinates.Length > 2) {
                            var drawThis = PolygonToDrawingPolygon(thisGeometry, stats.area, stats.degreesPerPixelX, stats.degreesPerPixelY);
                            if (w.paintOp.FillOrStroke == "fill")
                                image.Mutate(x => x.Fill(dOpts, paint, drawThis));
                            else
                                image.Mutate(x => x.Draw(dOpts, pen, drawThis));
                        }
                        break;
                    case "MultiPolygon":
                        foreach (NetTopologySuite.Geometries.Polygon p2 in ((MultiPolygon)thisGeometry).Geometries) {
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
                        var line = LineToDrawingLine(thisGeometry, stats);

                        if (firstPoint.Equals(lastPoint) && w.paintOp.FillOrStroke == "fill")
                            image.Mutate(x => x.Fill(dOpts, paint, new SixLabors.ImageSharp.Drawing.Polygon(new LinearLineSegment(line))));
                        else
                            image.Mutate(x => x.DrawLines(dOpts, pen, line));
                        break;
                    case "MultiLineString":
                        foreach (var p3 in ((MultiLineString)thisGeometry).Geometries) {
                            var line2 = LineToDrawingLine(p3, stats);
                            image.Mutate(x => x.DrawLines(dOpts, pen, line2));
                        }
                        break;
                    case "Point":
                        var convertedPoint = PointToPointF(thisGeometry, stats.area, stats.degreesPerPixelX, stats.degreesPerPixelY);
                        //If this type has an icon, use it. Otherwise draw a circle in that type's color.
                        if (!string.IsNullOrEmpty(w.paintOp.FileName)) {
                            Image i2 = cachedBitmaps[w.paintOp.FileName];
                            image.Mutate(x => x.DrawImage(i2, (SixLabors.ImageSharp.Point)convertedPoint, 1));
                        }
                        else {
                            var circleRadius = (float)(w.paintOp.LineWidthDegrees / stats.pixelsPerDegreeX); //was w.lineWidthPixels, but I think i want this to scale.
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

            image.Mutate(x => x.Flip(FlipMode.Vertical)); //Plus codes are south-to-north, so invert the image to make it correct.
            //image.Mutate(x => x.BoxBlur(1)); //This does help smooth out some of the rough edges on the game tiles, but it might need a bigger radius? 
            var ms = new MemoryStream();
            image.SaveAsPng(ms);
            return ms.ToArray();
        }

        /// <summary>
        /// ImageSharp doesn't support this at all. Throws NotImplementedException when called.
        /// </summary>
        public string DrawAreaAtSizeSVG(ImageStats stats, List<DbTables.Place> drawnItems = null, Dictionary<string, StyleEntry> styles = null, bool filterSmallAreas = true) {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Draws 1 tile overtop the other.
        /// </summary>
        /// <param name="info">Unused in this implementation, but requires per the interface.</param>
        /// <param name="bottomTile">the tile to use as the base of the image. Expected to be opaque.</param>
        /// <param name="topTile">The tile to layer on top. Expected to be at least partly transparent or translucent.</param>
        /// <returns></returns>
        public byte[] LayerTiles(ImageStats info, byte[] bottomTile, byte[] topTile) {
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
        public static Rgba32 GetStyleBgColorString(string styleSet) {
            var color = Rgba32.ParseHex(TagParser.allStyleGroups[styleSet]["background"].PaintOperations.First().HtmlColorCode);
            return color;
        }

        public static IPath PolygonToDrawingPolygon(Geometry place, GeoArea drawingArea, double resolutionX, double resolutionY) {
            var lineSegmentList = new List<LinearLineSegment>();
            NetTopologySuite.Geometries.Polygon p = (NetTopologySuite.Geometries.Polygon)place;
            var typeConvertedPoints = p.ExteriorRing.Coordinates.Select(o => new SixLabors.ImageSharp.PointF((float)((o.X - drawingArea.WestLongitude) * (1 / resolutionX)), (float)((o.Y - drawingArea.SouthLatitude) * (1 / resolutionY))));
            var path = new SixLabors.ImageSharp.Drawing.Path(new LinearLineSegment(typeConvertedPoints.ToArray())).AsClosedPath();

            foreach (var hole in p.InteriorRings) {
                typeConvertedPoints = hole.Coordinates.Select(o => new SixLabors.ImageSharp.PointF((float)((o.X - drawingArea.WestLongitude) * (1 / resolutionX)), (float)((o.Y - drawingArea.SouthLatitude) * (1 / resolutionY))));
                var tempHole = new SixLabors.ImageSharp.Drawing.Path(new LinearLineSegment(typeConvertedPoints.ToArray())).AsClosedPath();
                path = path.Clip(tempHole);
            }
            return path;
        }

        public static LinearLineSegment PolygonToDrawingLine(Geometry place, GeoArea drawingArea, double resolutionX, double resolutionY) {
            //NOTE: this doesn't handle holes if you add them to the end in the reverse order. Those must be handled by a function in ImageSharp.
            var typeConvertedPoints = place.Coordinates.Select(o => new SixLabors.ImageSharp.PointF((float)((o.X - drawingArea.WestLongitude) * (1 / resolutionX)), (float)((o.Y - drawingArea.SouthLatitude) * (1 / resolutionY))));
            LinearLineSegment part = new LinearLineSegment(typeConvertedPoints.ToArray());
            var x = new SixLabors.ImageSharp.Drawing.Path();
            return part;
        }

        public static SixLabors.ImageSharp.PointF[] LineToDrawingLine(Geometry place, ImageStats stats) // GeoArea drawingArea, double resolutionX, double resolutionY)
        {
            var typeConvertedPoints = place.Coordinates.Select(o => new SixLabors.ImageSharp.PointF((float)((o.X - stats.area.WestLongitude) * (1 / stats.degreesPerPixelX)), (float)((o.Y - stats.area.SouthLatitude) * (1 / stats.degreesPerPixelY)))).ToList();
            return typeConvertedPoints.ToArray();
        }

        public static SixLabors.ImageSharp.PointF PointToPointF(Geometry place, GeoArea drawingArea, double resolutionX, double resolutionY) {
            var coord = place.Coordinate;
            return new SixLabors.ImageSharp.PointF((float)((coord.X - drawingArea.WestLongitude) * (1 / resolutionX)), (float)((coord.Y - drawingArea.SouthLatitude) * (1 / resolutionY)));
        }

        public static Rectangle PlaceInfoToRect(StandaloneDbTables.PlaceInfo2 pi, ImageStats info) {
            Rectangle r = new Rectangle();
            r.Width = (int)(pi.width * info.pixelsPerDegreeX);
            r.Height = (int)(pi.height * info.pixelsPerDegreeY);
            r.X = (int)(pi.lonCenter * info.pixelsPerDegreeX);
            r.Y = (int)(pi.latCenter * info.pixelsPerDegreeY);

            return r;
        }
    }
}
