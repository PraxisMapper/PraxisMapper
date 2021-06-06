using CoreComponents.Support;
using NetTopologySuite.Geometries;
using OsmSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using static CoreComponents.ConstantValues;
using static CoreComponents.DbTables;
using static CoreComponents.Singletons;

namespace CoreComponents
{
    public static class GeometrySupport
    {
        //Shared class for functions that do work on Geometry objects.
        //GeometryHelper is specific to Larry, and should only contain code that won't be useful outside the console app if any.

        private static NetTopologySuite.IO.WKTReader reader = new NetTopologySuite.IO.WKTReader() {DefaultSRID = 4326 };
        private static JsonSerializerOptions jso = new JsonSerializerOptions() { };
        
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

            //Sql Server Geography type requires polygon points to be in counter-clockwise order.  This function returns the polygon in the right orientation, or null if it can't.
            if (p.Shell.IsCCW)
                return p;
            p = (Polygon)p.Reverse();
            if (p.Shell.IsCCW)
                return p;

            return null; //not CCW either way? Happen occasionally for some reason, and it will fail to write to a SQL Server DB. I think its related to lines crossing over each other multiple times?
        }

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
                        return null;
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
                return null; //some of the outer shells aren't compatible. Should alert this to the user if possible.
            }
            return simplerPlace;
        }

        public static StoredOsmElement ConvertOsmEntryToStoredElement(OsmSharp.Complete.ICompleteOsmGeo g)
        {
            if (g.Tags == null || g.Tags.Count() == 0)
                return null; //For nodes, don't store every untagged node.

            try
            {
                var feature = OsmSharp.Geo.FeatureInterpreter.DefaultInterpreter.Interpret(g);
                if (feature.Count() != 1)
                {
                    Log.WriteLog("Error: " + g.Type.ToString() + " " + g.Id + " didn't return expected number of features (" + feature.Count() + ")", Log.VerbosityLevels.High);
                    return null;
                }
                var sw = new StoredOsmElement();
                sw.name = TagParser.GetPlaceName(g.Tags);
                sw.sourceItemID = g.Id;
                sw.sourceItemType = (g.Type == OsmGeoType.Relation ? 3 : g.Type == OsmGeoType.Way ? 2 : 1);
                var geo = GeometrySupport.SimplifyArea(feature.First().Geometry);
                if (geo == null)
                    return null;
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

        public static void WriteStoredElementListToFile(string filename, ref List<StoredOsmElement> mapdata)
        {
            //StreamWriter sw = new StreamWriter(filename);
            //var fs = File.OpenWrite(filename);
            List<string> results = new List<string>(mapdata.Count());
            foreach (var md in mapdata)
                if (md != null) //null can be returned from the functions that convert OSM entries to StoredElement
                {
                    var recordVersion = new StoredOsmElementForJson(md.id, md.name, md.sourceItemID, md.sourceItemType, md.elementGeometry.AsText(), string.Join("~", md.Tags.Select(t => t.Key + "|" + t.Value)), md.IsGameElement);
                    var test = JsonSerializer.Serialize(recordVersion, typeof(StoredOsmElementForJson));
                    results.Add(test);
                    //sw.WriteLine(test);
                }
            File.AppendAllLines(filename, results);
            //sw.Close();
            //sw.Dispose();
            Log.WriteLog("All StoredElement entries were serialized individually and saved to file at " + DateTime.Now, Log.VerbosityLevels.High);
        }

        public static void WriteSingleStoredElementToFile(string filename, StoredOsmElement md)
        {
            if (md != null) //null can be returned from the functions that convert OSM entries to StoredElement
            {
                var recordVersion = new CoreComponents.Support.StoredOsmElementForJson(md.id, md.name, md.sourceItemID, md.sourceItemType, md.elementGeometry.AsText(), string.Join("~", md.Tags.Select(t => t.Key + "|" + t.Value)), md.IsGameElement);
                var test = JsonSerializer.Serialize(recordVersion, typeof(CoreComponents.Support.StoredOsmElementForJson));
                File.AppendAllText(filename, test + Environment.NewLine);
            }
        }

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

        public static StoredOsmElement ConvertSingleJsonStoredElement(string sw)
        {
            StoredOsmElementForJson j = (StoredOsmElementForJson)JsonSerializer.Deserialize(sw, typeof(StoredOsmElementForJson), jso);
            var temp = new StoredOsmElement() { id = j.id, name = j.name, sourceItemID = j.sourceItemID, sourceItemType = j.sourceItemType, elementGeometry = reader.Read(j.elementGeometry), IsGameElement = j.IsGameElement, Tags = new List<ElementTags>() };
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
