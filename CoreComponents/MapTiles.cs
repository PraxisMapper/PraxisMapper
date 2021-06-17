using Google.OpenLocationCode;
using NetTopologySuite.Geometries;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using static CoreComponents.ConstantValues;
using static CoreComponents.DbTables;
using static CoreComponents.Place;
using static CoreComponents.Singletons;
using SkiaSharp;
using Microsoft.EntityFrameworkCore;
using CoreComponents.Support;

namespace CoreComponents
{
    public static class MapTiles
    {
        public static int MapTileSizeSquare = 512; //Default value, updated by PraxisMapper at startup.
        static SKPaint eraser = new SKPaint() { Color = SKColors.Transparent, BlendMode = SKBlendMode.Src, Style = SKPaintStyle.StrokeAndFill }; //BlendMode is the important part for an Eraser.

        public static byte[] DrawMPAreaControlMapTile(ImageStats info, List<StoredOsmElement> places = null)
        {
            bool drawEverything = false; //for debugging/testing
            //var smallestFeature = (drawEverything ? 0 : degreesPerPixelX < degreesPerPixelY ? degreesPerPixelX : degreesPerPixelY);
            var smallestFeature = 0;

            //To make sure we don't get any seams on our maptiles (or points that don't show a full circle, we add a little extra area to the image before drawing, then crop it out at the end.
            //Do this after determining image size, since Skia will ignore parts off-canvas.
            var loadDataArea = new GeoArea(new GeoPoint(info.area.Min.Latitude - resolutionCell10, info.area.Min.Longitude - resolutionCell10), new GeoPoint(info.area.Max.Latitude + resolutionCell10, info.area.Max.Longitude + resolutionCell10));

            var db = new PraxisContext();
            if (places == null)
                places = GetPlaces(loadDataArea, skipTags: true); //, null, false, true, smallestFeature //Includes generated here with the final True parameter.
            List<long> placeIDs = places.Select(a => a.sourceItemID).ToList();
            Dictionary<long, long> teamClaims = db.AreaControlTeams.Where(act => placeIDs.Contains(act.StoredElementId)).ToDictionary(k => k.StoredElementId, v => v.FactionId);
            Dictionary<long, string> teamNames = db.Factions.ToDictionary(k => k.FactionId, v => v.Name);
            places = places
                .Where(a => teamClaims.ContainsKey(a.sourceItemID)) //Only draw elements claimed by a team.
                .Where(a => a.AreaSize >= smallestFeature) //only draw elements big enough to draw
                .OrderByDescending(a => a.AreaSize) //Order from biggest to smallest.
                .ToList();

            foreach (var ap in places) //Set team ownership via tags.
            {
                if (teamClaims.ContainsKey(ap.sourceItemID))
                    ap.Tags = new List<ElementTags>() { new ElementTags() { Key = "team", Value = teamNames[teamClaims[ap.sourceItemID]] } };
                else
                    ap.Tags = new List<ElementTags>() { new ElementTags() { Key = "team", Value = "none" } };
            }

            return DrawAreaAtSizeV4(info, places, TagParser.teams);
        }

        public static byte[] DrawPaintTownSlippyTileSkia(ImageStats info, int instanceID)
        {
            //It might be fun on rare occasion to try and draw this all at once, but zoomed out too far and we won't see anything and will be very slow.
            //Find all Cell8s in the relevant area.
            MemoryStream ms = new MemoryStream();
            var Cell8Wide = info.area.LongitudeWidth / resolutionCell8;
            var Cell8High = info.area.LatitudeHeight / resolutionCell8;

            //These may or may not be the same, even if the map tile is smaller than 1 Cell8.
            var firstCell8 = new OpenLocationCode(info.area.SouthLatitude, info.area.WestLongitude).CodeDigits.Substring(0, 8);
            var lastCell8 = new OpenLocationCode(info.area.NorthLatitude,info.area.EastLongitude).CodeDigits.Substring(0, 8);
            if (firstCell8 != lastCell8)
            {
                //quick hack to make sure we process enough data.
                Cell8High++;
                Cell8Wide++;
            }

            List<PaintTownEntry> allData = new List<PaintTownEntry>();
            for (var x = 0; x < Cell8Wide; x++)
                for (var y = 0; y < Cell8High; y++)
                {
                    var thisCell = new OpenLocationCode(info.area.SouthLatitude + (resolutionCell8 * x), info.area.WestLongitude + (resolutionCell8 * y)).CodeDigits.Substring(0, 8);
                    var thisData = PaintTown.LearnCell8(instanceID, thisCell);
                    allData.AddRange(thisData);
                }

            //Some image items setup.
            SKBitmap bitmap = new SKBitmap(info.imageSizeX, info.imageSizeY, SKColorType.Rgba8888, SKAlphaType.Premul);
            SKCanvas canvas = new SKCanvas(bitmap);
            var bgColor = SKColors.Transparent;
            canvas.Clear(bgColor);
            canvas.Scale(1, -1, info.imageSizeX / 2, info.imageSizeY / 2);
            SKPaint paint = new SKPaint();
            SKColor color = new SKColor();
            paint.IsAntialias = true;
            foreach (var line in allData)
            {
                var location = OpenLocationCode.DecodeValid(line.Cell10);
                var placeAsPoly = Converters.GeoAreaToPolygon(location);
                var path = new SKPath();
                path.AddPoly(Converters.PolygonToSKPoints(placeAsPoly, info.area, info.degreesPerPixelX, info.degreesPerPixelY));
                paint.Style = SKPaintStyle.Fill;
                SKColor.TryParse(teamColorReferenceLookupSkia[line.FactionId].FirstOrDefault(), out color);
                paint.Color = color;
                canvas.DrawPath(path, paint);
            }

            var skms = new SKManagedWStream(ms);
            bitmap.Encode(skms, SKEncodedImageFormat.Png, 100);
            var results = ms.ToArray();
            skms.Dispose(); ms.Close(); ms.Dispose();
            return results;
        }

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
            items = TagParser.ApplyTags(items);

            //var placeInfo = CoreComponents.Standalone.Standalone.GetPlaceInfo(items.Where(i =>
            //i.GameElementName != "trail" &&
            //i.GameElementName != "road" &&
            //i.GameElementName != "default" &&
            //i.GameElementName != "background"
            //).ToList());

            var placeInfo = CoreComponents.Standalone.Standalone.GetPlaceInfo(items.Where(i =>
            i.GameElementName == "admin"
            ).ToList());

            //this is for rectangles.
            foreach (var pi in placeInfo)
            {
                var rect = Converters.PlaceInfoToRect(pi, info);
                fillpaint.Color = CoreComponents.Misc.PickStaticColorForArea(pi.Name);
                canvas.DrawRect(rect, fillpaint);
                canvas.DrawRect(rect, strokePaint);
            }

            canvas.Scale(1, -1, info.imageSizeX / 2, info.imageSizeY / 2); //inverts the inverted image again!
            foreach (var pi in placeInfo)
            {
                var rect = Converters.PlaceInfoToRect(pi, info);
                canvas.DrawText(pi.Name, rect.MidX, info.imageSizeY - rect.MidY, strokePaint);
            }


            //this was for circles
            //canvas.Scale(1, -1, info.imageSizeX / 2, info.imageSizeY / 2);
            //foreach (var pi in placeInfo)
            //{
            //    fillpaint.Color = CoreComponents.Misc.PickStaticColorForArea(pi.Name);
            //    var imgpoint = Converters.PlaceInfoToSKPoint(pi, info);
            //    canvas.DrawCircle(imgpoint, (float)(pi.radius / info.degreesPerPixelX), fillpaint);
            //    canvas.DrawCircle(imgpoint, (float)(pi.radius / info.degreesPerPixelX), strokePaint);
            //    canvas.DrawText(pi.Name, imgpoint, strokePaint); //Unscale this so its not upside down.
            //}

            //canvas.Scale(1, -1, info.imageSizeX / 2, info.imageSizeY / 2); //inverts the inverted image again!
            //foreach (var pi in placeInfo)
            //{
            //    var imgpoint = Converters.PlaceInfoToSKPoint(pi, info);
            //    canvas.DrawText(pi.Name, imgpoint, strokePaint);
            //}

            var ms = new MemoryStream();
            var skms = new SkiaSharp.SKManagedWStream(ms);
            bitmap.Encode(skms, SkiaSharp.SKEncodedImageFormat.Png, 100);
            var results = ms.ToArray();
            skms.Dispose(); ms.Close(); ms.Dispose();
            return results;
        }

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

        //public static byte[] DrawAdminBoundsMapTileSlippy(ref List<StoredOsmElement> allPlaces, ImageStats info)
        //{
        //    //The correct replacement for this is to just do the normal draw, but only feed in admin bound areas.
        //    return null; 
        //    //return DrawAdminBoundsMapTileSlippy(ref allPlaces, info.area, info.area.LatitudeHeight, info.area.LongitudeWidth, false);
        //}

        public static void ExpireMapTiles(Geometry g, int limitModeTo = 0)
        {
            //If this would be faster as raw SQL, see function below for a template on how to write that.
            var db = new PraxisContext();
            var mapTiles = db.MapTiles.Where(m => m.areaCovered.Intersects(g) && (limitModeTo == 0 || m.mode == limitModeTo)).ToList(); //TODO: can I select only the ExpiresOn value and have that save back correctly?
            foreach (var mt in mapTiles)
                mt.ExpireOn = DateTime.Now;

            db.SaveChanges();
        }

        public static void ExpireSlippyMapTiles(Geometry g, int limitModeTo = 0)
        {
            //Might this be better off as raw SQL? If I expire, say, an entire state, that could be a lot of map tiles to pull into RAM just for a date to change.
            //var raw = "UPDATE SlippyMapTiles SET ExpireOn = CURRENT_TIMESTAMP WHERE ST_INTERSECTS(areaCovered, ST_GeomFromText(" + g.AsText() + "))";
            var db = new PraxisContext();
            var mapTiles = db.SlippyMapTiles.Where(m => m.areaCovered.Intersects(g) && (limitModeTo == 0 || m.mode == limitModeTo)).ToList(); //TODO: can I select only the ExpiresOn value and have that save back correctly?
            foreach (var mt in mapTiles)
                mt.ExpireOn = DateTime.Now;

            db.SaveChanges();
        }

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
            var baseImage = DrawAreaAtSizeV4(info, places); //InnerDrawSkia(ref places, mapToDraw, degreesPerPixelX, degreesPerPixelY, 1024, 1024);

            SKBitmap sKBitmap = SKBitmap.Decode(baseImage);
            SKCanvas canvas = new SKCanvas(sKBitmap);
            SKPaint paint = new SKPaint();
            paint.Style = SKPaintStyle.Stroke;
            paint.StrokeWidth = 4; //Larger than normal lines at any zoom level.
            paint.Color = new SKColor(255, 255, 255); //Pure White, for maximum visibility.
            for (var x = 0; x < drawableLine.Length - 1; x++)
                canvas.DrawLine(drawableLine[x], drawableLine[x + 1], paint);

            var ms = new MemoryStream();
            var skms = new SKManagedWStream(ms);
            sKBitmap.Encode(skms, SKEncodedImageFormat.Png, 100);
            var results = ms.ToArray();
            skms.Dispose(); ms.Close(); ms.Dispose();
            return results;
        }

        public static byte[] DrawCell8V4(GeoArea Cell8, List<StoredOsmElement> drawnItems = null)
        {
            return DrawAreaAtSizeV4(Cell8, 80, 100,  drawnItems);
        }

        public static byte[] DrawPlusCode(string area)
        {
            //This might be a cleaner version of my V4 function, for working with CellX sized tiles..
            //This will draw at a Cell11 resolution automatically.
            //Split it into a few functions.
            //then get all the area

            int imgX = 0, imgY = 0;
            switch (area.Length) //Didn't I have a function for this already?
            {
                case 10:
                    imgX = 4;
                    imgY = 5;
                    break;
                case 8:
                    imgX = 4 * 20;
                    imgY = 5 * 20;
                    break;
                case 6:
                    imgX = 4 * 20 * 20;
                    imgY = 5 * 20 * 20;
                    break;
                case 4:
                    imgX = 4 * 20 * 20 * 20;
                    imgY = 5 * 20 * 20 * 20;
                    break;
                default:
                    imgX = 0;
                    imgY = 0;
                    break;
            }

            ImageStats info = new ImageStats(OpenLocationCode.DecodeValid(area), imgX, imgY);
            return DrawAreaAtSizeV4(info, null, null, (area.Length <= 6));
        }

        public static byte[] DrawAreaAtSizeV4(GeoArea relevantArea, int imageSizeX, int imageSizeY, List<StoredOsmElement> drawnItems = null, List<TagParserEntry> styles = null)
        {
            //Create an Info object and use that to pass to to the main image.
            ImageStats info = new ImageStats(relevantArea, imageSizeX, imageSizeY);
            return DrawAreaAtSizeV4(info, drawnItems, styles);
        }

        //This generic function takes the area to draw, a size to make the canvas, and then draws it all.
        //Optional parameter allows you to pass in different stuff that the DB alone has, possibly for manual or one-off changes to styling
        //or other elements converted for maptile purposes.
        
        public static byte[] DrawAreaAtSizeV4(ImageStats stats, List<StoredOsmElement> drawnItems = null, List<TagParserEntry> styles = null, bool filterSmallAreas = true)
    {
            //This is the new core drawing function. Takes in an area, the items to draw, and the size of the image to draw. 
            //The drawn items get their paint pulled from the TagParser's list. If I need multiple match lists, I'll need to make a way
            //to pick which list of tagparser rules to use.

            if (styles == null)
                styles = TagParser.styles;

            double minimumSize = 0;
            if (filterSmallAreas)
                minimumSize = stats.degreesPerPixelX; //don't draw elements under 1 pixel in size. at slippy zoom 12, this is approx. 1 pixel for a Cell10.
          
            var db = new PraxisContext();
            var geo = Converters.GeoAreaToPolygon(stats.area);
            if (drawnItems == null)
                drawnItems = GetPlaces(stats.area, minimumSize: minimumSize);
                //drawnItems = db.StoredOsmElements.Include(c => c.Tags).Where(w => geo.Intersects(w.elementGeometry) && w.AreaSize >= minimumSize).OrderByDescending(w => w.elementGeometry.Area).ThenByDescending(w => w.elementGeometry.Length).ToList();

            //baseline image data stuff           
            SKBitmap bitmap = new SKBitmap(stats.imageSizeX, stats.imageSizeY, SKColorType.Rgba8888, SKAlphaType.Premul);
            SKCanvas canvas = new SKCanvas(bitmap);
            var bgColor = styles.Where(s => s.name == "background").FirstOrDefault().paint; //Backgound is a named style, unmatched will be the last entry and transparent.
            canvas.Clear(bgColor.Color);
            canvas.Scale(1, -1, stats.imageSizeX / 2, stats.imageSizeY / 2);
            SKPaint paint = new SKPaint();

            foreach (var w in drawnItems)
            {
                //NOTE: I now populate the necessary data in GetPlaces, so I can skip this now-redundant logic

                //var tempList = new List<ElementTags>();
                //if (w.Tags != null)
                //    tempList = w.Tags.ToList();
                //var style = CoreComponents.TagParser.GetStyleForOsmWay(w);
                var style = TagParser.styles.Where(s => s.name == w.GameElementName).First();
                paint = style.paint;
                if (paint.Color.Alpha == 0)
                    continue; //This area is transparent, skip drawing it entirely.

                var path = new SKPath();
                switch (w.elementGeometry.GeometryType)
                {
                    //Polygons without holes are super easy and fast: draw the path.
                    //Polygons with holes require their own bitmap to be drawn correctly and then overlaid onto the canvas.
                    //I want to use paths to fix things for performance reasons, but I have to use Bitmaps because paths apply their blend mode to
                    //ALL elements already drawn, not just the last one.
                    case "Polygon":
                        var p = w.elementGeometry as Polygon;
                        if (p.Holes.Length == 0)
                        {
                            path.AddPoly(Converters.PolygonToSKPoints(p, stats.area, stats.degreesPerPixelX, stats.degreesPerPixelY));
                            canvas.DrawPath(path, paint);
                        }
                        else
                        {
                            var innerBitmap = DrawPolygon((Polygon)w.elementGeometry, paint, stats);
                            canvas.DrawBitmap(innerBitmap, 0, 0, paint);
                        }
                        break;
                    case "MultiPolygon":
                        foreach (var p2 in ((MultiPolygon)w.elementGeometry).Geometries)
                        {
                            var p2p = p2 as Polygon;
                            if (p2p.Holes.Length == 0)
                            {
                                path.AddPoly(Converters.PolygonToSKPoints(p2p, stats.area, stats.degreesPerPixelX, stats.degreesPerPixelY));
                                canvas.DrawPath(path, paint);
                            }
                            else
                            {
                                var innerBitmap = DrawPolygon(p2p, paint, stats);
                                canvas.DrawBitmap(innerBitmap, 0, 0, paint);
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
                        var circleRadius = (float)(.000125 / stats.degreesPerPixelX / 2); //I want points to be drawn as 1 Cell10 in diameter.
                        var convertedPoint = Converters.PolygonToSKPoints(w.elementGeometry, stats.area, stats.degreesPerPixelX, stats.degreesPerPixelY);
                        canvas.DrawCircle(convertedPoint[0], circleRadius, paint);
                        break;
                    default:
                        Log.WriteLog("Unknown geometry type found, not drawn. Element " + w.id);
                        break;
                }
            }

            var ms = new MemoryStream();
            var skms = new SKManagedWStream(ms);
            bitmap.Encode(skms, SKEncodedImageFormat.Png, 100);
            var results = ms.ToArray();
            skms.Dispose(); ms.Close(); ms.Dispose();
            return results;
        }

        public static SKBitmap DrawPolygon(Polygon polygon, SKPaint paint, ImageStats stats)
        {
            //In order to do this the most correct, i have to draw the outer ring, then erase all the innner rings.
            //THEN draw that image overtop the original.
            SKBitmap bitmap = new SKBitmap(stats.imageSizeX, stats.imageSizeY, SKColorType.Rgba8888, SKAlphaType.Premul);
            SKCanvas canvas = new SKCanvas(bitmap);
            var bgColor = SKColors.Transparent;
            canvas.Clear(bgColor);
            canvas.Scale(1, 1, stats.imageSizeX / 2, stats.imageSizeY / 2); 
            var path = new SKPath();
            path.AddPoly(Converters.PolygonToSKPoints(polygon.ExteriorRing, stats.area, stats.degreesPerPixelX, stats.degreesPerPixelY));
            canvas.DrawPath(path, paint);

            foreach (var hole in polygon.InteriorRings)
            {
                path = new SKPath();
                path.AddPoly(Converters.PolygonToSKPoints(hole, stats.area, stats.degreesPerPixelX, stats.degreesPerPixelY));
                canvas.DrawPath(path, eraser);
            }

            return bitmap;
        }
    }
}

