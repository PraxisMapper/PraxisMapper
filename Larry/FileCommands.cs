using CoreComponents;
using CoreComponents.Support;
using NetTopologySuite.Geometries;
using OsmSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using static CoreComponents.DbTables;
using static CoreComponents.Singletons;
using static Larry.PbfOperations;

namespace Larry
{
    //FileCommands is intended for functions that do some work on various file types. Processing map data from PBFs belongs to PbfOperations.
    public static class FileCommands
    {
        public static void ResetFiles(string folder)
        {
            List<string> filenames = System.IO.Directory.EnumerateFiles(folder, "*.*Done").ToList();
            foreach (var file in filenames)
            {
                File.Move(file, file.Substring(0, file.Length - 4));
            }
        }

        //public static void MakeAllSerializedFilesFromPBF()
        //{
        //    List<string> filenames = System.IO.Directory.EnumerateFiles(ParserSettings.PbfFolder, "*.pbf").ToList();
        //    foreach (string filename in filenames)
        //        SerializeFilesFromPBF(filename);
        //}

        

        public static void WriteMapDataToFile(string filename, ref List<MapData> mapdata)
        {
            System.IO.StreamWriter sw = new StreamWriter(filename);
            sw.Write("[" + Environment.NewLine);
            foreach (var md in mapdata)
            {
                if (md != null) //null can be returned from the functions that convert OSM entries to MapData
                {
                    var recordVersion = new MapDataForJson(md.name, md.place.AsText(), md.type, md.WayId, md.NodeId, md.RelationId, md.AreaTypeId, md.AreaSize);
                    var test = JsonSerializer.Serialize(recordVersion, typeof(MapDataForJson));
                    sw.Write(test);
                    sw.Write("," + Environment.NewLine);
                }
            }
            sw.Write("]");
            sw.Close();
            sw.Dispose();
            Log.WriteLog("All MapData entries were serialized individually and saved to file at " + DateTime.Now);
        }

        public static List<MapData> ReadMapDataToMemory(string filename)
        {
            StreamReader sr = new StreamReader(filename);
            List<MapData> lm = new List<MapData>();
            lm.Capacity = 100000;
            JsonSerializerOptions jso = new JsonSerializerOptions();
            jso.AllowTrailingCommas = true;

            NetTopologySuite.IO.WKTReader reader = new NetTopologySuite.IO.WKTReader();
            reader.DefaultSRID = 4326;

            while (!sr.EndOfStream)
            {
                string line = sr.ReadLine();
                if (line == "[")
                {
                    //start of a file that spaced out every entry on a newline correctly. Skip.
                }
                else if (line == "]")
                {
                    //dont do anything, this is EOF
                }
                else //The standard line
                {
                    MapDataForJson j = (MapDataForJson)JsonSerializer.Deserialize(line.Substring(0, line.Count() - 1), typeof(MapDataForJson), jso);
                    var temp = new MapData() { name = j.name, NodeId = j.NodeId, place = reader.Read(j.place), RelationId = j.RelationId, type = j.type, WayId = j.WayId, AreaTypeId = j.AreaTypeId, AreaSize = j.AreaSize }; //first entry on a file before I forced the brackets onto newlines. Comma at end causes errors, is also trimmed.
                    if (temp.place is Polygon)
                    {
                        temp.place = GeometrySupport.CCWCheck((Polygon)temp.place);
                    }
                    if (temp.place is MultiPolygon)
                    {
                        MultiPolygon mp = (MultiPolygon)temp.place;
                        for (int i = 0; i < mp.Geometries.Count(); i++)
                        {
                            mp.Geometries[i] = GeometrySupport.CCWCheck((Polygon)mp.Geometries[i]);
                        }
                        temp.place = mp;
                    }
                    lm.Add(temp);
                }
            }

            if (lm.Count() == 0)
                Log.WriteLog("No entries for " + filename + "? why?");

            sr.Close(); sr.Dispose();
            Log.WriteLog("EOF Reached for " + filename + " at " + DateTime.Now);
            return lm;
        }

    }
}
