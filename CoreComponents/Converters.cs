using EGIS.ShapeFileLib;
using Google.OpenLocationCode;
using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Prepared;
using OsmSharp;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
        /// Convert a list or array of OSMSharp Nodes (probably from a CompleteWay) into an array of coordinates. Used by the FeatureInterpreter.
        /// </summary>
        /// <param name="w">The list of nodes to convert</param>
        /// <returns>An array of coordinate pairs</returns>
        public static Coordinate[] NodeArrayToCoordArray(IList<Node> w)
        {
            if (w == null)
                return null;

            Coordinate[] results = new Coordinate[w.Count];
            for (int i = 0; i < w.Count; i++)
                results[i] = new Coordinate(w[i].Longitude.Value, w[i].Latitude.Value); //Coordinates are X, Y

            return results;
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

        /// <summary>
        /// Takes a Shapefile record as a collection of double-precision points, and converts that to a polygon.
        /// </summary>
        /// <param name="shapePoints">The list of points to convert. Expects results from EGIS.ShapeFileLib.ShapeFile.GetShapeDataD() and in WSG84 projection</param>
        /// <returns>A Polygon composed</returns>
        public static Polygon ShapefileRecordToPolygon(ReadOnlyCollection<PointD[]> shapePoints)
        {
            var coordArray = shapePoints.First().Select(s => new Coordinate(s.X, s.Y)).ToArray();
            var poly = new Polygon(new LinearRing(coordArray));
            return poly;
        }

        public static Point GeometryToCenterPoint(Geometry g)
        {
            return g.Centroid;
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
