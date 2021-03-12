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

        public static void MakeAllSerializedFilesFromPBF()
        {
            List<string> filenames = System.IO.Directory.EnumerateFiles(ParserSettings.PbfFolder, "*.pbf").ToList();
            foreach (string filename in filenames)
                SerializeFilesFromPBF(filename);
        }

        public static void SerializeFilesFromPBF(string filename)
        {
            System.IO.FileInfo fi = new FileInfo(filename);
            if (ParserSettings.ForceSeparateFiles || fi.Length > ParserSettings.FilesizeSplit) //I have 28 country/state level extracts over this size, and this should include the ones that cause the most issues.
            {
                //Parse this file into area type sub-files from disk, so that I dodge hit RAM limits
                SerializeSeparateFilesFromPBF(filename);
                return;
            }

            //else parse this file all at once.
            FileStream fs = new FileStream(filename, FileMode.Open);
            byte[] fileInRam = new byte[fs.Length];
            fs.Read(fileInRam, 0, (int)fs.Length);
            MemoryStream ms = new MemoryStream(fileInRam);
            fs.Close(); fs.Dispose();

            Log.WriteLog("Checking for members in  " + filename + " at " + DateTime.Now);
            string destFilename = System.IO.Path.GetFileName(filename).Replace(".osm.pbf", "");

            Log.WriteLog("Starting " + filename + " " + " data read at " + DateTime.Now);
            var osmRelations = GetRelationsFromStream(ms, null);
            Log.WriteLog(osmRelations.Count() + " relations found", Log.VerbosityLevels.High);
            var referencedWays = osmRelations.AsParallel().SelectMany(r => r.Members.Where(m => m.Type == OsmGeoType.Way).Select(m => m.Id)).Distinct().ToHashSet();

            Log.WriteLog(referencedWays.Count() + " ways used within relations", Log.VerbosityLevels.High);
            Log.WriteLog("Relations loaded at " + DateTime.Now);
            var osmWays = GetWaysFromStream(ms, null, referencedWays);
            Log.WriteLog(osmWays.Count() + " ways found", Log.VerbosityLevels.High);
            Log.WriteLog((osmWays.Count() - referencedWays.Count()) + " standalone ways pulled in.", Log.VerbosityLevels.High);
            var referencedNodes = osmWays.AsParallel().SelectMany(m => m.Nodes).Distinct().ToHashSet();
            Log.WriteLog("Ways loaded at " + DateTime.Now);
            var osmNodes = GetNodesFromStream(ms, null, referencedNodes);
            referencedNodes = null;
            Log.WriteLog("All relevant data pulled from file at " + DateTime.Now);
            ms.Close(); ms.Dispose();
            fileInRam = null;

            var processedEntries = ProcessData(osmNodes, ref osmWays, ref osmRelations, referencedWays);
            WriteMapDataToFile(ParserSettings.JsonMapDataFolder + destFilename + "-MapData" + (ParserSettings.UseHighAccuracy ? "-highAcc" : "-lowAcc") + ".json", ref processedEntries);
            processedEntries = null;

            Log.WriteLog("Processed " + filename + " at " + DateTime.Now);
            File.Move(filename, filename + "Done"); //We made it all the way to the end, this file is done.
        }

        public static void SerializeSeparateFilesFromPBF(string filename)
        {
            //This will read from disk, since we are assuming this file will hit RAM limits if we read it all at once.
            foreach (var areatype in areaTypes.Where(a => a.AreaTypeId < 100)) //each pass takes roughly the same amount of time to read, but uses less ram. 
            {
                //skip entries if the settings say not to process them. they'll get 0 tagged entries but don't waste time reading the file.
                //ParserSettings.???

                //if (areatype.AreaName == "water")
                //  continue; //Water is too big for my PC on files this side most of the time on the 5-6 worst files. Norway.pbf can hit 39GB committed memory.
                try
                {

                    string areatypename = areatype.AreaName;
                    Log.WriteLog("Checking for " + areatypename + " members in  " + filename + " at " + DateTime.Now);
                    string destFilename = System.IO.Path.GetFileName(filename).Replace(".osm.pbf", "");

                    Log.WriteLog("Starting " + filename + " " + areatypename + " data read at " + DateTime.Now);
                    var osmRelations = GetRelationsFromPbf(filename, areatypename);
                    Log.WriteLog(osmRelations.Count() + " relations found", Log.VerbosityLevels.High);
                    var referencedWays = osmRelations.AsParallel().SelectMany(r => r.Members.Where(m => m.Type == OsmGeoType.Way).Select(m => m.Id)).Distinct().ToLookup(k => k, v => (short)0);
                    //var refWays2 = osmRelations.AsParallel().SelectMany(r => r.Members.Where(m => m.Type == OsmGeoType.Way).Select(m => m.Id)).Distinct().ToDictionary(k => k, v => (short)0);
                    Log.WriteLog(referencedWays.Count() + " ways used within relations", Log.VerbosityLevels.High);
                    Log.WriteLog("Relations loaded at " + DateTime.Now);
                    var osmWays = GetWaysFromPbf(filename, areatypename, referencedWays);
                    Log.WriteLog(osmWays.Count() + " ways found", Log.VerbosityLevels.High);
                    ////Log.WriteLog((osmWays.Count() - referencedWays.Count()) + " standalone ways pulled in.", Log.VerbosityLevels.High);
                    var referencedNodes = osmWays.AsParallel().SelectMany(m => m.Nodes).Distinct().ToLookup(k => k, v => (short)0);
                    //var referencedNodes2 = osmWays.AsParallel().SelectMany(m => m.Nodes).Distinct().ToDictionary(k => k, v => (short)0);
                    //var referencedNodes3 = osmWays.AsParallel().SelectMany(m => m.Nodes).Distinct().ToHashSet();
                    Log.WriteLog(referencedNodes.Count() + " nodes used by ways", Log.VerbosityLevels.High);
                    Log.WriteLog("Ways loaded at " + DateTime.Now);
                    var osmNodes = GetNodesFromPbf(filename, areatypename, referencedNodes); //making this by-ref able would probably be the best memory optimization i could still do.
                    referencedNodes = null;
                    Log.WriteLog("Relevant data pulled from file at " + DateTime.Now);

                    //Testing having this stream results to a file instead of making a list we write afterwards.
                    var processedEntries = ProcessData(osmNodes, ref osmWays, ref osmRelations, referencedWays, true, ParserSettings.JsonMapDataFolder + destFilename + "-MapData-" + areatypename + (ParserSettings.UseHighAccuracy ? "-highAcc" : "-lowAcc") + ".json");
                    //WriteMapDataToFile(ParserSettings.JsonMapDataFolder + destFilename + "-MapData-" + areatypename + (ParserSettings.UseHighAccuracy ? "-highAcc" : "-lowAcc") + ".json", ref processedEntries);
                    processedEntries = null;
                }
                catch (Exception ex)
                {
                    //do nothing, just recover and move on.
                    Log.WriteLog("Attempting last chance processing");
                    LastChanceSerializer(filename, areatype.AreaName);
                }
            }

            Log.WriteLog("Processed " + filename + " at " + DateTime.Now);
            File.Move(filename, filename + "Done"); //We made it all the way to the end, this file is done.
        }

        public static void LastChanceSerializer(string filename, string areaType)
        {
            //This will read from disk, since we are assuming this file will hit RAM limits if we read it all at once.
            //foreach (var areatype in areaTypes.Where(a => a.AreaTypeId < 100)) //each pass takes roughly the same amount of time to read, but uses less ram. 
            Log.WriteLog("Last Chance Mode!");
            try
            {

                int loopCount = 0;
                int loadCount = 100000; //This seems to give a peak RAM value of 8GB, which is the absolute highest I would want LastChance to go. 4GB would be better.

                string areatypename = areaType;
                Log.WriteLog("Checking for " + areatypename + " members in  " + filename + " at " + DateTime.Now);
                string destFilename = System.IO.Path.GetFileName(filename).Replace(".osm.pbf", "");
                Log.WriteLog("Starting " + filename + " " + areatypename + " data read at " + DateTime.Now);

                ILookup<long, int> usedWays = null;

                //Since this is the last chance entry, we need to be real careful about RAM use.
                //So we'll loop over the file for every 100 relations and standalone ways.
                bool loadRelations = true;
                while (loadRelations)
                {
                    var osmRelations = GetRelationsFromPbf(filename, areatypename, loadCount, loopCount * loadCount);

                    if (osmRelations.Count() < loadCount)
                        loadRelations = false;

                    usedWays = osmRelations.SelectMany(r => r.Members.Select(m => m.Id)).ToLookup(k => k, v => 0);

                    Log.WriteLog(osmRelations.Count() + " relations found", Log.VerbosityLevels.High);
                    var referencedWays = osmRelations.AsParallel().SelectMany(r => r.Members.Where(m => m.Type == OsmGeoType.Way).Select(m => m.Id)).Distinct().ToLookup(k => k, v => (short)0);
                    //var refWays2 = osmRelations.AsParallel().SelectMany(r => r.Members.Where(m => m.Type == OsmGeoType.Way).Select(m => m.Id)).Distinct().ToDictionary(k => k, v => (short)0);
                    Log.WriteLog(referencedWays.Count() + " ways used within relations", Log.VerbosityLevels.High);
                    Log.WriteLog("Relations loaded at " + DateTime.Now);
                    var osmWays = PbfOperations.GetWaysFromPbf(filename, areatypename, referencedWays, true);
                    Log.WriteLog(osmWays.Count() + " ways found", Log.VerbosityLevels.High);
                    ////Log.WriteLog((osmWays.Count() - referencedWays.Count()) + " standalone ways pulled in.", Log.VerbosityLevels.High);
                    var referencedNodes = osmWays.AsParallel().SelectMany(m => m.Nodes).Distinct().ToLookup(k => k, v => (short)0);
                    Log.WriteLog(referencedNodes.Count() + " nodes used by ways", Log.VerbosityLevels.High);
                    Log.WriteLog("Ways loaded at " + DateTime.Now);
                    var osmNodes2 = GetNodesFromPbf(filename, areatypename, referencedNodes, true); //making this by-ref able would probably be the best memory optimization i could still do.
                    referencedNodes = null;
                    Log.WriteLog("Relevant data pulled from file at " + DateTime.Now);

                    //Testing having this stream results to a file instead of making a list we write afterwards.
                    var processedEntries = PbfOperations.ProcessData(osmNodes2, ref osmWays, ref osmRelations, referencedWays, true, ParserSettings.JsonMapDataFolder + destFilename + "-MapData-" + areatypename + (ParserSettings.UseHighAccuracy ? "-highAcc" : "-lowAcc") + loopCount.ToString() + ".json");
                    processedEntries = null;
                    loopCount++;
                }
                //loopCount++;
                Log.WriteLog("Relations processed, moving on to standalone ways");

                int wayLoopCount = 0;
                bool loadWays = true;
                while (loadWays)
                {
                    var osmRelations2 = new List<Relation>();
                    ILookup<long, short> referencedWays = new List<long>().ToLookup(k => k, v => (short)0);
                    var osmWays2 = GetWaysFromPbf(filename, areatypename, referencedWays, false, wayLoopCount * loadCount, loadCount);
                    if (osmWays2.Count() < loadCount)
                        loadWays = false;

                    osmWays2 = osmWays2.Where(w => !usedWays.Contains(w.Id.Value)).ToList();

                    Log.WriteLog(osmWays2.Count() + " ways found", Log.VerbosityLevels.High);
                    var referencedNodes = osmWays2.AsParallel().SelectMany(m => m.Nodes).Distinct().ToLookup(k => k, v => (short)0);
                    Log.WriteLog(referencedNodes.Count() + " nodes used by ways", Log.VerbosityLevels.High);
                    Log.WriteLog("Ways loaded at " + DateTime.Now);
                    var osmNodes3 = GetNodesFromPbf(filename, areatypename, referencedNodes, true); //making this by-ref able would probably be the best memory optimization i could still do.
                    referencedNodes = null;
                    Log.WriteLog("Relevant data pulled from file at " + DateTime.Now);

                    //Testing having this stream results to a file instead of making a list we write afterwards.
                    var processedEntries2 = ProcessData(osmNodes3, ref osmWays2, ref osmRelations2, referencedWays, true, ParserSettings.JsonMapDataFolder + destFilename + "-MapData-" + areatypename + (ParserSettings.UseHighAccuracy ? "-highAcc" : "-lowAcc") + loopCount.ToString() + ".json");
                    processedEntries2 = null;
                    wayLoopCount++;
                    loopCount++;
                }
                //loopCount++;
                Log.WriteLog("Ways processed, moving on to standalone nodes");

                ILookup<long, short> referencedNodes2 = new List<long>().ToLookup(k => k, v => (short)0);
                ILookup<long, short> referencedWays2 = new List<long>().ToLookup(k => k, v => (short)0);
                var osmNodes4 = GetNodesFromPbf(filename, areatypename, referencedNodes2, true);
                var osmWays3 = new List<Way>();
                var osmRelations3 = new List<Relation>();
                var processedEntries3 = ProcessData(osmNodes4, ref osmWays3, ref osmRelations3, referencedWays2, true, ParserSettings.JsonMapDataFolder + destFilename + "-MapData-" + areatypename + (ParserSettings.UseHighAccuracy ? "-highAcc" : "-lowAcc") + loopCount.ToString() + ".json");
            }
            catch (Exception ex)
            {
                //do nothing, just recover and move on.
                Log.WriteLog("Exception occurred: " + ex.Message + " at " + DateTime.Now + ", moving on");
            }
            //}

            Log.WriteLog("Processed " + filename + " at " + DateTime.Now);
            File.Move(filename, filename + "Done"); //We made it all the way to the end, this file is done.
        }

        public static void WriteMapDataToFile(string filename, ref List<MapData> mapdata)
        {
            //TODO: i could probably parallelize the .Serialize() part by doing some kind of AsParallel.Select() on it.
            //but StreamWriter needs to be written from one thread. Remember that if I change this.
            System.IO.StreamWriter sw = new StreamWriter(filename);
            sw.Write("[" + Environment.NewLine);
            foreach (var md in mapdata)
            {
                if (md != null) //null can be returned from the functions that convert OSM entries to MapData
                {
                    var recordVersion = new MapDataForJson(md.name, md.place.AsText(), md.type, md.WayId, md.NodeId, md.RelationId, md.AreaTypeId);
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
            //Got out of memory errors trying to read files over 1GB through File.ReadAllText, so do those here this way.
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
                    var temp = new MapData() { name = j.name, NodeId = j.NodeId, place = reader.Read(j.place), RelationId = j.RelationId, type = j.type, WayId = j.WayId, AreaTypeId = j.AreaTypeId }; //first entry on a file before I forced the brackets onto newlines. Comma at end causes errors, is also trimmed.
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
