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
        public static List<TagParserEntry> styles;

        public static void DrawFromRawTest1(string filename)
        {
            //this would be part of app startup if i go with this.
            foreach(var tpe in Singletons.defaultTagParserEntries)
            {
                SetPaintForTPE(tpe);
            }

            Random r = new Random();
            FileStream fs = new FileStream(filename, FileMode.Open);
            //get location in lat/long format.
            //Delaware is 2384, 3138,13
            //Cedar point, where I had issues before, is 35430/48907/17
            //int x = 2384;
            //int y = 3138;
            //int zoom = 13;
            //int x = 35430;
            //int y = 48907;
            //int zoom = 17;
            int x = 8857;
            int y = 12226;
            int zoom = 15;

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
            //styleLayerList = new Style("liberty-style.json");

            styles = Singletons.defaultTagParserEntries;

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
                place.paint = GetStyleForOsmWay(w.Tags);

                mapDatas.Add(place);
            }
            //Reorder MapDatas correctly.
            mapDatas = mapDatas.OrderByDescending(m => m.place.Area).ThenByDescending(m => m.place.Length).ToList();

            foreach (var place in mapDatas)
            {
                //TODO: assign each Place a Style, or maybe its own Paint element, so I can look that up once and reuse the results.
                //instead of looping over each Style every time.

                paint = place.paint;
                var path = new SKPath();
                switch (place.place.GeometryType)
                {
                    case "Polygon":
                        path.AddPoly(Converters.PolygonToSKPoints(place.place, relevantArea, degreesPerPixelX, degreesPerPixelY));
                        //paint.Style = SKPaintStyle.Fill;
                        //paint.Color = new SKColor((byte)r.Next(0, 255), (byte)r.Next(0, 255), (byte)r.Next(0, 255));
                        canvas.DrawPath(path, paint);
                        break;
                    case "MultiPolygon":
                        //paint.Color = new SKColor((byte)r.Next(0, 255), (byte)r.Next(0, 255), (byte)r.Next(0, 255));
                        //paint.Style = SKPaintStyle.Fill;
                        foreach (var p in ((MultiPolygon)place.place).Geometries)
                        {
                            var path2 = new SKPath();
                            path2.AddPoly(Converters.PolygonToSKPoints(p, relevantArea, degreesPerPixelX, degreesPerPixelY));
                            canvas.DrawPath(path2, paint);
                        }
                        break;
                    case "LineString":
                        //paint.Style = SKPaintStyle.Stroke;
                        //paint.Color = new SKColor((byte)r.Next(0, 255), (byte)r.Next(0, 255), (byte)r.Next(0, 255));
                        var points = Converters.PolygonToSKPoints(place.place, relevantArea, degreesPerPixelX, degreesPerPixelY);
                        for (var line = 0; line < points.Length - 1; line++)
                            canvas.DrawLine(points[line], points[line + 1], paint);
                        break;
                    case "MultiLineString":
                        foreach (var p in ((MultiLineString)place.place).Geometries)
                        {
                            //paint.Style = SKPaintStyle.Stroke;
                            //paint.Color = new SKColor((byte)r.Next(0, 255), (byte)r.Next(0, 255), (byte)r.Next(0, 255));
                            var points2 = Converters.PolygonToSKPoints(p, relevantArea, degreesPerPixelX, degreesPerPixelY);
                            for (var line = 0; line < points2.Length - 1; line++)
                                canvas.DrawLine(points2[line], points2[line + 1], paint);
                        }
                        break;
                    case "Point":
                        //paint.Style = SKPaintStyle.Fill;
                        var circleRadius = (float)(.000125 / degreesPerPixelX / 2); //I want points to be drawn as 1 Cell10 in diameter.
                        var convertedPoint = Converters.PolygonToSKPoints(place.place, relevantArea, degreesPerPixelX, degreesPerPixelY);
                        //paint.Color = new SKColor((byte)r.Next(0, 255), (byte)r.Next(0, 255), (byte)r.Next(0, 255));
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

        public static SKPaint FindStyleToApply(OsmSharp.Tags.TagsCollection tags)
        {
            SKPaint paint = new SKPaint();



            return paint;
        }

        private static SKPaint StyleLayerToPaint(Layer layer)
        {
            //A lot of this is going to come from the SkiaCanvas file.
            var scale = 1;
            SKPaint paint = new SKPaint();
            var paintData = layer.Paint;
            Dictionary<string, object> attributes = new Dictionary<string, object>();

            if (paintData.ContainsKey("fill-color"))
            {
                paint.Color = parseColor(getValue(paintData["fill-color"], attributes));
            }

            if (paintData.ContainsKey("background-color"))
            {
                paint.Color = parseColor(getValue(paintData["background-color"], attributes));
            }

            if (paintData.ContainsKey("text-color"))
            {
                paint.Color = parseColor(getValue(paintData["text-color"], attributes));
            }

            if (paintData.ContainsKey("line-color"))
            {
                paint.Color = parseColor(getValue(paintData["line-color"], attributes));
            }

            // --

            //if (paintData.ContainsKey("line-pattern"))
            //{
            //    paint.PathEffect = SKPathEffect.CreateDash(getValue(paintData["line-pattern"], attributes), 0);
            //}

            //if (paintData.ContainsKey("background-pattern"))
            //{
            //    paint.BackgroundPattern = (string)getValue(paintData["background-pattern"], attributes);
            //}

            //if (paintData.ContainsKey("fill-pattern"))
            //{
            //    paint.FillPattern = (string)getValue(paintData["fill-pattern"], attributes);
            //}

            // --

            //if (paintData.ContainsKey("text-opacity"))
            //{
            //    paint.TextOpacity = Convert.ToDouble(getValue(paintData["text-opacity"], attributes));
            //}

            //if (paintData.ContainsKey("icon-opacity"))
            //{
            //    paint.IconOpacity = Convert.ToDouble(getValue(paintData["icon-opacity"], attributes));
            //}

            if (paintData.ContainsKey("line-opacity"))
            {
                var opacity = Convert.ToDouble(getValue(paintData["line-opacity"], attributes));
                var tempColor = paint.Color;
                paint.Color = new SKColor(tempColor.Red, tempColor.Green, tempColor.Blue, (byte)(tempColor.Alpha * opacity)); //TODO: clamp to 0-255
            }

            if (paintData.ContainsKey("fill-opacity"))
            {
                var opacity = Convert.ToDouble(getValue(paintData["fill-opacity"], attributes));
                var tempColor = paint.Color;
                paint.Color = new SKColor(tempColor.Red, tempColor.Green, tempColor.Blue, (byte)(tempColor.Alpha * opacity)); //TODO: clamp to 0-255
            }

            //if (paintData.ContainsKey("background-opacity"))
            //{
            //    paint.BackgroundOpacity = Convert.ToDouble(getValue(paintData["background-opacity"], attributes));
            //}

            // --

            if (paintData.ContainsKey("line-width"))
            {
                paint.StrokeWidth = Convert.ToSingle(getValue(paintData["line-width"], attributes)) * scale; // * screenScale;
            }

            //text property. ignoring.
            //if (paintData.ContainsKey("line-offset"))
            //{
            //    paint.LineOffset = Convert.ToDouble(getValue(paintData["line-offset"], attributes)) * scale;// * screenScale;
            //}


            if (paintData.ContainsKey("line-dasharray"))
            {
                var array = (getValue(paintData["line-dasharray"], attributes) as object[]);
                var effect = SKPathEffect.CreateDash(array.Select(n => (float)n).ToArray(), 0);
                paint.PathEffect = effect;
            }

            // --

            //if (paintData.ContainsKey("text-halo-color"))
            //{
            //    paint.TextStrokeColor = parseColor(getValue(paintData["text-halo-color"], attributes));
            //}

            //if (paintData.ContainsKey("text-halo-width"))
            //{
            //    paint.TextStrokeWidth = Convert.ToDouble(getValue(paintData["text-halo-width"], attributes)) * scale;
            //}

            //if (paintData.ContainsKey("text-halo-blur"))
            //{
            //    paint.TextStrokeBlur = Convert.ToDouble(getValue(paintData["text-halo-blur"], attributes)) * scale;
            //}

            return paint;
        }

        public static SKColor parseColor(object iColor)
        {
            if (iColor.GetType() == typeof(SKColor))
            {
                return (SKColor)iColor;
            }

            var colorString = (string)iColor;

            if (colorString[0] == '#')
            {
                return SKColor.Parse(colorString);
                //return (SKColor)ColorConverter.ConvertFromString(colorString);
            }

            if (colorString.StartsWith("hsl("))
            {
                var segments = colorString.Replace('%', '\0').Split(',', '(', ')');
                float h = float.Parse(segments[1]);
                float s = float.Parse(segments[2]);
                float l = float.Parse(segments[3]);

                ////var color = (new ColorMine.ColorSpaces.Hsl()
                //{
                //    H = h,
                //    S = s,
                //    L = l,
                //}).ToRgb();
                return SKColor.FromHsl(h, s, l);
                //return Color.FromRgb((byte)color.R, (byte)color.G, (byte)color.B);
            }

            if (colorString.StartsWith("hsla("))
            {
                var segments = colorString.Replace('%', '\0').Split(',', '(', ')');
                float h = float.Parse(segments[1]);
                float s = float.Parse(segments[2]);
                float l = float.Parse(segments[3]);
                byte a = byte.Parse((int.Parse(segments[4]) * 255).ToString());

                return SKColor.FromHsl(h, s, l, a);
                //var color = (new ColorMine.ColorSpaces.Hsl()
                //{
                //    H = h,
                //    S = s,
                //    L = l,
                //}).ToRgb();

                //return Color.FromArgb((byte)(a), (byte)color.R, (byte)color.G, (byte)color.B);
            }

            if (colorString.StartsWith("rgba("))
            {
                var segments = colorString.Replace('%', '\0').Split(',', '(', ')');
                double r = double.Parse(segments[1]);
                double g = double.Parse(segments[2]);
                double b = double.Parse(segments[3]);
                double a = double.Parse(segments[4]) * 255;

                return new SKColor((byte)r, (byte)g, (byte)b, (byte)a);
                //return Color.FromArgb((byte)a, (byte)r, (byte)g, (byte)b);
            }

            if (colorString.StartsWith("rgb("))
            {
                var segments = colorString.Replace('%', '\0').Split(',', '(', ')');
                double r = double.Parse(segments[1]);
                double g = double.Parse(segments[2]);
                double b = double.Parse(segments[3]);

                return new SKColor((byte)r, (byte)g, (byte)b);
                //return Color.FromRgb((byte)r, (byte)g, (byte)b);
            }

            try
            {
                return SKColor.Parse(colorString);
                //return (Color)ColorConverter.ConvertFromString(colorString);
            }
            catch (Exception e)
            {
                throw new NotImplementedException("Not implemented color format: " + colorString);
            }
            //return Colors.Violet;
            return SKColors.Violet;
        }

        public static object getValue(object token, Dictionary<string, object> attributes = null)
        {

            if (token is string && attributes != null)
            {
                string value = token as string;
                if (value.Length == 0)
                {
                    return "";
                }
                if (value[0] == '$')
                {
                    return getValue(attributes[value]);
                }
            }

            if (token.GetType().IsArray)
            {
                var array = token as object[];
                //List<object> result = new List<object>();

                //foreach (object item in array)
                //{
                //    var obj = getValue(item, attributes);
                //    result.Add(obj);
                //}

                //return result.ToArray();

                return array.Select(item => getValue(item, attributes)).ToArray();
            }
            else if (token is Dictionary<string, object>)
            {
                var dict = token as Dictionary<string, object>;
                if (dict.ContainsKey("stops"))
                {
                    var stops = dict["stops"] as object[];
                    // if it has stops, it's interpolation domain now :P
                    //var pointStops = stops.Select(item => new Tuple<double, JToken>((item as JArray)[0].Value<double>(), (item as JArray)[1])).ToList();
                    var pointStops = stops.Select(item => new Tuple<double, object>(Convert.ToDouble((item as object[])[0]), (item as object[])[1])).ToList();

                    var zoom = (double)attributes["$zoom"];
                    var minZoom = pointStops.First().Item1;
                    var maxZoom = pointStops.Last().Item1;
                    double power = 1;

                    if (minZoom == 5 && maxZoom == 10)
                    {

                    }

                    double zoomA = minZoom;
                    double zoomB = maxZoom;
                    int zoomAIndex = 0;
                    int zoomBIndex = pointStops.Count() - 1;

                    // get min max zoom bounds from array
                    if (zoom <= minZoom)
                    {
                        //zoomA = minZoom;
                        //zoomB = pointStops[1].Item1;
                        return pointStops.First().Item2;
                    }
                    else if (zoom >= maxZoom)
                    {
                        //zoomA = pointStops[pointStops.Count - 2].Item1;
                        //zoomB = maxZoom;
                        return pointStops.Last().Item2;
                    }
                    else
                    {
                        // checking for consecutive values
                        for (int i = 1; i < pointStops.Count(); i++)
                        {
                            var previousZoom = pointStops[i - 1].Item1;
                            var thisZoom = pointStops[i].Item1;

                            if (zoom >= previousZoom && zoom <= thisZoom)
                            {
                                zoomA = previousZoom;
                                zoomB = thisZoom;

                                zoomAIndex = i - 1;
                                zoomBIndex = i;
                                break;
                            }
                        }
                    }


                    if (dict.ContainsKey("base"))
                    {
                        power = Convert.ToDouble(getValue(dict["base"], attributes));
                    }

                    //var referenceElement = (stops[0] as object[])[1];
                    return null;

                }
            }


            //if (token is string)
            //{
            //    return token as string;
            //}
            //else if (token is bool)
            //{
            //    return (bool)token;
            //}
            //else if (token is float)
            //{
            //    return token as float;
            //}
            //else if (token.Type == JTokenType.Integer)
            //{
            //    return token.Value<int>();
            //}
            //else if (token.Type == JTokenType.None || token.Type == JTokenType.Null)
            //{
            //    return null;
            //}


            return token;
        }

        public static SKPaint GetStyleForOsmWay(OsmSharp.Tags.TagsCollectionBase tags)
        {
            //TODO: make this faster, i should make the SKPaint objects once at startup, then return those here instead of generating them per thing I need to draw.
            
            //Drawing order: 
            //these styles should be drawing in Descending Index order (Continents are 88, then countries and states, then roads, etc etc.... airport taxiways are 1).

            //Search logic:
            //SourceLayer is the general name of the category of stuff to draw. Transportation, for example.
            //Filter has some rules on what things this specific entry should apply to.
            //Most specific filter should apply, but if its the category than use the base category values.

            //I might just want to write my own rules, and possibly make then DB entries? simplify them down to just Skia rules?
            //also, if it doesn't find any rules, draw background color.

            //now, attempt to see if these tags apply in any way to our style list.
            //foreach (var rules in styleLayerList.Layers)
            //{
            //    //get rules
            //    var filterRules = rules.Filter;
            //    var areaName = rules.SourceLayer;
            //}
            if (tags == null || tags.Count() == 0)
            {
                var style = styles.Last(); //background must be last.
                return style.paint;
            }

            foreach(var drawingRules in styles)
            {
                if (MatchOnTags(drawingRules, tags))
                {
                    return drawingRules.paint;
                }
            }
            return null;
        }

        public static void SetPaintForTPE(TagParserEntry tpe)
        {
            var paint = new SKPaint();
            paint.Color = SKColor.Parse(tpe.HtmlColorCode);
            if (tpe.FillOrStroke == "fill")
                paint.Style = SKPaintStyle.Fill;
            else
                paint.Style = SKPaintStyle.Stroke;
            paint.StrokeWidth = tpe.LineWidth;
            tpe.paint = paint;
        }


        public static bool MatchOnTags(TagParserEntry tpe, OsmSharp.Tags.TagsCollectionBase tags)
        {
            int rulesCount = tpe.TagParserMatchRules.Count();
            bool[] rulesMatch = new bool[rulesCount];
            int matches = 0;


            for(var i = 0; i < tpe.TagParserMatchRules.Count(); i++) // var entry in tpe.TagParserMatchRules)
            {
                var entry = tpe.TagParserMatchRules.ElementAt(i);
                if (entry.Value == "*") //The Key needs to exist, but any value counts.
                {
                    if (tags.ContainsKey(entry.Key))
                    {
                        matches++;
                        rulesMatch[i] = true;
                        continue;
                    }
                }

                switch (entry.MatchType)
                {
                    case "any":
                        if (!tags.ContainsKey(entry.Key))
                            continue;

                        var possibleValues = entry.Value.Split("|");
                        var actualValue = tags[entry.Key];
                        if (possibleValues.Contains(actualValue))
                        {
                            matches++;
                            rulesMatch[i] = true;
                        }
                        break;
                    case "equals":
                        if (!tags.ContainsKey(entry.Key))
                            continue;
                        if (tags[entry.Key] == entry.Value)
                        {
                            matches++;
                            rulesMatch[i] = true;
                        }

                        break;


                    //case "or":
                    //Uh, need to check this set out specifi
                    //break;

                    case "default":
                        //Always matches.
                        return true;
                        break;
                }
            }

            return rulesCount == matches; //almost works except for OR checks.
        }

    }
}
