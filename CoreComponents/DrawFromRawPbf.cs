using CoreComponents.Support;
using Google.OpenLocationCode;
using NetTopologySuite.Geometries;
using OsmSharp.Streams;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static CoreComponents.DbTables;

namespace CoreComponents
{
    public static class DrawFromRawPbf
    {
        //Testing out logic to just make map tiles from scratch, since nothing seems to do what I want the way I want to.

        public static void DrawFromRawTest1(string filename)
        {
            Random r = new Random();
            FileStream fs = new FileStream(filename, FileMode.Open);
            //get location in lat/long format.
            //Delaware is 2384, 3138,13
            //Cedar point, where I had issues before, is 35430/48907/17
            //int x = 2384;
            //int y = 3138;
            //int zoom = 13;
            int x = 35430;
            int y = 48907;
            int zoom = 17;

            var n = Math.Pow(2, zoom);

            var lon_degree_w = x / n * 360 - 180;
            var lon_degree_e = (x + 1) / n * 360 - 180;
            var lat_rads_n = Math.Atan(Math.Sinh(Math.PI * (1 - 2 * y / n)));
            var lat_degree_n = lat_rads_n * 180 / Math.PI;
            var lat_rads_s = Math.Atan(Math.Sinh(Math.PI * (1 - 2 * (y + 1) / n)));
            var lat_degree_s = lat_rads_s * 180 / Math.PI;

            var relevantArea = new GeoArea(lat_degree_s, lon_degree_w, lat_degree_n, lon_degree_e);
            var areaHeightDegrees = lat_degree_n - lat_degree_s;
            var areaWidthDegrees = 360 / n;

            //Get relevant data from PBF 
            //(can change that to other data source later)
            var elements = GetData((float)relevantArea.WestLongitude, (float)relevantArea.EastLongitude, (float)relevantArea.SouthLatitude, (float)relevantArea.NorthLatitude, fs);


            //Draw each object according to some style rules
            //load and parse style file
            var style = new Style("liberty-style.json");

            //TEST LOGIC:
            //take each relation, complete it, draw it in a random color.
            //Take each area, complete it, draw it in a random color.
            //Nodes that meet standalone drawing criteria get a solid white dot.

            //baseline image data stuff
            int imageSizeX = 512;
            int imageSizeY = 512;
            double degreesPerPixelX = relevantArea.LongitudeWidth / imageSizeX;
            double degreesPerPixelY = relevantArea.LatitudeHeight / imageSizeX;
            SKBitmap bitmap = new SKBitmap(512, 512, SKColorType.Rgba8888, SKAlphaType.Premul);
            SKCanvas canvas = new SKCanvas(bitmap);
            var bgColor = new SKColor();
            SKColor.TryParse("00000000", out bgColor);
            canvas.Clear(bgColor);
            canvas.Scale(1, -1, imageSizeX / 2, imageSizeY / 2);
            SKPaint paint = new SKPaint();
            SKColor color = new SKColor();
            var nodes = elements.Where(e => e.Type == OsmSharp.OsmGeoType.Node).Select(e => (OsmSharp.Node)e).ToLookup(k => k.Id);
            var rels = elements.Where(e => e.Type == OsmSharp.OsmGeoType.Relation).Select(e => (OsmSharp.Relation)e).ToList();
            foreach (var r2 in rels)
            {
                //Draw this?
            }
            var ways = elements.Where(e => e.Type == OsmSharp.OsmGeoType.Way).Select(e => (OsmSharp.Way)e).ToList(); //How to order these?
            var mapDatas = new List<MapData>();
            foreach (var w in ways)
            {
                //get node data and translate it to image coords
                WayData wd = new WayData();
                foreach (long nr in w.Nodes)
                {
                    var osmNode = nodes[nr].FirstOrDefault();
                    if (osmNode != null)
                    {
                        var myNode = new NodeData(osmNode.Id.Value, (float)osmNode.Latitude, (float)osmNode.Longitude);
                        wd.nds.Add(myNode);
                    }
                }

                //quick hack to make this work.
                wd.AreaType = "park";

                var place = Converters.ConvertWayToMapData(ref wd);

                if (place == null)
                    continue;

                mapDatas.Add(place);
            }
            //Reorder MapDatas correctly.
            mapDatas = mapDatas.OrderByDescending(m => m.place.Area).ThenByDescending(m => m.place.Length).ToList();

            foreach (var place in mapDatas)
            {
                var path = new SKPath();
                switch (place.place.GeometryType)
                {
                    case "Polygon":
                        path.AddPoly(Converters.PolygonToSKPoints(place.place, relevantArea, degreesPerPixelX, degreesPerPixelY));
                        paint.Style = SKPaintStyle.Fill;
                        paint.Color = new SKColor((byte)r.Next(0, 255), (byte)r.Next(0, 255), (byte)r.Next(0, 255));
                        canvas.DrawPath(path, paint);
                        break;
                    case "MultiPolygon":
                        paint.Color = new SKColor((byte)r.Next(0, 255), (byte)r.Next(0, 255), (byte)r.Next(0, 255));
                        paint.Style = SKPaintStyle.Fill;
                        foreach (var p in ((MultiPolygon)place.place).Geometries)
                        {
                            var path2 = new SKPath();
                            path2.AddPoly(Converters.PolygonToSKPoints(p, relevantArea, degreesPerPixelX, degreesPerPixelY));
                            canvas.DrawPath(path2, paint);
                        }
                        break;
                    case "LineString":
                        paint.Style = SKPaintStyle.Stroke;
                        paint.Color = new SKColor((byte)r.Next(0, 255), (byte)r.Next(0, 255), (byte)r.Next(0, 255));
                        var points = Converters.PolygonToSKPoints(place.place, relevantArea, degreesPerPixelX, degreesPerPixelY);
                        for (var line = 0; line < points.Length - 1; line++)
                            canvas.DrawLine(points[line], points[line + 1], paint);
                        break;
                    case "MultiLineString":
                        foreach (var p in ((MultiLineString)place.place).Geometries)
                        {
                            paint.Style = SKPaintStyle.Stroke;
                            paint.Color = new SKColor((byte)r.Next(0, 255), (byte)r.Next(0, 255), (byte)r.Next(0, 255));
                            var points2 = Converters.PolygonToSKPoints(p, relevantArea, degreesPerPixelX, degreesPerPixelY);
                            for (var line = 0; line < points2.Length - 1; line++)
                                canvas.DrawLine(points2[line], points2[line + 1], paint);
                        }
                        break;
                    case "Point":
                        paint.Style = SKPaintStyle.Fill;
                        var circleRadius = (float)(.000125 / degreesPerPixelX / 2); //I want points to be drawn as 1 Cell10 in diameter.
                        var convertedPoint = Converters.PolygonToSKPoints(place.place, relevantArea, degreesPerPixelX, degreesPerPixelY);
                        paint.Color = new SKColor((byte)r.Next(0, 255), (byte)r.Next(0, 255), (byte)r.Next(0, 255));
                        canvas.DrawCircle(convertedPoint[0], circleRadius, paint);
                        break;
                }

                //draw all the WayData entry.
                //var path = new SKPath();
                //path.AddPoly(Converters.PolygonToSKPoints(place.place, relevantArea, degreesPerPixelX, degreesPerPixelY));
                //paint.Style = SKPaintStyle.Fill;
                //paint.Color = new SKColor((byte)r.Next(0, 255), (byte)r.Next(0, 255), (byte)r.Next(0, 255));
                //canvas.DrawPath(path, paint);
            }


            foreach (var no in nodes)
            {
                //draw this.
            }


            //Save object to .png file.
            var ms = new MemoryStream();
            var skms = new SKManagedWStream(ms);
            bitmap.Encode(skms, SKEncodedImageFormat.Png, 100);
            var results = ms.ToArray();
            skms.Dispose(); ms.Close(); ms.Dispose();
            File.WriteAllBytes("test.png", results);
            return;
        }

        private static List<OsmSharp.OsmGeo> GetData(float left, float right, float bottom, float top, Stream stream)
        {
            var source = new PBFOsmStreamSource(stream);

            //var allElements = source.FilterBox(left, top, right, bottom).Where(s => s.Type != OsmSharp.OsmGeoType.Node).ToComplete().Select(s => (OsmSharp.Complete.CompleteOsmGeo)s).ToList();
            var allElements2 = source.FilterBox(left, top, right, bottom, true).ToList();

            return allElements2;
        }
    }
}
