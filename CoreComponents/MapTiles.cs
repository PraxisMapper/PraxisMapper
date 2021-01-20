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

namespace CoreComponents
{
    public static class MapTiles
    {
        //Class TODO:
        //Make a core function that takes in resolution, so that doesn't need a function per scale. might need a check for Cell11+ resolution since thats not symmetrical

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

        // as above but each pixel is an 11 cell instead of a 10 cell. more detail but slower.
        //NOTE: this function has been better optimized than the above. 
        public static byte[] DrawAreaMapTile11(ref List<MapData> allPlaces, GeoArea totalArea)
        {
            List<MapData> rowPlaces;
            //create a new bitmap.
            MemoryStream ms = new MemoryStream();
            PreparedGeometryFactory pgf = new PreparedGeometryFactory(); //this is supposed to be faster than regular geometry.
            //pixel formats. RBGA32 allows for hex codes. RGB24 doesnt?
            int imagesizeX = (int)Math.Floor(totalArea.LongitudeWidth / resolutionCell11Lon); //scales to area size
            int imagesizeY = (int)Math.Floor(totalArea.LatitudeHeight / resolutionCell11Lat); //scales to area size          


            double[] xCoords = new double[imagesizeX + 1];
            double[] yCoords = new double[imagesizeY + 1];
            List<GeoArea> areas = new List<GeoArea>();
            for (int i = 0; i <= imagesizeX; i++)
            {
                xCoords[i] = totalArea.Min.Longitude + (resolutionCell11Lon * i);
            }
            for (int i = 0; i <= imagesizeY; i++)
            {
                yCoords[i] = totalArea.Min.Latitude + (resolutionCell11Lat * i);
            }

            //pre-cache the set of data we need per column. Storing IDs saves us a lot of Intersects checks later.
            List<long>[] columnPlaces = new List<long>[imagesizeX];
            for (int i = 0; i < imagesizeX; i++)
            {
                columnPlaces[i] = GetPlaces(new GeoArea(new GeoPoint(totalArea.Min.Latitude, xCoords[i]), new GeoPoint(totalArea.Max.Latitude, xCoords[i + 1])), allPlaces, false).Select(m => m.MapDataId).ToList();
            }

            using (var image = new Image<Rgba32>(imagesizeX, imagesizeY)) //each 11 cell in this area is a pixel.
            {
                image.Mutate(x => x.Fill(Rgba32.ParseHex(areaColorReference[999].First()))); //set all the areas to the background color
                for (int y = 0; y < image.Height; y++)
                {
                    //Dramatic performance improvement by limiting this to just the row's area.
                    rowPlaces = GetPlaces(new GeoArea(new GeoPoint(yCoords[y], totalArea.Min.Longitude), new GeoPoint(yCoords[y + 1], totalArea.Max.Longitude)), allPlaces, false);
                    var preparedPlaces = rowPlaces.Select(rp => new PreparedMapData() { PreparedMapDataID = rp.MapDataId, place = pgf.Create(rp.place), AreaTypeId = rp.AreaTypeId }).ToList();

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
                                var tempPlaces = preparedPlaces.Where(r => columnPlaces[x].Contains(r.PreparedMapDataID)).ToList(); //reduces Intersects() calls done in the next function
                                placeData = GetAreaTypeForCell11(xCoords[x], yCoords[y], ref tempPlaces);
                                if (placeData != 0)
                                {
                                    var color = areaColorReference[placeData].First();
                                    pixelRow[x] = Rgba32.ParseHex(color); //set to appropriate type color
                                }
                            }
                        }
                    }
                }

                image.SaveAsPng(ms);
            } //image disposed here.

            return ms.ToArray();
        }

        //TODO: drop in optimizations for the Cell11 resolution version into this function. 
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
    }
}
