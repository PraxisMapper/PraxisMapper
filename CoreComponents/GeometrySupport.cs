using PraxisCore.Support;
using NetTopologySuite.Geometries;
using OsmSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using static PraxisCore.ConstantValues;
using static PraxisCore.DbTables;
using static PraxisCore.Singletons;

namespace PraxisCore
{
    public static class GeometrySupport
    {
        //Shared class for functions that do work on Geometry objects.

        private static NetTopologySuite.IO.WKTReader reader = new NetTopologySuite.IO.WKTReader() {DefaultSRID = 4326 };
        private static JsonSerializerOptions jso = new JsonSerializerOptions() { };
        private static PMFeatureInterpreter featureInterpreter = new PMFeatureInterpreter();
        
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

            //SQL Server also requires holes in a polygon to be in clockwise order, opposite the outer shell.
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
            return reader.Read(elementGeometry);
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
                        //return null; //Used to return null. What if we attempt to work with the data present?
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
            var simplerPlace = NetTopologySuite.Simplify.TopologyPreservingSimplifier.Simplify(place, resolutionCell10); //This cuts storage space for files by 30-50%
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
            //}
            //return simplerPlace;
        }

        /// <summary>
        /// Create a database StoredOsmElement from an OSMSharp Complete object.
        /// </summary>
        /// <param name="g">the CompleteOSMGeo object to prepare to save to the DB</param>
        /// <returns>the StoredOsmElement ready to save to the DB</returns>
        public static StoredOsmElement ConvertOsmEntryToStoredElement(OsmSharp.Complete.ICompleteOsmGeo g)
        {
            if (g.Tags == null || g.Tags.Count() == 0)
                return null; //For nodes, don't store every untagged node.

            try
            {
                var geometry = featureInterpreter.Interpret(g); 
                if (geometry == null)
                {
                    Log.WriteLog("Error: " + g.Type.ToString() + " " + g.Id + " didn't interpret into a Geometry object", Log.VerbosityLevels.High);
                    return null;
                }
                var sw = new StoredOsmElement();
                sw.name = TagParser.GetPlaceName(g.Tags);
                sw.sourceItemID = g.Id;
                sw.sourceItemType = (g.Type == OsmGeoType.Relation ? 3 : g.Type == OsmGeoType.Way ? 2 : 1);
                var geo = GeometrySupport.SimplifyArea(geometry);
                if (geo == null)
                {
                    Log.WriteLog("Error: " + g.Type.ToString() + " " + g.Id + " didn't simplify for some reason.", Log.VerbosityLevels.High);
                    return null;
                }
                geo.SRID = 4326;//Required for SQL Server to accept data this way.
                sw.elementGeometry = geo;
                sw.Tags = TagParser.getFilteredTags(g.Tags);
                if (sw.elementGeometry.GeometryType == "LinearRing" || (sw.elementGeometry.GeometryType == "LineString" && sw.elementGeometry.Coordinates.First() == sw.elementGeometry.Coordinates.Last()))
                {
                    //I want to update all LinearRings to Polygons, and let the style determine if they're Filled or Stroked.
                    var poly = factory.CreatePolygon((LinearRing)sw.elementGeometry);
                    sw.elementGeometry = poly;
                }
                sw.AreaSize = sw.elementGeometry.Length;
                return sw;
            }
            catch(Exception ex)
            {
                Log.WriteLog("Error: Item " + g.Id + " failed to process. " + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Dump a list of Stored OSM Elements to a JSON file.
        /// </summary>
        /// <param name="filename"> path to save data to</param>
        /// <param name="mapdata">the list of elements to save</param>
        public static void WriteStoredElementListToFile(string filename, ref List<StoredOsmElement> mapdata)
        {
            List<string> results = new List<string>(mapdata.Count());
            foreach (var md in mapdata)
                if (md != null) //null can be returned from the functions that convert OSM entries to StoredElement
                {
                    var recordVersion = new StoredOsmElementForJson(md.id, md.name, md.sourceItemID, md.sourceItemType, md.elementGeometry.AsText(), string.Join("~", md.Tags.Select(t => t.Key + "|" + t.Value)), md.IsGameElement, md.IsUserProvided, md.IsGenerated);
                    var test = JsonSerializer.Serialize(recordVersion, typeof(StoredOsmElementForJson));
                    results.Add(test);
                }
            File.AppendAllLines(filename, results);
            Log.WriteLog("All StoredElement entries were serialized individually and saved to file at " + DateTime.Now, Log.VerbosityLevels.High);
        }

        /// <summary>
        /// Add one stored OSM element to the end of a file.
        /// </summary>
        /// <param name="filename">path to save data to</param>
        /// <param name="md">the element to save</param>
        public static void WriteSingleStoredElementToFile(string filename, StoredOsmElement md)
        {
            if (md != null) //null can be returned from the functions that convert OSM entries to StoredElement
            {
                var recordVersion = new PraxisCore.Support.StoredOsmElementForJson(md.id, md.name, md.sourceItemID, md.sourceItemType, md.elementGeometry.AsText(), string.Join("~", md.Tags.Select(t => t.Key + "|" + t.Value)), md.IsGameElement,md.IsUserProvided, md.IsGenerated);
                var test = JsonSerializer.Serialize(recordVersion, typeof(PraxisCore.Support.StoredOsmElementForJson));
                File.AppendAllText(filename, test + Environment.NewLine);
            }
        }

        /// <summary>
        /// Loads up JSON data into RAM for use.
        /// </summary>
        /// <param name="filename">the JSON file to parse. </param>
        /// <returns>a list of storedOSMelements</returns>
        public static List<StoredOsmElement> ReadStoredElementsFileToMemory(string filename)
        {
            StreamReader sr = new StreamReader(filename);
            List<StoredOsmElement> lm = new List<StoredOsmElement>();
            lm.Capacity = 100000;
            JsonSerializerOptions jso = new JsonSerializerOptions();
            jso.AllowTrailingCommas = true;

            while (!sr.EndOfStream)
            {
                string line = sr.ReadLine();
                var sw = ConvertSingleJsonStoredElement(line);
                lm.Add(sw);
            }

            if (lm.Count() == 0)
                Log.WriteLog("No entries for " + filename + "? why?");

            sr.Close(); sr.Dispose();
            Log.WriteLog("EOF Reached for " + filename + " at " + DateTime.Now);
            return lm;
        }

        /// <summary>
        /// Turns a JSON string into a StoredOSMElement
        /// </summary>
        /// <param name="sw">the json string to parse</param>
        /// <returns>the StoredOsmElement</returns>
        public static StoredOsmElement ConvertSingleJsonStoredElement(string sw)
        {
            StoredOsmElementForJson j = (StoredOsmElementForJson)JsonSerializer.Deserialize(sw, typeof(StoredOsmElementForJson), jso);
            var temp = new StoredOsmElement() { id = j.id, name = j.name, sourceItemID = j.sourceItemID, sourceItemType = j.sourceItemType, elementGeometry = reader.Read(j.elementGeometry), IsGameElement = j.IsGameElement, Tags = new List<ElementTags>(), IsGenerated = j.isGenerated, IsUserProvided = j.isUserProvided };
            if (!string.IsNullOrWhiteSpace(j.WayTags))
            {
                var tagData = j.WayTags.Split("~");
                if (tagData.Count() > 0)
                    foreach (var tag in tagData)
                    {
                        var elements = tag.Split("|");
                        if (elements.Length == 2)
                        {
                            ElementTags wt = new ElementTags();
                            wt.storedOsmElement = temp;
                            wt.Key = elements[0];
                            wt.Value = elements[1];
                            temp.Tags.Add(wt);
                        }
                    }
            }

            if (temp.elementGeometry is Polygon)
                temp.elementGeometry = GeometrySupport.CCWCheck((Polygon)temp.elementGeometry);

            if (temp.elementGeometry is MultiPolygon)
            {
                MultiPolygon mp = (MultiPolygon)temp.elementGeometry;
                for (int i = 0; i < mp.Geometries.Count(); i++)
                {
                    mp.Geometries[i] = GeometrySupport.CCWCheck((Polygon)mp.Geometries[i]);
                }
                temp.elementGeometry = mp;
            }
            temp.AreaSize = temp.elementGeometry.Length;
            return temp;
        }
    }
}
