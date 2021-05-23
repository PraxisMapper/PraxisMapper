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

            return null; //not CCW either way? Happen occasionally for some reason, and it will fail to write to the DB. I think its related to lines crossing over each other multiple times.
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

        public static StoredWay ConvertOsmEntryToStoredWay(OsmSharp.Complete.ICompleteOsmGeo g)
        {
            var feature = OsmSharp.Geo.FeatureInterpreter.DefaultInterpreter.Interpret(g);
            if (feature.Count() != 1)
            {
                Log.WriteLog("Error: " + g.Type.ToString() + " " + g.Id + " didn't return expected number of features (" + feature.Count() + ")", Log.VerbosityLevels.High);
                return null;
            }
            var sw = new StoredWay();
            sw.name = Place.GetPlaceName(g.Tags);
            sw.sourceItemID = g.Id;
            sw.sourceItemType = (g.Type == OsmGeoType.Relation ? 3 : g.Type == OsmGeoType.Way ? 2 : 1);
            var geo = GeometrySupport.SimplifyArea(feature.First().Geometry);
            if (geo == null)
                return null;
            geo.SRID = 4326;//Required for SQL Server to accept data this way.
            sw.wayGeometry = geo;
            sw.WayTags = TagParser.getFilteredTags(g.Tags);
            if (sw.wayGeometry.GeometryType == "LinearRing" || (sw.wayGeometry.GeometryType == "LineString" && sw.wayGeometry.Coordinates.First() == sw.wayGeometry.Coordinates.Last()))
            {
                //I want to update all LinearRings to Polygons, and let the style determine if they're Filled or Stroked.
                var poly = factory.CreatePolygon((LinearRing)sw.wayGeometry);
                sw.wayGeometry = poly;
            }
            return sw;
        }

        public static void WriteStoredWayListToFile(string filename, ref List<StoredWay> mapdata)
        {
            StreamWriter sw = new StreamWriter(filename);
            //sw.Write("[" + Environment.NewLine);
            foreach (var md in mapdata)
            {
                if (md != null) //null can be returned from the functions that convert OSM entries to MapData
                {
                    var recordVersion = new StoredWayForJson(md.id, md.name, md.sourceItemID, md.sourceItemType, md.wayGeometry.AsText(), string.Join("~", md.WayTags.Select(t => t.storedWay + "|" + t.Key + "|" + t.Value)), md.IsGameElement);
                    var test = JsonSerializer.Serialize(recordVersion, typeof(StoredWayForJson));
                    sw.WriteLine(test);
                    //sw.WriteLine("," + Environment.NewLine);
                }
            }
            //sw.Write("]");
            sw.Close();
            sw.Dispose();
            Log.WriteLog("All StoredWay entries were serialized individually and saved to file at " + DateTime.Now);
        }

        public static void WriteSingleStoredWayToFile(string filename, StoredWay md) //, bool open = false, bool close = false
        {
            if (md != null) //null can be returned from the functions that convert OSM entries to MapData
            {
                var recordVersion = new CoreComponents.Support.StoredWayForJson(md.id, md.name, md.sourceItemID, md.sourceItemType, md.wayGeometry.AsText(), string.Join("~", md.WayTags.Select(t => t.storedWay + "|" + t.Key + "|" + t.Value)), md.IsGameElement);
                var test = JsonSerializer.Serialize(recordVersion, typeof(CoreComponents.Support.StoredWayForJson));
                File.AppendAllText(filename, test + Environment.NewLine);
            }
        }

        public static List<StoredWay> ReadStoredWaysFileToMemory(string filename)
        {
            StreamReader sr = new StreamReader(filename);
            List<StoredWay> lm = new List<StoredWay>();
            lm.Capacity = 100000;
            JsonSerializerOptions jso = new JsonSerializerOptions();
            jso.AllowTrailingCommas = true;

            while (!sr.EndOfStream)
            {
                string line = sr.ReadLine();
                var sw = ConvertSingleJsonStoredWay(line);
                lm.Add(sw);
            }

            if (lm.Count() == 0)
                Log.WriteLog("No entries for " + filename + "? why?");

            sr.Close(); sr.Dispose();
            Log.WriteLog("EOF Reached for " + filename + " at " + DateTime.Now);
            return lm;
        }

        public static StoredWay ConvertSingleJsonStoredWay(string sw)
        {
            JsonSerializerOptions jso = new JsonSerializerOptions();
            jso.AllowTrailingCommas = true;
            NetTopologySuite.IO.WKTReader reader = new NetTopologySuite.IO.WKTReader();
            reader.DefaultSRID = 4326;

            StoredWayForJson j = (StoredWayForJson)JsonSerializer.Deserialize(sw.Substring(0, sw.Count() - 1), typeof(StoredWayForJson), jso);//TODO: confirm/deny that substring command is still necessary.
            var temp = new StoredWay() { id = j.id, name = j.name, sourceItemID = j.sourceItemID, sourceItemType = j.sourceItemType, wayGeometry = reader.Read(j.wayGeometry), IsGameElement = j.IsGameElement };
            if (!string.IsNullOrWhiteSpace(j.WayTags))
            {
                var tagData = j.WayTags.Split("~");
                if (tagData.Count() > 0)
                {
                    foreach (var tag in tagData)
                    {
                        var elements = tag.Split("|");
                        WayTags wt = new WayTags();
                        wt.storedWay = temp;
                        wt.Key = elements[1];
                        wt.Value = elements[2];
                    }
                }
            }

            if (temp.wayGeometry is Polygon)
            {
                temp.wayGeometry = GeometrySupport.CCWCheck((Polygon)temp.wayGeometry);
            }
            if (temp.wayGeometry is MultiPolygon)
            {
                MultiPolygon mp = (MultiPolygon)temp.wayGeometry;
                for (int i = 0; i < mp.Geometries.Count(); i++)
                {
                    mp.Geometries[i] = GeometrySupport.CCWCheck((Polygon)mp.Geometries[i]);
                }
                temp.wayGeometry = mp;
            }
            return temp;
        }
    }
}
