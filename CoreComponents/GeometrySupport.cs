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

namespace PraxisCore
{
    /// <summary>
    /// Common functions revolving around Geometry object operations
    /// </summary>
    public static class GeometrySupport
    {
        //Shared class for functions that do work on Geometry objects.

        private static NetTopologySuite.IO.WKTReader geomTextReader = new NetTopologySuite.IO.WKTReader() {DefaultSRID = 4326 };
        private static PMFeatureInterpreter featureInterpreter = new PMFeatureInterpreter();

        public static GeoArea MakeBufferedGeoArea(GeoArea original, double bufferSize)
        {
            return new GeoArea(original.SouthLatitude - bufferSize, original.WestLongitude - bufferSize, original.NorthLatitude + bufferSize, original.EastLongitude + bufferSize);
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
                //can't determine orientation, because this point was shortened to an awkward line.
                return null;

            //NTS specs also requires holes in a polygon to be in clockwise order, opposite the outer shell.
            for (int i = 0; i < p.Holes.Count(); i++)
            {
                if (p.Holes[i].IsCCW)
                    p.Holes[i] = (LinearRing)p.Holes[i].Reverse();
            }

            if (p.Shell.IsCCW)
                return p;
            p = (Polygon)p.Reverse();
            if (p.Shell.IsCCW)
                return p;

            return null; //not CCW either way? Happen occasionally for some reason, and it will fail to write to a SQL Server DB. I think its related to lines crossing over each other multiple times?
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
        public static Geometry SimplifyArea(Geometry place)
        {
            if (!SimplifyAreas)
            {
                //We still do a CCWCheck here, because it's always expected to be done here as part of the process.
                //But we don't alter the geometry past that.
                if (place is Polygon)
                    place = CCWCheck((Polygon)place);
                else if (place is MultiPolygon)
                {
                    MultiPolygon mp = (MultiPolygon)place;
                    for (int i = 0; i < mp.Geometries.Count(); i++)
                    {
                        mp.Geometries[i] = CCWCheck((Polygon)mp.Geometries[i]);
                    }
                    if (mp.Geometries.Count(g => g == null) != 0)
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
                for (int i = 0; i < mp.Geometries.Count(); i++)
                {
                    mp.Geometries[i] = CCWCheck((Polygon)mp.Geometries[i]);
                }
                if (mp.Geometries.Count(g => g == null) == 0)
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
        /// Create a database StoredOsmElement from an OSMSharp Complete object.
        /// </summary>
        /// <param name="g">the CompleteOSMGeo object to prepare to save to the DB</param>
        /// <returns>the StoredOsmElement ready to save to the DB</returns>
        public static DbTables.Place ConvertOsmEntryToStoredElement(OsmSharp.Complete.ICompleteOsmGeo g)
        {
            var tags = TagParser.getFilteredTags(g.Tags);
            if (tags == null || tags.Count() == 0)
                return null; //For nodes, don't store every untagged node.

            try
            {
                var geometry = featureInterpreter.Interpret(g); 
                if (geometry == null)
                {
                    Log.WriteLog("Error: " + g.Type.ToString() + " " + g.Id + "-" + TagParser.GetPlaceName(g.Tags) + " didn't interpret into a Geometry object", Log.VerbosityLevels.Errors);
                    return null;
                }
                var sw = new DbTables.Place();
                sw.SourceItemID = g.Id;
                sw.SourceItemType = (g.Type == OsmGeoType.Relation ? 3 : g.Type == OsmGeoType.Way ? 2 : 1);
                var geo = SimplifyArea(geometry);
                if (geo == null)
                {
                    Log.WriteLog("Error: " + g.Type.ToString() + " " + g.Id + " didn't simplify for some reason.", Log.VerbosityLevels.Errors);
                    return null;
                }
                geo.SRID = 4326;//Required for SQL Server to accept data.
                sw.ElementGeometry = geo;
                sw.Tags = tags; 
                if (sw.ElementGeometry.GeometryType == "LinearRing" || (sw.ElementGeometry.GeometryType == "LineString" && sw.ElementGeometry.Coordinates.First() == sw.ElementGeometry.Coordinates.Last()))
                {
                    //I want to update all LinearRings to Polygons, and let the style determine if they're Filled or Stroked.
                    var poly = factory.CreatePolygon((LinearRing)sw.ElementGeometry);
                    sw.ElementGeometry = poly;
                }
                sw.AreaSize = sw.ElementGeometry.Length > 0 ? sw.ElementGeometry.Length : resolutionCell10;
                return sw;
            }
            catch(Exception ex)
            {
                Log.WriteLog("Error: Item " + g.Id + " failed to process. " + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Loads up TSV data into RAM for use.
        /// </summary>
        /// <param name="filename">the geomData file to parse. Matching .tagsData file is assumed.</param>
        /// <returns>a list of storedOSMelements</returns>
        public static List<DbTables.Place> ReadStoredElementsFileToMemory(string filename)
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
                var sw = ConvertSingleTsvStoredElement(line);
                sw.Tags = tagDict[sw.SourceItemID].ToList();
                lm.Add(sw);
            }
            srGeo.Close(); srGeo.Dispose();

            if (lm.Count() == 0)
                Log.WriteLog("No entries for " + filename + "? why?");

            Log.WriteLog("EOF Reached for " + filename + " at " + DateTime.Now);
            return lm;
        }

        public static DbTables.Place ConvertSingleTsvStoredElement(string sw)
        {
            var source = sw.AsSpan();
            DbTables.Place entry = new DbTables.Place();
            entry.SourceItemID = source.SplitNext('\t').ToLong();
            entry.SourceItemType = source.SplitNext('\t').ToInt();
            entry.ElementGeometry = GeometryFromWKT(source.SplitNext('\t').ToString());
            entry.AreaSize = source.SplitNext('\t').ToDouble();
            entry.PrivacyId = Guid.Parse(source);

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
    }
}