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
            //Keeping this around for reference.

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

            //pre-cache the set of data we need per column. Storing IDs saves us a lot of Intersects checks later. Approx. 1 second of 5 seconds of drawing time on a busy Cell8 (41 Places)
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

        public static byte[] DrawSimple(ref List<MapData> allPlaces, GeoArea totalArea, int pixelSizeCells)
        {
            //TODO: put the simple, un-optimized code into a function here as a reference to confirm 
            //the optimizations work at all scales. I've wondered if they slow down small tiles as they make big ones faster.
            return null;

            double resolutionX, resolutionY;
            GetResolutionValues(pixelSizeCells, out resolutionX, out resolutionY);
            List<MapData> rowPlaces;
            //create a new bitmap.
            MemoryStream ms = new MemoryStream();
            //pixel formats. RBGA32 allows for hex codes. RGB24 doesnt?
            int imagesizeX = (int)Math.Floor(totalArea.LongitudeWidth / resolutionX);
            int imagesizeY = (int)Math.Floor(totalArea.LatitudeHeight / resolutionY);
            using (var image = new Image<Rgba32>(imagesizeX, imagesizeY))
            {
                image.Mutate(x => x.Fill(Rgba32.ParseHex(areaColorReference[999].First()))); //set all the areas to the background color
                for (int y = 0; y < image.Height; y++)
                {
                    rowPlaces = GetPlaces(new GeoArea(new GeoPoint(totalArea.Min.Latitude + (resolutionCell10 * y), totalArea.Min.Longitude), new GeoPoint(totalArea.Min.Latitude + (resolutionCell10 * (y + 1)), totalArea.Max.Longitude)), allPlaces);

                    Span<Rgba32> pixelRow = image.GetPixelRowSpan(image.Height - y - 1); //Plus code data is searched south-to-north, image is inverted otherwise.
                    for (int x = 0; x < image.Width; x++)
                    {
                        //Set the pixel's color by its type.
                        int placeData = GetAreaTypeForCell11(totalArea.Min.Longitude + (resolutionCell10 * x), totalArea.Min.Latitude + (resolutionCell10 * y), ref rowPlaces);
                        if (placeData != 0)
                        {
                            var color = areaColorReference[placeData].First();
                            pixelRow[x] = Rgba32.ParseHex(color); //set to appropriate type color
                        }
                    }
                }

                image.SaveAsPng(ms);
            } //image disposed here.

            return ms.ToArray();

        }

        //TODO: drop in optimizations for the Cell11 resolution version into this function below
        public static byte[] DrawAreaMapTile(ref List<MapData> allPlaces, GeoArea totalArea)
        {
            List<MapData> rowPlaces;
            //create a new bitmap.
            MemoryStream ms = new MemoryStream();
            //pixel formats. RBGA32 allows for hex codes. RGB24 doesnt?
            int imagesize = (int)Math.Floor(totalArea.LatitudeHeight / resolutionCell10); //scales to area size
            using (var image = new Image<Rgba32>(imagesize, imagesize)) //each 10 cell in this area is a pixel.
            {
                image.Mutate(x => x.Fill(Rgba32.ParseHex(areaColorReference[999].First()))); //set all the areas to the background color
                for (int y = 0; y < image.Height; y++)
                {
                    //Dramatic performance improvement by limiting this to just the row's area.
                    rowPlaces = GetPlaces(new GeoArea(new GeoPoint(totalArea.Min.Latitude + (resolutionCell10 * y), totalArea.Min.Longitude), new GeoPoint(totalArea.Min.Latitude + (resolutionCell10 * (y + 1)), totalArea.Max.Longitude)), allPlaces);

                    Span<Rgba32> pixelRow = image.GetPixelRowSpan(image.Height - y - 1); //Plus code data is searched south-to-north, image is inverted otherwise.
                    for (int x = 0; x < image.Width; x++)
                    {
                        //Set the pixel's color by its type.
                        int placeData = GetAreaTypeForCell11(totalArea.Min.Longitude + (resolutionCell10 * x), totalArea.Min.Latitude + (resolutionCell10 * y), ref rowPlaces);
                        if (placeData != 0)
                        {
                            var color = areaColorReference[placeData].First();
                            pixelRow[x] = Rgba32.ParseHex(color); //set to appropriate type color
                        }
                    }
                }

                image.SaveAsPng(ms);
            } //image disposed here.

            return ms.ToArray();
        }

        

        //TODO: make this use vector rules too.
        public static byte[] DrawMPControlAreaMapTile11(GeoArea totalArea, Tuple<long, int> shortcut = null)
        {
            List<MapData> rowPlaces;
            var db = new PraxisContext();
            var factionColors = db.Factions.ToLookup(k => k.FactionId, v => v.HtmlColor);
            var places = GetPlaces(totalArea);
            var placeList = places.Select(p => p.MapDataId).ToList();
            var teamClaims = db.AreaControlTeams.Where(a => placeList.Contains(a.MapDataId)).ToDictionary(k => k.MapDataId, v => v.FactionId);

            //create a new bitmap.
            MemoryStream ms = new MemoryStream();
            //pixel formats. RBGA32 allows for hex codes. RGB24 doesnt?
            int imagesizeX = (int)Math.Floor(totalArea.LongitudeWidth / resolutionCell11Lon); //scales to area size
            int imagesizeY = (int)Math.Floor(totalArea.LatitudeHeight / resolutionCell11Lat); //scales to area size
            using (var image = new Image<Rgba32>(imagesizeX, imagesizeY)) //each 11 cell in this area is a pixel.
            {
                image.Mutate(x => x.Fill(Rgba32.ParseHex("00000000"))); //set all the areas to transparent.
                for (int y = 0; y < image.Height; y++)
                {
                    //Dramatic performance improvement by limiting this to just the row's area.
                    rowPlaces = GetPlaces(new GeoArea(new GeoPoint(totalArea.Min.Latitude + (resolutionCell11Lat * y), totalArea.Min.Longitude), new GeoPoint(totalArea.Min.Latitude + (resolutionCell11Lat * (y + 1)), totalArea.Max.Longitude)), places);

                    Span<Rgba32> pixelRow = image.GetPixelRowSpan(image.Height - y - 1); //Plus code data is searched south-to-north, image is inverted otherwise.
                    for (int x = 0; x < image.Width; x++)
                    {
                        //Set the pixel's color by its faction owner.
                        int placeData = GetFactionForCell11(totalArea.Min.Longitude + (resolutionCell11Lon * x), totalArea.Min.Latitude + (resolutionCell11Lat * y), ref rowPlaces, shortcut);
                        if (placeData != 0)
                        {
                            var color = factionColors[placeData].First();
                            pixelRow[x] = Rgba32.ParseHex(color); //set to appropriate type color
                        }
                    }
                }

                image.SaveAsPng(ms);
            } //image disposed here.

            return ms.ToArray();
        }

        public static byte[] DrawAreaMapTile(ref List<MapData> allPlaces, GeoArea totalArea, int pixelSizeCells)
        {
            //Idea here was to convert shapes into some kind of coordinate that ImageSharp can use.
            //Requires ImageSharp to use polygon and line options for drawing.
            //Isnt finished. TODO find this out.
            //Initial suggestion for these is to use a pixelSizeCell value 2 steps down from the areas size
            //EX: for Cell8 tiles, use 11 for the pixel cell size (this is the default I use, smallest location in a pixel sets the color)
            //or for Cell4 tiles, use Cell8 pixel size. (alternative sort for pixel color: largest area? Exclude points?)
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

            //crop all places to the current area. This removes a ton of work from the process by simplifying geometry to only what's relevant, instead of drawing all of a great lake or state-wide park.
            var cropArea = Converters.GeoAreaToPolygon(totalArea);
            foreach (var ap in allPlaces)
                ap.place = ap.place.Intersection(cropArea);

            using (var image = new Image<Rgba32>(imagesizeX, imagesizeY))
            {
                image.Mutate(x => x.Fill(Rgba32.ParseHex(areaColorReference[999].First()))); //set all the areas to the background color

                //Now, instead of going per pixel, go per area, sorted by area descending.
                foreach (var place in allPlaces.OrderByDescending(a => a.place.Area).ThenByDescending(a => a.place.Length))
                {
                    var color = areaColorReferenceRgba32[place.AreaTypeId];
                    switch(place.place.GeometryType)
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
                    }
                }
                image.Mutate(x => x.Flip(FlipMode.Vertical));
                image.SaveAsPng(ms);
            } //image disposed here.

            return ms.ToArray();
        }
    }
}
