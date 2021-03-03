using Google.OpenLocationCode;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static CoreComponents.DbTables;
using static CoreComponents.ConstantValues;
using SixLabors.ImageSharp.Drawing.Processing;
using static CoreComponents.Singletons;
using NetTopologySuite.Geometries.Prepared;
using static CoreComponents.Place;
using static CoreComponents.AreaTypeInfo;
using NetTopologySuite.Geometries;
using SixLabors.ImageSharp.Drawing;

namespace CoreComponents
{
    public static class MapTiles
    {
        public static void GetResolutionValues(int CellSize, out double resX, out double resY)
        {
            switch (CellSize)
            {
                case 2: //not real useful but lets allow it
                    resX = resolutionCell2;
                    resY = resolutionCell2;
                    break;
                case 4:
                    resX = resolutionCell4;
                    resY = resolutionCell4;
                    break;
                case 6:
                    resX = resolutionCell6;
                    resY = resolutionCell6;
                    break;
                case 8:
                    resX = resolutionCell8;
                    resY = resolutionCell8;
                    break;
                case 10:
                    resX = resolutionCell10;
                    resY = resolutionCell10;
                    break;
                case 11:
                    resX = resolutionCell11Lon;
                    resY = resolutionCell11Lat;
                    break;
                default: //Not a supported resolution
                    resX = 0;
                    resY = 0;
                    break;
            }
        }

        
        public static byte[] DrawAreaMapTileRaster(ref List<MapData> allPlaces, GeoArea totalArea, int pixelSizeCells)
        {
            //This was the original function I used to draw map tiles before I figured out the vector drawing logic.
            //A lot of optimization went into this and it's still half the speed of the vectorizer in the best case.
            //Keeping this around for reference, mostly as a performance scale measurement.

            double resolutionX, resolutionY;
            double filterSize = 0;
            GetResolutionValues(pixelSizeCells, out resolutionX, out resolutionY);
            if (pixelSizeCells < 10) // Roads and buildings are good at Cell10+. Not helpful at Cell8-;
                filterSize = resolutionX / 2; //things smaller than half a pixel will not be considered for the map tile. Might just want to toggle the alternate sort rules for pixels (most area, not smallest item)
            //Or should this filter to 'smallest area over filter size'?

            List<MapData> rowPlaces;
            //create a new bitmap.
            MemoryStream ms = new MemoryStream();
            //pixel formats. RBGA32 allows for hex codes. RGB24 doesnt?
            int imagesizeX = (int)Math.Floor(totalArea.LongitudeWidth / resolutionX);
            int imagesizeY = (int)Math.Floor(totalArea.LatitudeHeight / resolutionY);

            double[] xCoords = new double[imagesizeX + 1];
            double[] yCoords = new double[imagesizeY + 1];
            for (int i = 0; i <= imagesizeX; i++)
            {
                xCoords[i] = totalArea.Min.Longitude + (resolutionX * i);
            }
            for (int i = 0; i <= imagesizeY; i++)
            {
                yCoords[i] = totalArea.Min.Latitude + (resolutionY * i);
            }

            //crop all places to the current area. This removes a ton of work from the process by simplifying geometry to only what's relevant, instead of drawing all of a great lake or state-wide park.
            var cropArea = Converters.GeoAreaToPolygon(totalArea);
            foreach (var ap in allPlaces)
                ap.place = ap.place.Intersection(cropArea);

            //pre-cache the set of data we need per column. Storing IDs saves us a lot of Intersects checks later. Approx. 20% faster this way.
            List<long>[] columnPlaces = new List<long>[imagesizeX];
            for (int i = 0; i < imagesizeX; i++)
            {
                columnPlaces[i] = GetPlaceIDs(new GeoArea(new GeoPoint(totalArea.Min.Latitude, xCoords[i]), new GeoPoint(totalArea.Max.Latitude, xCoords[i + 1])), ref allPlaces).ToList();
            }

            using (var image = new Image<Rgba32>(imagesizeX, imagesizeY))
            {
                image.Mutate(x => x.Fill(Rgba32.ParseHex(areaColorReference[999].First()))); //set all the areas to the background color
                for (int y = 0; y < image.Height; y++)
                {
                    //Dramatic performance improvement by limiting this to just the row's area.
                    rowPlaces = GetPlaces(new GeoArea(new GeoPoint(yCoords[y], totalArea.Min.Longitude), new GeoPoint(yCoords[y + 1], totalArea.Max.Longitude)), allPlaces);
                    var preparedPlaces = rowPlaces.Select(rp => new PreparedMapData() { PreparedMapDataID = rp.MapDataId, place = pgf.Create(rp.place), AreaTypeId = rp.AreaTypeId }).ToList(); //This make the loop dramatically faster and I cannot identify why.

                    if (rowPlaces.Count() != 0) //don't bother drawing the row if there's nothing in it.
                    {
                        Span<Rgba32> pixelRow = image.GetPixelRowSpan(image.Height - y - 1); //Plus code data is searched south-to-north, image is inverted otherwise.
                        for (int x = 0; x < image.Width; x++)
                        {
                            if (columnPlaces[x].Count() != 0) //if this column has no places, don't bother checking either. This is just as fast or faster than saving a list/array of columns to check.
                            {
                                //Set the pixel's color by its type.
                                int placeData = 0;
                                //var tempPlaces = rowPlaces.Where(r => columnPlaces[x].Contains(r.MapDataId)).ToList(); //reduces Intersects() calls done in the next function
                                var tempPlaces = preparedPlaces.Where(r => columnPlaces[x].Contains(r.PreparedMapDataID)).ToList(); //reduces Intersects() calls done in GetAreaType
                                var pixelArea = new GeoArea(new GeoPoint(yCoords[y], xCoords[x]), new GeoPoint(yCoords[y + 1], xCoords[x + 1]));
                                placeData = GetAreaType(pixelArea, ref tempPlaces, true, filterSize);
                                if (placeData != 0)
                                {
                                    pixelRow[x] = areaColorReferenceRgba32[placeData]; //set to appropriate type color
                                }
                            }
                        }
                    }
                }

                image.SaveAsPng(ms);
            } //image disposed here.

            return ms.ToArray();
        }


        //Unlike the basic function, this one does its own DB lookup, and it's an overlay for the existing map tile, not a full redraw.
        public static byte[] DrawMPControlAreaMapTile(GeoArea totalArea, int pixelSizeCells, Tuple<long, int> shortcut = null)
        {
            //These are Mode=2 tiles in the database, used as an overlay that's merged into the baseline map tile. Should be faster than re-drawing a full tile.
            //Initial suggestion for these is to use a pixelSizeCell value 2 steps down from the areas size
            //EX: for Cell8 tiles, use 11 for the pixel cell size (this is the default I use, smallest location in a pixel sets the color)
            //or for Cell4 tiles, use Cell8 pixel size. (alternative sort for pixel color: largest area? Exclude points?)
            //But this is gaining flexibilty, and adjusting pixelSizeCells to a double to be degreesPerPixel allows for more freedom.
            double resolutionX, resolutionY;
            double filterSize = 0;
            GetResolutionValues(pixelSizeCells, out resolutionX, out resolutionY);
            if (pixelSizeCells < 10) // Roads and buildings are good at Cell10+. Not helpful at Cell8-;
                filterSize = resolutionX / 2; //things smaller than half a pixel will not be considered for the map tile. Might just want to toggle the alternate sort rules for pixels (most area, not smallest item)
            //Or should this filter to 'smallest area over filter size'?

            //To make sure we don't get any seams on our maptiles (or points that don't show a full circle, we add a little extra area to the image before drawing, then crop it out at the end.
            totalArea = new GeoArea(new GeoPoint(totalArea.Min.Latitude - resolutionCell10, totalArea.Min.Longitude - resolutionCell10), new GeoPoint(totalArea.Max.Latitude + resolutionCell10, totalArea.Max.Longitude + resolutionCell10));

            List<MapData> rowPlaces;
            //create a new bitmap.
            MemoryStream ms = new MemoryStream();
            int imagesizeX = (int)Math.Ceiling(totalArea.LongitudeWidth / resolutionX);
            int imagesizeY = (int)Math.Ceiling(totalArea.LatitudeHeight / resolutionY);

            var db = new PraxisContext();
            List<MapData> allPlaces = GetPlaces(totalArea, null, false, true); //Includes generated here with the final True parameter.
            List<long> placeIDs = allPlaces.Select(a => a.MapDataId).ToList();
            Dictionary<long, int> teamClaims = db.AreaControlTeams.Where(act => placeIDs.Contains(act.MapDataId)).ToDictionary(k => k.MapDataId, v => v.FactionId);


            //crop all places to the current area. This removes a ton of work from the process by simplifying geometry to only what's relevant, instead of drawing all of a great lake or state-wide park.
            var cropArea = Converters.GeoAreaToPolygon(totalArea);
            //A quick fix to drawing order when multiple areas take up the entire cell: sort before the crop (otherwise, the areas are drawn in a random order, which makes some disappear)
            //Affects small map tiles more often than larger ones, but it can affect both.
            allPlaces = allPlaces.Where(ap => teamClaims.ContainsKey(ap.MapDataId)).OrderByDescending(a => a.place.Area).ThenByDescending(a => a.place.Length).ToList();
            foreach (var ap in allPlaces)
                ap.place = ap.place.Intersection(cropArea); //This is a ref list, so this crop will apply if another call is made to this function with the same list.

            var options = new ShapeGraphicsOptions(); //currently using defaults.
            using (var image = new Image<Rgba32>(imagesizeX, imagesizeY))
            {
                image.Mutate(x => x.Fill(Rgba32.ParseHex("00000000"))); //image starts transparent. This gets merged onto the base tile

                foreach (var place in allPlaces)
                {
                    switch (place.place.GeometryType)
                    {
                        case "Polygon":
                            var drawThis = Converters.PolygonToDrawingPolygon(place.place, totalArea, resolutionX, resolutionY);
                                image.Mutate(x => x.Fill(teamColorReferenceRgba32[teamClaims[place.MapDataId]], drawThis));
                            break;
                        case "MultiPolygon":
                            foreach (var p in ((MultiPolygon)place.place).Geometries)
                            {
                                var drawThis2 = Converters.PolygonToDrawingPolygon(p, totalArea, resolutionX, resolutionY);
                                image.Mutate(x => x.Fill(teamColorReferenceRgba32[teamClaims[place.MapDataId]], drawThis2));
                            }
                            break;
                        case "LineString":
                            var drawThis3 = Converters.LineToDrawingLine(place.place, totalArea, resolutionX, resolutionY);
                            if (drawThis3.Count() > 1)
                                    image.Mutate(x => x.DrawLines(teamColorReferenceRgba32[teamClaims[place.MapDataId]], 1, drawThis3.ToArray()));
                            break;
                        case "MultiLineString":
                            foreach (var p in ((MultiLineString)place.place).Geometries)
                            {
                                var drawThis4 = Converters.LineToDrawingLine(p, totalArea, resolutionX, resolutionY);
                                image.Mutate(x => x.DrawLines(teamColorReferenceRgba32[teamClaims[place.MapDataId]], 1, drawThis4.ToArray()));
                            }
                            break;
                        case "Point":
                            var point = Converters.PointToPointF(place.place, totalArea, resolutionX, resolutionY);
                            var shape = new SixLabors.ImageSharp.Drawing.EllipsePolygon(Converters.PointToPointF(place.place, totalArea, resolutionX, resolutionY), 1.5f);
                            image.Mutate(x => x.Fill(teamColorReferenceRgba32[teamClaims[place.MapDataId]], shape));
                            break;
                    }
                }
                image.Mutate(x => x.Flip(FlipMode.Vertical)); //Plus codes are south-to-north, so invert the image to make it correct.
                int removeX = (int)Math.Ceiling(resolutionCell10 / resolutionX);
                int removeY = (int)Math.Ceiling(resolutionCell10 / resolutionY);
                image.Mutate(x => x.Crop(new Rectangle(removeX, removeY, imagesizeX - (removeX * 2), imagesizeY - (removeY * 2)))); //remove a Cell10's data from the edges.
                image.SaveAsPng(ms);
            } //image disposed here.

            return ms.ToArray();
        }

        public static byte[] DrawAreaMapTile(ref List<MapData> allPlaces, GeoArea totalArea, int pixelSizeCells)
        {
            //Initial suggestion for these is to use a pixelSizeCell value 2 steps down from the areas size
            //EX: for Cell8 tiles, use 11 for the pixel cell size (this is the default I use, smallest location in a pixel sets the color)
            //or for Cell4 tiles, use Cell8 pixel size. (alternative sort for pixel color: largest area? Exclude points?)
            //But this is gaining flexibilty, and adjusting pixelSizeCells to a double to be degreesPerPixel allows for more freedom.
            double resolutionX, resolutionY;
            double filterSize = 0;
            GetResolutionValues(pixelSizeCells, out resolutionX, out resolutionY);
            if (pixelSizeCells < 10) // Roads and buildings are good at Cell10+. Not helpful at Cell8-;
                filterSize = resolutionX / 2; //things smaller than half a pixel will not be considered for the map tile. Might just want to toggle the alternate sort rules for pixels (most area, not smallest item)
            //Or should this filter to 'smallest area over filter size'?

            //To make sure we don't get any seams on our maptiles (or points that don't show a full circle, we add a little extra area to the image before drawing, then crop it out at the end.
            totalArea = new GeoArea(new GeoPoint(totalArea.Min.Latitude - resolutionCell10, totalArea.Min.Longitude - resolutionCell10), new GeoPoint(totalArea.Max.Latitude + resolutionCell10, totalArea.Max.Longitude + resolutionCell10));

            List<MapData> rowPlaces;
            //create a new bitmap.
            MemoryStream ms = new MemoryStream();
            int imagesizeX = (int)Math.Ceiling(totalArea.LongitudeWidth / resolutionX);
            int imagesizeY = (int)Math.Ceiling(totalArea.LatitudeHeight / resolutionY);

            //crop all places to the current area. This removes a ton of work from the process by simplifying geometry to only what's relevant, instead of drawing all of a great lake or state-wide park.
            var cropArea = Converters.GeoAreaToPolygon(totalArea);
            //A quick fix to drawing order when multiple areas take up the entire cell: sort before the crop (otherwise, the areas are drawn in a random order, which makes some disappear)
            //Affects small map tiles more often than larger ones, but it can affect both.
            allPlaces = allPlaces.OrderByDescending(a => a.place.Area).ThenByDescending(a => a.place.Length).ToList();
            foreach (var ap in allPlaces)
                ap.place = ap.place.Intersection(cropArea); //This is a ref list, so this crop will apply if another call is made to this function with the same list.

            var options = new ShapeGraphicsOptions(); //currently using defaults.
            using (var image = new Image<Rgba32>(imagesizeX, imagesizeY))
            {
                image.Mutate(x => x.Fill(Rgba32.ParseHex(areaColorReference[999].First()))); //set all the areas to the background color

                //Now, instead of going per pixel, go per area, sorted by area descending.
                foreach (var place in allPlaces.Where(ap => ap.AreaTypeId !=13)) //Exclude admin areas if they got passed in.
                {
                    var color = areaColorReferenceRgba32[place.AreaTypeId];
                    switch (place.place.GeometryType)
                    {
                        case "Polygon":
                            var drawThis = Converters.PolygonToDrawingPolygon(place.place, totalArea, resolutionX, resolutionY);
                            image.Mutate(x => x.Fill(color, drawThis));
                            break;
                        case "MultiPolygon":
                            foreach (var p in ((MultiPolygon)place.place).Geometries)
                            {
                                var drawThis2 = Converters.PolygonToDrawingPolygon(p, totalArea, resolutionX, resolutionY);
                                image.Mutate(x => x.Fill(color, drawThis2));
                            }
                            break;
                        case "LineString":
                            var drawThis3 = Converters.LineToDrawingLine(place.place, totalArea, resolutionX, resolutionY);
                            if (drawThis3.Count() > 1)
                                image.Mutate(x => x.DrawLines(color, 1, drawThis3.ToArray()));
                            break;
                        case "MultiLineString":
                            foreach (var p in ((MultiLineString)place.place).Geometries)
                            {
                                var drawThis4 = Converters.LineToDrawingLine(p, totalArea, resolutionX, resolutionY);
                                image.Mutate(x => x.DrawLines(color, 1, drawThis4.ToArray()));
                            }
                            break;
                        case "Point":
                            var point = Converters.PointToPointF(place.place, totalArea, resolutionX, resolutionY);
                            //image.Mutate(x => x.DrawLines(color, 3, new PointF[] {point, point }));

                            var shape = new SixLabors.ImageSharp.Drawing.EllipsePolygon(Converters.PointToPointF(place.place, totalArea, resolutionX, resolutionY), 1.5f);
                            image.Mutate(x => x.Fill(color, shape));
                            break;
                    }
                }
                image.Mutate(x => x.Flip(FlipMode.Vertical)); //Plus codes are south-to-north, so invert the image to make it correct.
                int removeX = (int)Math.Ceiling(resolutionCell10 / resolutionX);
                int removeY = (int)Math.Ceiling(resolutionCell10 / resolutionY);
                image.Mutate(x => x.Crop(new Rectangle(removeX, removeY, imagesizeX - (removeX * 2), imagesizeY - (removeY * 2)))); //remove a Cell10's data from the edges.
                image.SaveAsPng(ms);
            } //image disposed here.

            return ms.ToArray();
        }

        public static byte[] DrawAreaMapTileSlippy(ref List<MapData> allPlaces, GeoArea totalArea, double areaHeight, double areaWidth, bool transparent = false)
        {
            //Resolution scaling here is flexible, since we're always drawing a 512x512 tile.
            //This has the crop-logic removed temporarily while I figure out how to fix that for a variable-resolution image.
            double resolutionX, resolutionY;
            double filterSize = 0;
            resolutionX = areaWidth / 512;
            resolutionY = areaHeight / 512;
            bool drawEverything = false; //for debugging/testing
            var smallestFeature = (drawEverything ? 0 : resolutionX < resolutionY ? resolutionX : resolutionY);
            //GetResolutionValues(pixelSizeCells, out resolutionX, out resolutionY);
            //if (pixelSizeCells < 10) // Roads and buildings are good at Cell10+. Not helpful at Cell8-;
            //filterSize = resolutionX / 2; //things smaller than half a pixel will not be considered for the map tile. Might just want to toggle the alternate sort rules for pixels (most area, not smallest item)
            //Or should this filter to 'smallest area over filter size'?

            //To make sure we don't get any seams on our maptiles (or points that don't show a full circle, we add a little extra area to the image before drawing, then crop it out at the end.
            //totalArea = new GeoArea(new GeoPoint(totalArea.Min.Latitude - resolutionCell10, totalArea.Min.Longitude - resolutionCell10), new GeoPoint(totalArea.Max.Latitude + resolutionCell10, totalArea.Max.Longitude + resolutionCell10));

            List<MapData> rowPlaces;
            //create a new bitmap. Add 10 pixels of padding to each side
            MemoryStream ms = new MemoryStream();
            int imagesizeX = 512; //(int)Math.Ceiling(totalArea.LongitudeWidth / resolutionX);
            int imagesizeY = 512; //(int)Math.Ceiling(totalArea.LatitudeHeight / resolutionY);

            //crop all places to the current area. This removes a ton of work from the process by simplifying geometry to only what's relevant, instead of drawing all of a great lake or state-wide park.
            var cropArea = Converters.GeoAreaToPolygon(totalArea);
            //A quick fix to drawing order when multiple areas take up the entire cell: sort before the crop (otherwise, the areas are drawn in a random order, which makes some disappear)
            //Affects small map tiles more often than larger ones, but it can affect both.
            //This where clause means things smaller than 1 pixel won't get drawn. It's a C# filter here, but it would be faster to do DB-side on a SizeColumn on Mapdata to save more time, in the function above this one.
            allPlaces = allPlaces.Where(a => a.place.Area > smallestFeature || a.place.Length > smallestFeature || a.place.GeometryType == "Point").OrderByDescending(a => a.place.Area).ThenByDescending(a => a.place.Length).ToList();
            foreach (var ap in allPlaces)
                ap.place = ap.place.Intersection(cropArea); //This is a ref list, so this crop will apply if another call is made to this function with the same list.

            //This loop still occasionally throws an 'index was outside the bounds of the array issue. Not sure what the deal is. It's a dependency issue, but I might need to work around it.
            var options = new ShapeGraphicsOptions(); //currently using defaults.
            using (var image = new Image<Rgba32>(imagesizeX, imagesizeY))
            {
                if (!transparent)
                    image.Mutate(x => x.Fill(Rgba32.ParseHex(areaColorReference[999].First()))); //set whole image to background color
                else
                    image.Mutate(x => x.Fill(Rgba32.ParseHex("00000000"))); //sets whole image to transparent

                //Now, instead of going per pixel, go per area, sorted by area descending.
                foreach (var place in allPlaces.Where(ap => ap.AreaTypeId != 13)) //Exclude admin areas if they got passed in.
                {
                    var color = areaColorReferenceRgba32[place.AreaTypeId];
                    switch (place.place.GeometryType)
                    {
                        case "Polygon":
                            var drawThis = Converters.PolygonToDrawingPolygon(place.place, totalArea, resolutionX, resolutionY);
                            //drawThis = drawThis.Translate(10, 10)
                            image.Mutate(x => x.Fill(color, drawThis));
                            break;
                        case "MultiPolygon":
                            foreach (var p in ((MultiPolygon)place.place).Geometries)
                            {
                                var drawThis2 = Converters.PolygonToDrawingPolygon(p, totalArea, resolutionX, resolutionY);
                                //drawThis2 = drawThis2.Translate(10, 10);
                                image.Mutate(x => x.Fill(color, drawThis2));
                            }
                            break;
                        case "LineString":
                            var drawThis3 = Converters.LineToDrawingLine(place.place, totalArea, resolutionX, resolutionY);
                            if (drawThis3.Count() > 1)
                            {
                                //drawThis3.ForEach(a => { a.X += 10; a.Y += 10; }); 
                                image.Mutate(x => x.DrawLines(color, 1, drawThis3.ToArray()));
                            }
                            break;
                        case "MultiLineString":
                            foreach (var p in ((MultiLineString)place.place).Geometries)
                            {
                                var drawThis4 = Converters.LineToDrawingLine(p, totalArea, resolutionX, resolutionY);
                                //drawThis4.ForEach(a => { a.X += 10; a.Y += 10; });
                                image.Mutate(x => x.DrawLines(color, 1, drawThis4.ToArray()));
                            }
                            break;
                        case "Point":
                            var point = Converters.PointToPointF(place.place, totalArea, resolutionX, resolutionY);
                            //image.Mutate(x => x.DrawLines(color, 3, new PointF[] {point, point }));

                            var shape = new SixLabors.ImageSharp.Drawing.EllipsePolygon(Converters.PointToPointF(place.place, totalArea, resolutionX, resolutionY), new SizeF((float)(2 * resolutionCell11Lon / resolutionX), (float)(2 * resolutionCell11Lat / resolutionY))); //was 1.5f, decided this should draw points as being 1 Cell11 big instead to scale.
                            image.Mutate(x => x.Fill(color, shape));
                            break;
                    }
                }
                image.Mutate(x => x.Flip(FlipMode.Vertical)); //Plus codes are south-to-north, so invert the image to make it correct.
                //image.Mutate(x => x.Crop(new Rectangle(10, 10, 512, 512))); //remove 10 pixels from each edge.
                image.SaveAsPng(ms);
            } //image disposed here.

            return ms.ToArray();
        }

        public static byte[] DrawMPAreaMapTileSlippy(GeoArea totalArea, double areaHeight, double areaWidth)
        {
            //Resolution scaling here is flexible, since we're always drawing a 512x512 tile.
            //This has the crop-logic removed temporarily while I figure out how to fix that for a variable-resolution image.
            double resolutionX, resolutionY;
            double filterSize = 0;
            resolutionX = areaWidth / 512;
            resolutionY = areaHeight / 512;
            bool drawEverything = false; //for debugging/testing
            var smallestFeature = (drawEverything ? 0 : resolutionX < resolutionY ? resolutionX : resolutionY);
            //GetResolutionValues(pixelSizeCells, out resolutionX, out resolutionY);
            //if (pixelSizeCells < 10) // Roads and buildings are good at Cell10+. Not helpful at Cell8-;
            //filterSize = resolutionX / 2; //things smaller than half a pixel will not be considered for the map tile. Might just want to toggle the alternate sort rules for pixels (most area, not smallest item)
            //Or should this filter to 'smallest area over filter size'?

            //To make sure we don't get any seams on our maptiles (or points that don't show a full circle, we add a little extra area to the image before drawing, then crop it out at the end.
            //totalArea = new GeoArea(new GeoPoint(totalArea.Min.Latitude - resolutionCell10, totalArea.Min.Longitude - resolutionCell10), new GeoPoint(totalArea.Max.Latitude + resolutionCell10, totalArea.Max.Longitude + resolutionCell10));

            List<MapData> rowPlaces;
            //create a new bitmap. Add 10 pixels of padding to each side
            MemoryStream ms = new MemoryStream();
            int imagesizeX = 512; //(int)Math.Ceiling(totalArea.LongitudeWidth / resolutionX);
            int imagesizeY = 512; //(int)Math.Ceiling(totalArea.LatitudeHeight / resolutionY);

            var db = new PraxisContext();
            List<MapData> allPlaces = GetPlaces(totalArea, null, false, true); //Includes generated here with the final True parameter.
            List<long> placeIDs = allPlaces.Select(a => a.MapDataId).ToList();
            Dictionary<long, int> teamClaims = db.AreaControlTeams.Where(act => placeIDs.Contains(act.MapDataId)).ToDictionary(k => k.MapDataId, v => v.FactionId);
            allPlaces = allPlaces.Where(a => teamClaims.ContainsKey(a.MapDataId)).ToList();

            //crop all places to the current area. This removes a ton of work from the process by simplifying geometry to only what's relevant, instead of drawing all of a great lake or state-wide park.
            var cropArea = Converters.GeoAreaToPolygon(totalArea);
            //A quick fix to drawing order when multiple areas take up the entire cell: sort before the crop (otherwise, the areas are drawn in a random order, which makes some disappear)
            //Affects small map tiles more often than larger ones, but it can affect both.
            //This where clause means things smaller than 1 pixel won't get drawn. It's a C# filter here, but it would be faster to do DB-side on a SizeColumn on Mapdata to save more time, in the function above this one.
            allPlaces = allPlaces.Where(a => a.place.Area > smallestFeature || a.place.Length > smallestFeature || a.place.GeometryType == "Point").OrderByDescending(a => a.place.Area).ThenByDescending(a => a.place.Length).ToList();
            foreach (var ap in allPlaces)
                ap.place = ap.place.Intersection(cropArea); //This is a ref list, so this crop will apply if another call is made to this function with the same list.

            var options = new ShapeGraphicsOptions(); //currently using defaults.
            using (var image = new Image<Rgba32>(imagesizeX, imagesizeY))
            {
                image.Mutate(x => x.Fill(Rgba32.ParseHex("00000000"))); //set all the areas to the background color

                //Now, instead of going per pixel, go per area, sorted by area descending.
                foreach (var place in allPlaces.Where(ap => ap.AreaTypeId != 13)) //Exclude admin areas if they got passed in.
                {
                    var color = areaColorReferenceRgba32[place.AreaTypeId];
                    switch (place.place.GeometryType)
                    {
                        case "Polygon":
                            var drawThis = Converters.PolygonToDrawingPolygon(place.place, totalArea, resolutionX, resolutionY);
                            //drawThis = drawThis.Translate(10, 10)
                            image.Mutate(x => x.Fill(teamColorReferenceRgba32[teamClaims[place.MapDataId]], drawThis));
                            break;
                        case "MultiPolygon":
                            foreach (var p in ((MultiPolygon)place.place).Geometries)
                            {
                                var drawThis2 = Converters.PolygonToDrawingPolygon(p, totalArea, resolutionX, resolutionY);
                                //drawThis2 = drawThis2.Translate(10, 10);
                                image.Mutate(x => x.Fill(teamColorReferenceRgba32[teamClaims[place.MapDataId]], drawThis2));
                            }
                            break;
                        case "LineString":
                            var drawThis3 = Converters.LineToDrawingLine(place.place, totalArea, resolutionX, resolutionY);
                            if (drawThis3.Count() > 1)
                            {
                                //drawThis3.ForEach(a => { a.X += 10; a.Y += 10; }); 
                                image.Mutate(x => x.DrawLines(teamColorReferenceRgba32[teamClaims[place.MapDataId]], 1, drawThis3.ToArray()));
                            }
                            break;
                        case "MultiLineString":
                            foreach (var p in ((MultiLineString)place.place).Geometries)
                            {
                                var drawThis4 = Converters.LineToDrawingLine(p, totalArea, resolutionX, resolutionY);
                                //drawThis4.ForEach(a => { a.X += 10; a.Y += 10; });
                                image.Mutate(x => x.DrawLines(teamColorReferenceRgba32[teamClaims[place.MapDataId]], 1, drawThis4.ToArray()));
                            }
                            break;
                        case "Point":
                            var point = Converters.PointToPointF(place.place, totalArea, resolutionX, resolutionY);
                            //image.Mutate(x => x.DrawLines(color, 3, new PointF[] {point, point }));

                            var shape = new SixLabors.ImageSharp.Drawing.EllipsePolygon(Converters.PointToPointF(place.place, totalArea, resolutionX, resolutionY), new SizeF((float)(2 * resolutionCell11Lon / resolutionX), (float)(2 * resolutionCell11Lat / resolutionY))); //was 1.5f, decided this should draw points as being 1 Cell11 big instead to scale.
                            image.Mutate(x => x.Fill(teamColorReferenceRgba32[teamClaims[place.MapDataId]], shape));
                            break;
                    }
                }
                image.Mutate(x => x.Flip(FlipMode.Vertical)); //Plus codes are south-to-north, so invert the image to make it correct.
                //image.Mutate(x => x.Crop(new Rectangle(10, 10, 512, 512))); //remove 10 pixels from each edge.
                image.SaveAsPng(ms);
            } //image disposed here.

            return ms.ToArray();
        }

        public static byte[] DrawPaintTownSlippyTile(GeoArea relevantArea, int instanceID)
        {
            //TODO: figure out which zoom level makes attempting to draw this pointless, and return an empty transparent image instead.
            //It might be fun on rare occasion to try and draw this all at once, but zoomed out too far and we won't see anything and will be very slow.
            //Find all Cell8s in the relevant area.
            MemoryStream ms = new MemoryStream();
            var imagesizeX = 512;
            var imagesizeY = 512;
            var Cell8Wide = relevantArea.LongitudeWidth / resolutionCell8;
            var Cell8High = relevantArea.LatitudeHeight / resolutionCell8;
            var Cell10PixelSize = resolutionCell10 / relevantArea.LongitudeWidth; //Making this square for now.
            var resolutionX = relevantArea.LongitudeWidth / imagesizeX;
            var resolutionY = relevantArea.LatitudeHeight / imagesizeY;

            //These may or may not be the same, even if the map tile is smaller than 1 Cell8.
            var firstCell8 = new OpenLocationCode(relevantArea.SouthLatitude, relevantArea.WestLongitude).CodeDigits.Substring(0,8);
            var lastCell8 = new OpenLocationCode(relevantArea.NorthLatitude, relevantArea.EastLongitude).CodeDigits.Substring(0, 8);
            if (firstCell8 != lastCell8)
            {
                //quick hack to make sure we process enough data.
                Cell8High++;
                Cell8Wide++;
            }

            List<PaintTownEntry> allData = new List<PaintTownEntry>();
            for(var x = 0; x < Cell8Wide; x++)
                for(var y = 0; y < Cell8High; y++)
                {
                    var thisCell = new OpenLocationCode(relevantArea.SouthLatitude + (resolutionCell8 * x), relevantArea.WestLongitude + (resolutionCell8 * y)).CodeDigits.Substring(0, 8);
                    var thisData = PaintTown.LearnCell8(instanceID, thisCell);
                    allData.AddRange(thisData);
                }

            var options = new ShapeGraphicsOptions(); //currently using defaults.
            using (var image = new Image<Rgba32>(imagesizeX, imagesizeY))
            {
                image.Mutate(x => x.Fill(Rgba32.ParseHex("00000000"))); //transparent.

                foreach (var line in allData)
                {
                    //if (line == "")
                        //continue;

                    //string[] components = line.Split('=');
                    var location = OpenLocationCode.DecodeValid(line.Cell10);
                    //This is a workaround for an issue with the SixLabors.Drawing library. I think this dramatically slows down drawing tiles, but it means that all the tiles get drawn.
                    try
                    {
                        var placeAsPoly = Converters.GeoAreaToPolygon(location);
                        var drawingSquare = Converters.PolygonToDrawingPolygon(placeAsPoly, relevantArea, resolutionX, resolutionY);
                        image.Mutate(x => x.Fill(teamColorReferenceRgba32[line.FactionId], drawingSquare));
                    }
                    catch(Exception ex)
                    {
                        //we won't draw this one entry.
                        //var a = 1;
                        //give up
                    }
                }

                image.Mutate(x => x.Flip(FlipMode.Vertical)); //Plus codes are south-to-north, so invert the image to make it correct.
                //image.Mutate(x => x.Crop(new Rectangle(10, 10, 512, 512))); //remove 10 pixels from each edge.
                image.SaveAsPng(ms);
            }
            
            return ms.ToArray();
        }
    }
}
