﻿using Google.OpenLocationCode;
using Microsoft.EntityFrameworkCore;
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
    public class MapTiles : IMapTiles {
        static readonly SKPaint eraser = new SKPaint() { Color = SKColors.Transparent, BlendMode = SKBlendMode.Src, Style = SKPaintStyle.StrokeAndFill }; //BlendMode is the important part for an Eraser.
        static readonly SKPaint text = new SKPaint() { Color = SKColors.Black, Style = SKPaintStyle.StrokeAndFill };
        static readonly Random r = new Random();
        static Dictionary<string, SKBitmap> cachedBitmaps = new Dictionary<string, SKBitmap>(); //Icons for points separate from pattern fills, though I suspect if I made a pattern fill with the same size as the icon I wouldn't need this.
        static Dictionary<long, SKPaint> cachedPaints = new Dictionary<long, SKPaint>();
        static SKPaint outlinePaint;


        public void Initialize() {
            cachedBitmaps.Clear();
            cachedPaints.Clear();

            foreach (var b in TagParser.cachedBitmaps)
                try
                {
                    cachedBitmaps.Add(b.Key, SKBitmap.Decode(b.Value));
                }
                catch (Exception ex)
                {
                    Log.WriteLog("Failed to cache " + b.Key + ": " + ex.Message);
                }

            int maxId = 1;
            foreach (var g in TagParser.allStyleGroups)
                foreach (var s in g.Value)
                    foreach (var p in s.Value.PaintOperations) {
                        if (p.Id == 0) {
                            p.Id = maxId++;
                        }
                        cachedPaints.Add(p.Id, SetPaintForTPP(p));
                    }

            outlinePaint = SetPaintForTPP(Styles.outlines.style[0].PaintOperations.First());
        }

        /// <summary>
        /// Create the SKPaint object for each style and store it in the requested object.
        /// </summary>
        /// <param name="tpe">the TagParserPaint object to populate</param>
        private static SKPaint SetPaintForTPP(StylePaint tpe) {
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
            if (tpe.LinePattern != null && tpe.LinePattern != "solid") { //TODO: fix styles with null linePattern
                float[] linesAndGaps = tpe.LinePattern.Split('|').Select(t => float.Parse(t)).ToArray();
                paint.PathEffect = SKPathEffect.CreateDash(linesAndGaps, 0);
                paint.StrokeCap = SKStrokeCap.Butt;
            }
            if (!string.IsNullOrEmpty(tpe.FileName)) {
                try
                {
                    SKBitmap fillPattern = cachedBitmaps[tpe.FileName];
                    //cachedBitmaps.TryAdd(tpe.fileName, fillPattern); //For icons.
                    SKShader tiling = SKShader.CreateBitmap(fillPattern, SKShaderTileMode.Repeat, SKShaderTileMode.Repeat); //For fill patterns.
                    paint.Shader = tiling;
                }
                catch (Exception ex)
                {
                    Log.WriteLog("Error creating paint " + tpe.Id + ": " + ex.Message);
                }
            }
            return paint;
        }

        /// <summary>
        /// Draws grid lines to match boundaries for 8 character PlusCodes.
        /// </summary>
        /// <param name="totalArea">the GeoArea to draw lines in</param>
        /// <returns>the byte array for the maptile png file</returns>
        public static byte[] DrawCell8GridLines(GeoArea totalArea) {
            int imageSizeX = MapTileSupport.SlippyTileSizeSquare;
            int imageSizeY = MapTileSupport.SlippyTileSizeSquare;
            SKBitmap bitmap = new SKBitmap(imageSizeX, imageSizeY, SKColorType.Rgba8888, SKAlphaType.Premul);
            SKCanvas canvas = new SKCanvas(bitmap);
            SKColor bgColor = SKColor.Parse("00000000");
            canvas.Clear(bgColor);
            canvas.Scale(1, -1, imageSizeX / 2, imageSizeY / 2);
            SKPaint paint = new SKPaint();
            SKColor color = SKColor.Parse("#FF0000");
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
        public static byte[] DrawCell10GridLines(GeoArea totalArea) {
            int imageSizeX = MapTileSupport.SlippyTileSizeSquare;
            int imageSizeY = MapTileSupport.SlippyTileSizeSquare;
            SKBitmap bitmap = new SKBitmap(imageSizeX, imageSizeY, SKColorType.Rgba8888, SKAlphaType.Premul);
            SKCanvas canvas = new SKCanvas(bitmap);
            SKColor bgColor = SKColor.Parse("00000000");
            canvas.Clear(bgColor);
            canvas.Scale(1, -1, imageSizeX / 2, imageSizeY / 2);
            SKPaint paint = new SKPaint();
            SKColor color = SKColor.Parse("#00CCFF");
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
        public byte[] DrawAreaAtSize(ImageStats stats, List<DbTables.Place> drawnItems = null, string styleSet = "mapTiles", string skipType = null) {
            //This is the new core drawing function. Takes in an area, the items to draw, and the size of the image to draw. 
            //The drawn items get their paint pulled from the TagParser's list. If I need multiple match lists, I'll need to make a way
            //to pick which list of tagparser rules to use.
            //This can work for user data by using the linked Places from the items in PlaceGameData.
            //I need a slightly different function for using AreaGameData, or another optional parameter here

            if (drawnItems == null)
                drawnItems = GetPlaces(stats.area, filterSize: stats.filterSize, skipTags:true, dataKey:styleSet, skipType: skipType);

            var paintOps = MapTileSupport.GetPaintOpsForPlaces(drawnItems, styleSet, stats);
            return DrawAreaAtSize(stats, paintOps);
        }

            public byte[] DrawAreaAtSize(ImageStats stats, List<CompletePaintOp> paintOps) {
            //This is the new core drawing function. Once the paint operations have been created, I just draw them here.
            //baseline image data stuff           
            SKBitmap bitmap = new SKBitmap(stats.imageSizeX, stats.imageSizeY, SKColorType.Rgba8888, SKAlphaType.Premul);
            SKCanvas canvas = new SKCanvas(bitmap);
            canvas.Clear(eraser.Color);
            canvas.Scale(1, -1, stats.imageSizeX / 2, stats.imageSizeY / 2);
            SKPaint paint = new SKPaint();
            SKPath path = new SKPath();
            SKColor originalColor = new SKColor();
            foreach (var w in paintOps.OrderByDescending(p => p.paintOp.LayerId).ThenByDescending(p => p.drawSizeHint)) {
                paint = cachedPaints[w.paintOp.Id];

                if (w.paintOp.FromTag) //FromTag is for when you are saving color data directly to each element, instead of tying it to a styleset.
                    paint.Color = SKColor.Parse(w.tagValue);

                if (w.paintOp.Randomize) //To randomize the color on every Draw call.
                    paint.Color = new SKColor((byte)r.Next(0, 255), (byte)r.Next(0, 255), (byte)r.Next(0, 255), 99);

                if (w.OverrideColor) //save original color.
                {
                    originalColor = paint.Color;
                    paint.Color = SKColor.Parse(w.tagValue);
                 }

                paint.StrokeWidth = (float)w.lineWidthPixels;
                path.Reset();
                switch (w.elementGeometry.GeometryType) {
                    case "Polygon":
                        var castPoly = w.elementGeometry as Polygon;
                        path.AddPoly(PolygonToSKPoints((castPoly).ExteriorRing, stats.area, stats.degreesPerPixelX, stats.degreesPerPixelY));
                        foreach (var ir in castPoly.Holes) {
                            path.AddPoly(PolygonToSKPoints(ir, stats.area, stats.degreesPerPixelX, stats.degreesPerPixelY));
                        }
                        canvas.DrawPath(path, paint);
                        break;
                    case "MultiPolygon":
                        foreach (Polygon p2 in ((MultiPolygon)w.elementGeometry).Geometries)
                        {
                            path.AddPoly(PolygonToSKPoints(p2.ExteriorRing, stats.area, stats.degreesPerPixelX, stats.degreesPerPixelY));
                            foreach (var ir in p2.Holes)
                            {
                                path.AddPoly(PolygonToSKPoints(ir, stats.area, stats.degreesPerPixelX, stats.degreesPerPixelY));
                            }
                        }
                        canvas.DrawPath(path, paint); //moving this might be super important!
                        break;
                    case "LineString":
                        var firstPoint = w.elementGeometry.Coordinates.First();
                        var lastPoint = w.elementGeometry.Coordinates.Last();
                        var points = PolygonToSKPoints(w.elementGeometry, stats.area, stats.degreesPerPixelX, stats.degreesPerPixelY);
                        if (firstPoint.Equals(lastPoint)) {
                            //This is a closed shape. Check to see if it's supposed to be filled in.
                            if (paint.Style == SKPaintStyle.Fill) {
                                path.AddPoly(points);
                                canvas.DrawPath(path, paint);
                            }
                        }
                        else
                            for (var line = 0; line < points.Length - 1; line++)
                                canvas.DrawLine(points[line], points[line + 1], paint);
                        break;
                    case "MultiLineString":
                        foreach (var p3 in ((MultiLineString)w.elementGeometry).Geometries) {
                            var points2 = PolygonToSKPoints(p3, stats.area, stats.degreesPerPixelX, stats.degreesPerPixelY);
                            for (var line = 0; line < points2.Length - 1; line++)
                                canvas.DrawLine(points2[line], points2[line + 1], paint);
                        }
                        break;
                    case "Point":
                        var convertedPoint = PolygonToSKPoints(w.elementGeometry, stats.area, stats.degreesPerPixelX, stats.degreesPerPixelY);
                        //If this type has an icon, use it. Otherwise draw a circle in that type's color.
                        if (!string.IsNullOrEmpty(w.paintOp.FileName)) {
                            SKBitmap icon = cachedBitmaps[w.paintOp.FileName];
                            canvas.DrawBitmap(icon, convertedPoint[0]); //draws icon at fixed size (30x30px for most existing items)
                            //If i want to scale this, i should use the icon's Width, (which draws at 1:1 scale for gameplay tiles (drawsizehint = 1)
                            //so scale the image by (1/DrawSizeHint)? Remember to use server scale value.
                        }
                        else {
                            var circleRadius = (float)(w.paintOp.LineWidthDegrees / stats.degreesPerPixelX); //I want points to be drawn as 1 Cell10 in diameter usually, but should be adjustable.
                            canvas.DrawCircle(convertedPoint[0], circleRadius, paint);
                            canvas.DrawCircle(convertedPoint[0], circleRadius + (paint.StrokeWidth * 0.5f), outlinePaint); 
                        }
                        //SVG code.
                        //if (w.paintOp.FileName.EndsWith("svg"))
                        //{
                        //    var svg = new SkiaSharp.SKSvg(new SKSize(32, 32)); //TODO: work out scale factor or leave unscaled?  Also Why isnt this found? its in the core!
                        //    svg.load(w.paintOp.FileName);

                        //    canvas.DrawPicture(svg, convertedPoint);

                        //}
                        break;
                    case "MultiPoint":
                        //This is text, draw it at the first point in the item.
                        var textPoint = PolygonToSKPoints(w.elementGeometry, stats.area, stats.degreesPerPixelX, stats.degreesPerPixelY);
                        var textPoint2 = new SKPoint(textPoint[0].X, stats.imageSizeY - textPoint[0].Y);
                        //Text is also upside down.
                        canvas.Scale(1, -1, stats.imageSizeX / 2, stats.imageSizeY / 2);
                        canvas.DrawText(w.tagValue, textPoint2, text);
                        canvas.Scale(1, -1, stats.imageSizeX / 2, stats.imageSizeY / 2);
                        break;
                    default:
                        Log.WriteLog("Unknown geometry type found, not drawn.");
                        break;
                }
                canvas.Flush();//TODO test perf with and without this. Hopefully this reduces some RAM use
                if (w.OverrideColor) //restore original color.
                    paint.Color = originalColor;
            }

            var ms = new MemoryStream();
            var skms = new SKManagedWStream(ms);
            bitmap.Encode(skms, SKEncodedImageFormat.Png, 100);
            var results = ms.ToArray();
            path.Dispose();
            skms.Dispose(); ms.Close(); ms.Dispose(); canvas.Discard(); canvas.Dispose(); bitmap.Reset(); bitmap.Dispose(); canvas = null; bitmap = null;
            GC.Collect();
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
        public string DrawAreaAtSizeSVG(ImageStats stats, List<DbTables.Place> drawnItems = null, Dictionary<string, StyleEntry> styles = null, bool filterSmallAreas = true) {
            //This is the new core drawing function. Takes in an area, the items to draw, and the size of the image to draw. 
            //The drawn items get their paint pulled from the TagParser's list. If I need multiple match lists, I'll need to make a way
            //to pick which list of tagparser rules to use.

            if (styles == null)
                styles = TagParser.allStyleGroups["mapTiles"];

            double minimumSize = 0;
            if (filterSmallAreas)
                minimumSize = stats.degreesPerPixelX; //don't draw elements under 1 pixel in size. at slippy zoom 12, this is approx. 1 pixel for a Cell10.

            using var db = new PraxisContext();
            db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            var geo = stats.area.ToPolygon();
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
            var pass1 = drawnItems.Select(d => new { d.DrawSizeHint, d.ElementGeometry, paintOp = styles[d.StyleName].PaintOperations });
            var pass2 = new List<CompletePaintOp>(drawnItems.Count);
            foreach (var op in pass1)
                foreach (var po in op.paintOp)
                    pass2.Add(new CompletePaintOp(op.ElementGeometry, op.DrawSizeHint, po, "", po.LineWidthDegrees * stats.pixelsPerDegreeX, false));


            foreach (var w in pass2.OrderByDescending(p => p.paintOp.LayerId).ThenByDescending(p => p.drawSizeHint)) {
                paint = cachedPaints[w.paintOp.Id];
                if (paint.Color.Alpha == 0)
                    continue; //This area is transparent, skip drawing it entirely.

                if (stats.degreesPerPixelX > w.paintOp.MaxDrawRes || stats.degreesPerPixelX < w.paintOp.MinDrawRes)
                    continue; //This area isn't drawn at this scale.

                var path = new SKPath();
                switch (w.elementGeometry.GeometryType) {
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
                        foreach (var p2 in ((MultiPolygon)w.elementGeometry).Geometries) {
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
                        if (firstPoint.Equals(lastPoint)) {
                            //This is a closed shape. Check to see if it's supposed to be filled in.
                            if (paint.Style == SKPaintStyle.Fill) {
                                path.AddPoly(points);
                                canvas.DrawPath(path, paint);
                                continue;
                            }
                        }
                        for (var line = 0; line < points.Length - 1; line++)
                            canvas.DrawLine(points[line], points[line + 1], paint);
                        break;
                    case "MultiLineString":
                        foreach (var p3 in ((MultiLineString)w.elementGeometry).Geometries) {
                            var points2 = PolygonToSKPoints(p3, stats.area, stats.degreesPerPixelX, stats.degreesPerPixelY);
                            for (var line = 0; line < points2.Length - 1; line++)
                                canvas.DrawLine(points2[line], points2[line + 1], paint);
                        }
                        break;
                    case "Point":
                        var convertedPoint = PolygonToSKPoints(w.elementGeometry, stats.area, stats.degreesPerPixelX, stats.degreesPerPixelY);
                        //If this type has an icon, use it. Otherwise draw a circle in that type's color.
                        if (!string.IsNullOrEmpty(w.paintOp.FileName)) {
                            SKBitmap icon = cachedBitmaps[w.paintOp.FileName];
                            canvas.DrawBitmap(icon, convertedPoint[0]);
                        }
                        else {
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
        public byte[] LayerTiles(ImageStats info, byte[] bottomTile, byte[] topTile) {
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
        public static SKColor GetStyleBgColor(string styleSet) {
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
        public static SkiaSharp.SKPoint[] PolygonToSKPoints(Geometry place, GeoArea drawingArea, double degreesPerPixelX, double degreesPerPixelY) {
            SkiaSharp.SKPoint[] points = place.Coordinates.Select(o => new SkiaSharp.SKPoint((float)((o.X - drawingArea.WestLongitude) * (1 / degreesPerPixelX)), (float)((o.Y - drawingArea.SouthLatitude) * (1 / degreesPerPixelY)))).ToArray();
            return points;
        }
    }
}