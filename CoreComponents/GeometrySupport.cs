using Google.OpenLocationCode;
using NetTopologySuite.Geometries;
using OsmSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using static PraxisCore.ConstantValues;
using static PraxisCore.DbTables;
using static PraxisCore.Singletons;

namespace PraxisCore {
    /// <summary>
    /// Common functions revolving around Geometry object operations
    /// </summary>
    public static class GeometrySupport
    {
        //Shared class for functions that do work on Geometry objects.

        private static readonly NetTopologySuite.NtsGeometryServices s = new NetTopologySuite.NtsGeometryServices(PrecisionModel.Floating.Value, 4326);
        private static readonly NetTopologySuite.IO.WKTReader geomTextReader = new NetTopologySuite.IO.WKTReader(s); // {DefaultSRID = 4326 };

        public static GeoArea MakeBufferedGeoArea(GeoArea original)
        {
            return original.PadGeoArea(MapTileSupport.BufferSize);
        }

        public static GeoArea MakeBufferedGeoArea(GeoArea original, double bufferSize) {
            return original.PadGeoArea(bufferSize);
        }

        /// <summary>
        /// Forces a Polygon to run counter-clockwise, and inner holes to run clockwise, which is important for NTS geometry. SQL Server rejects objects that aren't CCW.
        /// </summary>
        /// <param name="p">Polygon to run operations on</param>
        /// <returns>the Polygon in CCW orientaiton, or null if the orientation cannot be confimred or corrected</returns>
        public static Polygon CCWCheck(Polygon p)
        {
            if (p == null)
                return null;

            if (p.NumPoints < 4)
                //can't determine orientation, because this poly was shortened to an awkward line.
                return null;

            //NTS specs also requires holes in a polygon to be in clockwise order, opposite the outer shell.
            for (int i = 0; i < p.Holes.Length; i++)
            {
                if (!p.Holes[i].IsCCW) { //this looks backwards, but it passes for SQL Server
                    p.Holes[i] = (LinearRing)p.Holes[i].Reverse();
                    if(p.Holes[i].IsCCW) {
                        Log.WriteLog("Hole refused to orient CW correctly.");
                        return null;
                    }
                }
            }

            if (!p.Shell.IsCCW) {
                p = (Polygon)p.Reverse();
                if (!p.Shell.IsCCW) {
                    Log.WriteLog("shell refused to orient CCW correctly.");
                    return null;
                }
            }

            return p;
        }

        /// <summary>
        /// Creates a Geometry object from the WellKnownText for a geometry.
        /// </summary>
        /// <param name="elementGeometry">The WKT for a geometry item</param>
        /// <returns>a Geometry object for the WKT provided</returns>
        public static Geometry GeometryFromWKT(string elementGeometry)
        {
            return geomTextReader.Read(elementGeometry);
        }

        /// <summary>
        /// Run a CCWCheck on a Geometry and (if enabled) simplify the geometry of an object to the minimum
        /// resolution for PraxisMapper gameplay, which is a Cell10 in degrees (.000125). Simplifying areas reduces storage
        /// space for OSM Elements by about 30% but dramatically reduces the accuracy of rendered map tiles.
        /// </summary>
        /// <param name="place">The Geometry to CCWCheck and potentially simplify</param>
        /// <returns>The Geometry object, in CCW orientation and potentially simplified.</returns>
        public static Geometry SimplifyPlace(Geometry place)
        {
            if (!place.IsValid)
                place = NetTopologySuite.Geometries.Utilities.GeometryFixer.Fix(place);

            if (!SimplifyAreas)
            {
                //We still do a CCWCheck here, because it's always expected to be done here as part of the process.
                //But we don't alter the geometry past that.
                //NOTE: this is faster this way. VS keeps suggesting to immediately make it (place is Polygon p). Ignore it.
                if (place is Polygon)
                    place = CCWCheck((Polygon)place);
                else if (place is MultiPolygon)
                {
                    MultiPolygon mp = (MultiPolygon)place;
                    for (int i = 0; i < mp.Geometries.Length; i++)
                    {
                        mp.Geometries[i] = CCWCheck((Polygon)mp.Geometries[i]);
                    }
                    if (mp.Geometries.Any(g => g == null))
                    {
                        mp = new MultiPolygon(mp.Geometries.Where(g => g != null).Select(g => (Polygon)g).ToArray());
                        if (mp.Geometries.Length == 0)
                            return null;
                        place = mp;
                    }
                    else
                        place = mp;
                }
                return place; //will be null if it fails the CCWCheck
            }

            //Note: SimplifyArea CAN reverse a polygon's orientation, especially in a multi-polygon, so don't do CheckCCW until after
            var simplerPlace = NetTopologySuite.Simplify.TopologyPreservingSimplifier.Simplify(place, resolutionCell10); //This cuts storage space for files by 30-50% but makes maps look pretty bad.
            if (simplerPlace is Polygon)
            {
                simplerPlace = CCWCheck((Polygon)simplerPlace);
                return simplerPlace; //will be null if this object isn't correct in either orientation.
            }
            else if (simplerPlace is MultiPolygon)
            {
                MultiPolygon mp = (MultiPolygon)simplerPlace;
                for (int i = 0; i < mp.Geometries.Length; i++)
                {
                    mp.Geometries[i] = CCWCheck((Polygon)mp.Geometries[i]);
                }
                if (!mp.Geometries.Any(g => g == null))
                    return mp;
                else
                {
                    mp = new MultiPolygon(mp.Geometries.Where(g => g != null).Select(g => (Polygon)g).ToArray());
                    if (mp.Geometries.Length == 0)
                        return null;
                    return mp;
                }

            }
            return null; //some of the outer shells aren't compatible. Should alert this to the user if possible.
        }

        /// <summary>
        /// Create a database Place from an OSMSharp Complete object.
        /// </summary>
        /// <param name="g">the CompleteOSMGeo object to prepare to save to the DB</param>
        /// <returns>the Place ready to save to the DB</returns>
        public static DbTables.Place ConvertOsmEntryToPlace(OsmSharp.Complete.ICompleteOsmGeo g)
        {
            var tags = TagParser.getFilteredTags(g.Tags);
            if (tags == null || tags.Count == 0)
                return null; //untagged elements are not useful, do not store them.

            try
            {
                var geometry = PMFeatureInterpreter.Interpret(g); 
                if (geometry == null)
                {
                    Log.WriteLog("Error: " + g.Type.ToString() + " " + g.Id + "-" + TagParser.GetName(g) + " didn't interpret into a Geometry object", Log.VerbosityLevels.Errors);
                    return null;
                }
                if (geometry.GeometryType == "LinearRing" || (geometry.GeometryType == "LineString" && geometry.Coordinates.First() == geometry.Coordinates.Last())) {
                    //I want to update all LinearRings to Polygons, and let the style determine if they're Filled or Stroked.
                    geometry = Singletons.geometryFactory.CreatePolygon((LinearRing)geometry);
                }

                var place = new DbTables.Place();
                place.SourceItemID = g.Id;
                place.SourceItemType = (g.Type == OsmGeoType.Relation ? 3 : g.Type == OsmGeoType.Way ? 2 : 1);
                geometry = SimplifyPlace(geometry);
                if (geometry == null)
                {
                    Log.WriteLog("Error: " + g.Type.ToString() + " " + g.Id + " didn't simplify for some reason.", Log.VerbosityLevels.Errors);
                    return null;
                }
                geometry.SRID = 4326;//Required for SQL Server to accept data.
                place.ElementGeometry = geometry;
                place.Tags = tags; 

                TagParser.ApplyTags(place, "mapTiles");
                if (place.StyleName == "unmatched" || place.StyleName == "background")
                {
                    //skip, leave value at 0.
                }
                else
                {
                    place.DrawSizeHint = CalculateDrawSizeHint(place);
                }
                return place;
            }
            catch(Exception ex)
            {
                Log.WriteLog("Error: Item " + g.Id + " failed to process. " + ex.Message);
                return null;
            }
        }

        public static double CalculateDrawSizeHint(DbTables.Place place)
        {
            //The default assumption here is that a Cell11 is 1 pixel for gameplay tiles before factoring in GameTileScale.
            //So we take the area of the drawn element in degrees, divide by (the size of a square Cell11 divided by GameTileScale).
            //That's how many pixels an individual element would take up at typical scale. MapTiles will skip anything below 1. (Slippy tiles scale proportionally)
            //The value of what to skip will be automatically adjusted based on the area being drawn.
            var paintOp = TagParser.allStyleGroups["mapTiles"][place.StyleName].PaintOperations;
            var pixelMultiplier = MapTileSupport.GameTileScale;

            if (place.ElementGeometry.Area > 0)
                return (place.ElementGeometry.Area / (ConstantValues.squareCell11Area / pixelMultiplier));
            else if (place.ElementGeometry.Length > 0)
            {
                var lineWidth = paintOp.Max(p => p.LineWidthDegrees);
                var rectSize = lineWidth * place.ElementGeometry.Length;
                return (rectSize / (ConstantValues.squareCell11Area / pixelMultiplier));
            }
            else if (paintOp.Any(p => !string.IsNullOrEmpty(p.FileName)))
            {
                //I need a way to find out how big this image is.
                return 32; // for now, just guessing that I made these 32x32 images.
            }
            else
            {
                var pointRadius = paintOp.Max(p => p.LineWidthDegrees); //for Points, this is the radius of the circle being drawn.
                var pointRadiusPixels = ((pointRadius * pointRadius * float.Pi) / (ConstantValues.squareCell11Area / pixelMultiplier));
                return pointRadiusPixels;
            }
        }

        /// <summary>
        /// Loads up TSV data into RAM for use.
        /// </summary>
        /// <param name="filename">the geomData file to parse. Matching .tagsData file is assumed.</param>
        /// <returns>a list of storedOSMelements</returns>
        public static List<DbTables.Place> ReadPlaceFilesToMemory(string filename)
        {
            StreamReader srGeo = new StreamReader(filename);
            StreamReader srTags = new StreamReader(filename.Replace(".geomData", ".tagsData"));

            List<DbTables.Place> lm = new List<DbTables.Place>(8000);
            List<PlaceTags> tagsTemp = new List<PlaceTags>(8000);
            ILookup<long, PlaceTags> tagDict;

            while (!srTags.EndOfStream)
            {
                string line = srTags.ReadLine();
                PlaceTags tag = ConvertSingleTsvTag(line);
                tagsTemp.Add(tag);
            }
            srTags.Close(); srTags.Dispose();
            tagDict = tagsTemp.ToLookup(k => k.SourceItemId, v => v);

            while (!srGeo.EndOfStream)
            {
                string line = srGeo.ReadLine();
                var sw = ConvertSingleTsvPlace(line);
                sw.Tags = tagDict[sw.SourceItemID].ToList();
                lm.Add(sw);
            }
            srGeo.Close(); srGeo.Dispose();

            if (lm.Count == 0)
                Log.WriteLog("No entries for " + filename + "? why?");

            Log.WriteLog("EOF Reached for " + filename + " at " + DateTime.Now);
            return lm;
        }

        public static DbTables.Place ConvertSingleTsvPlace(string sw)
        {
            var source = sw.AsSpan();
            DbTables.Place entry = new DbTables.Place();
            entry.SourceItemID = source.SplitNext('\t').ToLong();
            entry.SourceItemType = source.SplitNext('\t').ToInt();
            entry.ElementGeometry = GeometryFromWKT(source.SplitNext('\t').ToString());
            entry.PrivacyId = Guid.Parse(source.SplitNext('\t'));
            entry.DrawSizeHint = source.ToDouble();
            entry.Tags = new List<PlaceTags>();

            return entry;
        }

        public static PlaceTags ConvertSingleTsvTag(string sw)
        {
            var source = sw.AsSpan();
            PlaceTags entry = new PlaceTags();
            entry.SourceItemId = source.SplitNext('\t').ToLong();
            entry.SourceItemType = source.SplitNext('\t').ToInt();
            entry.Key = source.SplitNext('\t').ToString();
            entry.Value = source.ToString();
            return entry;
        }

        /// <summary>
        /// Calculates an accurate distance to 2 points, in meters.
        /// </summary>
        /// <param name="p">First point</param>
        /// <param name="otherPoint">Second point</param>
        /// <returns>distance in meters between the given points on Earth</returns>
        public static double MetersDistanceTo(GeoPoint p, GeoPoint otherPoint)
        {
            return MetersDistanceTo(p.Longitude, p.Latitude, otherPoint.Longitude, otherPoint.Latitude);
        }

        public static double MetersDistanceTo(Point p, Point otherPoint) {
            return MetersDistanceTo(p.X, p.Y, otherPoint.X, otherPoint.Y);
        }

        public static double MetersDistanceTo(string plusCode1, string plusCode2) {
            return MetersDistanceTo(plusCode1.ToGeoArea().ToPoint(), plusCode2.ToGeoArea().ToPoint());
        }

        public static double MetersDistanceTo (double x1,  double y1, double x2, double y2) {
            double calcLat = Math.Sin((y2.ToRadians() - y1.ToRadians()) * 0.5);
            double calcLon = Math.Sin((x2.ToRadians() - x1.ToRadians()) * 0.5);
            double q = calcLat * calcLat + calcLon * calcLon * (Math.Cos(y2.ToRadians()) * Math.Cos(y1.ToRadians()));
            return 12734000.0 * Math.Asin(Math.Sqrt(q));
        }

        /// <summary>
        /// Returns the speed traveled between 2 points given the times the points were recorded in meters per second.
        /// </summary>
        /// <param name="point1"></param>
        /// <param name="time1"></param>
        /// <param name="point2"></param>
        /// <param name="time2"></param>
        /// <returns>speed in meters per second travelled between these 2 points</returns>
        public static double SpeedCheck(GeoPoint point1, DateTime time1, GeoPoint point2, DateTime time2)
        {
            var time = Math.Abs((time1 - time2).TotalSeconds);
            var distance = MetersDistanceTo(point1, point2);
            var speed = distance / time; //Speed is meters/second.

            return speed;
        }

        public static double SpeedCheck(Point point1, DateTime time1, Point point2, DateTime time2) {
            var time = Math.Abs((time1 - time2).TotalSeconds);
            var distance = MetersDistanceTo(point1, point2);
            var speed = distance / time; //Speed is meters/second.

            return speed;
        }
    }
}