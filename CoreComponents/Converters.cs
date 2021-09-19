using PraxisCore.Support;
using Google.OpenLocationCode;
using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Prepared;
using System;
using System.Collections.Generic;
using System.Linq;
using static PraxisCore.Singletons;

namespace PraxisCore
{
    /// <summary>
    /// Functions that translate one thing to another somewhere in PraxisMapper.
    /// </summary>
    public static class Converters
    {
        /// <summary>
        /// Converts a GeoArea into a NTS coordinate array
        /// </summary>
        /// <param name="plusCodeArea">The GeoArea to convert</param>
        /// <returns>a Coordinate array using the GeoArea's boundaries</returns>
        public static Coordinate[] GeoAreaToCoordArray(GeoArea plusCodeArea)
        {
            var cord1 = new Coordinate(plusCodeArea.Min.Longitude, plusCodeArea.Min.Latitude);
            var cord2 = new Coordinate(plusCodeArea.Min.Longitude, plusCodeArea.Max.Latitude);
            var cord3 = new Coordinate(plusCodeArea.Max.Longitude, plusCodeArea.Max.Latitude);
            var cord4 = new Coordinate(plusCodeArea.Max.Longitude, plusCodeArea.Min.Latitude);
            var cordSeq = new Coordinate[5] { cord4, cord3, cord2, cord1, cord4 };

            return cordSeq;
        }

        /// <summary>
        /// Converts a GeoArea from a PlusCode into an NTS Polygon. Useful for making PlusCodes work with spatial indexes in a database.
        /// </summary>
        /// <param name="plusCodeArea">GeoArea from a PlusCode</param>
        /// <returns>a Polygon covering the GeoArea provided</returns>
        public static Geometry GeoAreaToPolygon(GeoArea plusCodeArea)
        {
            return factory.CreatePolygon(GeoAreaToCoordArray(plusCodeArea));
        }

        /// <summary>
        /// Converts a GeoArea from a PlusCode into an NTS PreparedPolygon, ideal for using to test intersections against a list of Geometry objects.
        /// </summary>
        /// <param name="plusCodeArea">GeoArea from a PlusCode</param>
        /// <returns>a PreparedPolygon covering the GeoArea provided.</returns>
        public static IPreparedGeometry GeoAreaToPreparedPolygon(GeoArea plusCodeArea)
        {
            return pgf.Create(GeoAreaToPolygon(plusCodeArea));
        }

        /// <summary>
        /// Convert an OSMSharp CompleteWay into an array of coordinates. Used by the FeatureInterpreter.
        /// </summary>
        /// <param name="w">The CompleteWay to convert</param>
        /// <returns>An array of coordinate pairs</returns>
        public static Coordinate[] CompleteWayToCoordArray(OsmSharp.Complete.CompleteWay w)
        {
            if (w == null)
                return null;

            List<Coordinate> results = new List<Coordinate>();
            results.Capacity = w.Nodes.Count();

            foreach (var node in w.Nodes)
                results.Add(new Coordinate(node.Longitude.Value, node.Latitude.Value));

            return results.ToArray();
        }

        /// <summary>
        /// Converts an NTS Polygon into a SkiaSharp SKPoint array so that it can be drawn in SkiaSharp.
        /// </summary>
        /// <param name="place">Polygon object to be converted/drawn</param>
        /// <param name="drawingArea">GeoArea representing the image area being drawn. Usually passed from an ImageStats object</param>
        /// <param name="degreesPerPixelX">Width of each pixel in degrees</param>
        /// <param name="degreesPerPixelY">Height of each pixel in degrees</param>
        /// <returns>Array of SkPoints for the image information provided.</returns>
        public static SkiaSharp.SKPoint[] PolygonToSKPoints(Geometry place, GeoArea drawingArea, double degreesPerPixelX, double degreesPerPixelY)
        {
            SkiaSharp.SKPoint[] points = place.Coordinates.Select(o => new SkiaSharp.SKPoint((float)((o.X - drawingArea.WestLongitude) * (1 / degreesPerPixelX)), (float)((o.Y - drawingArea.SouthLatitude) * (1 / degreesPerPixelY)))).ToArray();
            return points;
        }

        public static SkiaSharp.SKPoint PlaceInfoToSKPoint(PraxisCore.StandaloneDbTables.PlaceInfo2 pi, ImageStats imgstats)
        {
            SkiaSharp.SKPoint point = new SkiaSharp.SKPoint();
            point.X = (float)((pi.lonCenter - imgstats.area.WestLongitude) * (1 / imgstats.degreesPerPixelX));
            point.Y =(float)((pi.latCenter - imgstats.area.SouthLatitude) * (1 / imgstats.degreesPerPixelY));
            return point;
        }

        public static SkiaSharp.SKPoint[] PlaceInfoToSKPoints(PraxisCore.StandaloneDbTables.PlaceInfo2 pi, ImageStats info)
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
        public static SkiaSharp.SKRect PlaceInfoToRect(PraxisCore.StandaloneDbTables.PlaceInfo2 pi, ImageStats info)
        {
            SkiaSharp.SKRect r = new SkiaSharp.SKRect();
            float heightMod = (float)pi.height / 2;
            float widthMod = (float)pi.width / 2;
            r.Left = (float)pi.lonCenter - widthMod;
            r.Left = (float)(r.Left - info.area.WestLongitude) * (float)(1/info.degreesPerPixelX);
            r.Right =(float) pi.lonCenter + widthMod;
            r.Right = (float)(r.Right - info.area.WestLongitude) * (float)(1 / info.degreesPerPixelX);
            r.Top = (float)pi.latCenter + heightMod;
            r.Top = (float)(r.Top - info.area.SouthLatitude) * (float)(1 / info.degreesPerPixelY);
            r.Bottom = (float)pi.latCenter - heightMod;
            r.Bottom = (float)(r.Bottom - info.area.SouthLatitude) * (float)(1 / info.degreesPerPixelY);


            return r;
        }

        /// <summary>
        /// Converts an NTS Geometry object into a GeoArea object matching the Geometry's internal envelope.
        /// </summary>
        /// <param name="g">The NTS Geometry object to convert</param>
        /// <returns>the GeoArea covering the Geometry's internal envelope, or null if the conversion fails.</returns>
        public static GeoArea GeometryToGeoArea(Geometry g)
        {
            try
            {
                GeoArea results = new GeoArea(g.EnvelopeInternal.MinY, g.EnvelopeInternal.MinX, g.EnvelopeInternal.MaxY, g.EnvelopeInternal.MaxX);
                return results;
            }
            catch (Exception ex)
            {
                return null;
            }
        }

        /// <summary>
        /// Get the X value for a SlippyMap tile based on a given longitude and a zoom value
        /// </summary>
        /// <param name="lon">longitude in degrees</param>
        /// <param name="z">zoom level</param>
        /// <returns>the X value to draw a tile at the given longitude</returns>
        public static int GetSlippyXFromLon(double lon, int z)
        {
            return (int)(Math.Floor((lon + 180.0) / 360.0 * (1 << z)));
        }

        /// <summary>
        /// Get the Y value for a SlippyMap tile based on a given latitude and a zoom value
        /// </summary>
        /// <param name="lat">latitude in degrees</param>
        /// <param name="z">zoom level</param>
        /// <returns>the Y value to draw a tile at the given latitude</returns>
        public static int GetSlippyYFromLat(double lat, int z)
        {
            return (int)Math.Floor((1 - Math.Log(Math.Tan(lat.ToRadians()) + 1 / Math.Cos(lat.ToRadians())) / Math.PI) / 2 * (1 << z));
        }

        /// <summary>
        /// Gets the longitude to use give a SlippyMap X coord and zoom level
        /// </summary>
        /// <param name="x">X parameter from a SlippyMap coordinate</param>
        /// <param name="z">Zoom level for a SlippyMap tile</param>
        /// <returns>longitude in degrees of the SlippyMap tile</returns>
        public static double SlippyXToLon(int x, int z)
        {
            return x / (double)(1 << z) * 360.0 - 180;
        }

        /// <summary>
        /// Gets the latitude to use give a SlippyMap Y coord and zoom level
        /// </summary>
        /// <param name="y">Y parameter from a SlippyMap coordinate</param>
        /// <param name="z">Zoom level for a SlippyMap tile</param>
        /// <returns>latitude in degrees of the SlippyMap tile</returns>
        public static double SlippyYToLat(int y, int z)
        {
            double n = Math.PI - 2.0 * Math.PI * y / (double)(1 << z);
            return 180.0 / Math.PI * Math.Atan(0.5 * (Math.Exp(n) - Math.Exp(-n)));
        }

        //Incomplete, slippy tiles require some compromises for this to work
        //public static void GetSlippyTileForPoint(GeoArea buffered, int zoomLevel)
        //{
        //    var intersectCheck = Converters.GeoAreaToPolygon(buffered);

        //    //start drawing maptiles and sorting out data.
        //    var swCornerLon = Converters.GetSlippyXFromLon(intersectCheck.EnvelopeInternal.MinX, zoomLevel);
        //    var neCornerLon = Converters.GetSlippyXFromLon(intersectCheck.EnvelopeInternal.MaxX, zoomLevel);
        //    var swCornerLat = Converters.GetSlippyYFromLat(intersectCheck.EnvelopeInternal.MinY, zoomLevel);
        //    var neCornerLat = Converters.GetSlippyYFromLat(intersectCheck.EnvelopeInternal.MaxY, zoomLevel);

        //}
    }
}
