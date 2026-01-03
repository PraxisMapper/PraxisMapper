using Google.OpenLocationCode;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using NetTopologySuite.Geometries;
using OsmSharp.IO.PBF;
using PraxisCore.Support;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PraxisCore
{
    //TODO: offline data improvements to consider:
    // Styles include the name of each entry, which makes editing offline entries easier.
    // Do offline items get the privacyID, so that a client downloading new data can apply existing changes to new data correctly?
    // (That Id is the OSM ID plus type. That's the proper unique connection, and its offline so it won't expose data to other players)
    // Odds are high that Larry will use the OfflinePlaces table, as its the bulk processor app, and PraxisMapper will use Places as normal.

    //May want to see if I can make a global nametable file, that assigns each word a number/ID, and then have the offline data nametable
    //refer to that instead of saving each name separately in them. This might be better for the super-minimized entries, but I should check
    //and see if it reduces the output size. Plus, is it smaller in text to store name as strings or actual int array? In JSON, might be string.
    //name = "12 244 19" is 9 bytes if UTF-8 collapses to ASCII correctly, 3 int32s in memory are 12 bytes.
    public class OfflineData
    {
        static Lock zipLock = new Lock();
        public class OfflineDataV2
        {
            public string olc { get; set; } //PlusCode
            public double dateGenerated { get; set; } // UTC in Unix time, easier for Godot to work with.
            public int version { get; set; } = 2; // absence/null indicates version 1. V2 added OsmId.
            public Dictionary<string, List<OfflinePlaceEntry>> entries { get; set; }
            public Dictionary<int, string> nameTable { get; set; } //id, name
        }

        public class OfflinePlaceEntry
        {
            public int? nid { get; set; } = null; //nametable id
            public int tid { get; set; } //terrain id, which style entry this place is
            public int gt { get; set; } //geometry type. 1 = point, 2 = line OR hollow shape, 3 = filled shape.
            public string p { get; set; } //Points, local to the given PlusCode. If human-readable, is string pairs, if not is base64 encoded integers.
            public double? s { get; set; } //size, Removed after sorting.
            public int? lo { get; set; } //layer order.
            public long OsmId { get; set; } // can combine with gt to uniquely identify this item
            public string name { get; set; } //Meant to be used internally for sorting. For maximum space efficiency, should be nulled out before saving.
        }

        public class OfflineDataV2Min//Still a Cell6 to draw, but minimized as much as possible.
        {
            public string olc { get; set; } //PlusCode
            public Dictionary<string, List<MinOfflineData>> entries { get; set; }
            public Dictionary<int, string> nameTable { get; set; } //id, name
        }

        public class MinOfflineData
        {
            public string c { get; set; } //Point Center, as pixel coords
            public int r { get; set; }  //radius for a circle representing roughly the place, in pixels on the client image (1 Cell12)
            public int? nid { get; set; } = null; //nametable id, as regular offline data.
            public int tid { get; set; } //terrain id, which style entry this place is

        }

        public static double simplifyRes = 0.0000078125; //default = cell12Lat
        public static double xRes = 0.0000078125; //default = cell12Lon
        public static double yRes = 0.000005; //default = cell12Lat
        public static string[] styles = ["suggestedmini", "adminBoundsFilled"];
        public static string filePath = "";

        public static void MakeOfflineJson(string plusCode, Polygon bounds = null, bool saveToFile = true, ZipArchive inner_zip = null, List<DbTables.Place> places = null)
        {
            //Make offline data for PlusCode6s, repeatedly if the one given is a 4 or 2.
            if (bounds == null)
            {
                var dbB = new PraxisContext();
                var settings = dbB.ServerSettings.FirstOrDefault();
                bounds = new GeoArea(settings.SouthBound, settings.WestBound, settings.NorthBound, settings.EastBound).ToPolygon();
                dbB.Dispose();
            }

            var area = plusCode.ToPolygon();
            if (!area.Intersects(bounds))
                return;

            if (plusCode.Length < 6)
            {
                if (!PraxisCore.Place.DoPlacesExist(plusCode.ToGeoArea(), places))
                    return;

                if (plusCode.Length == 4)
                {
                    var folder2 = string.Concat(filePath, plusCode.AsSpan(0, 2));
                    var file = string.Concat(folder2, "\\", plusCode.AsSpan(0, 4), ".zip");
                    Directory.CreateDirectory(folder2);
                    if (!File.Exists(file))
                    {
                        inner_zip = new ZipArchive(File.Create(file), ZipArchiveMode.Update);
                    }
                    else
                        inner_zip = ZipFile.Open(file, ZipArchiveMode.Update);

                    try
                    {
                        //Far future TODO: Work out out to start loading the next set of places from the DB while processing data for the current set
                        //OR find a way to consistently improve MariaDB performance reading places where there's huge ones.
                        Console.WriteLine("Loading places for " + plusCode);
                        Stopwatch load = Stopwatch.StartNew();
                        places = Place.GetPlaces(plusCode.ToGeoArea().PadGeoArea(ConstantValues.resolutionCell8), skipTags: true);
                        //For the really big areas, if we crop it once here, should save about 3 minutes of processing later.
                        foreach (var place in places)
                            place.ElementGeometry = place.ElementGeometry.Intersection(area);
                        load.Stop();
                        Console.WriteLine("Places for " + plusCode + " loaded in " + load.Elapsed);
                    }
                    catch (Exception ex)
                    {
                        //Do nothing, we'll load places up per Cell6 if we can't pull the whole Cell4 into RAM.
                        Console.WriteLine("Places for " + plusCode + " wouldn't load, doing it per Cell6");
                        places = null;
                    }

                    //This block isnt limited by IO, and will suffer from swapping threads constantly.
                    ParallelOptions po = new ParallelOptions() { MaxDegreeOfParallelism = Environment.ProcessorCount };
                    Parallel.ForEach(ConstantValues.GetCellCombos(), po, pair =>
                    {
                        MakeOfflineJson(plusCode + pair, bounds, saveToFile, inner_zip, places);
                    });

                    bool removeFile = false;
                    if (inner_zip.Entries.Count == 0)
                        removeFile = true;

                    if (inner_zip != null)
                        inner_zip.Dispose();

                    if (removeFile)
                        File.Delete(filePath + plusCode.Substring(0, 2) + "\\" + plusCode.Substring(0, 4) + ".zip");

                    return;
                }
                else
                {
                    var doneCell2s = File.ReadAllText("lastOfflineEntry.txt");
                    if (doneCell2s.Contains(plusCode) && plusCode != "")
                        return;

                    foreach (var pair in ConstantValues.GetCellCombos())
                        MakeOfflineJson(plusCode + pair, bounds, saveToFile, inner_zip, places);
                    File.AppendAllText("lastOfflineEntry.txt", "|" + plusCode);
                    return;
                }
            }

            if (plusCode.Length == 2)
            {
                var folder = string.Concat(filePath, plusCode); //.Substring(0, 2)
                Directory.CreateDirectory(folder);
            }

            var sw = Stopwatch.StartNew();
            var finalData = MakeEntries(plusCode, string.Join(",", styles), places);
            if (finalData == null || (finalData.nameTable == null && finalData.entries.Count == 0))
                return;

            JsonSerializerOptions jso = new JsonSerializerOptions();
            jso.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
            string data = JsonSerializer.Serialize(finalData, jso);

            if (saveToFile)
            {
                lock (zipLock)
                {
                    Stream entryStream;
                    var entry = inner_zip.GetEntry(plusCode + ".json");
                    if (entry != null)
                    {
                        entryStream = entry.Open();
                        OfflineDataV2 existingData = JsonSerializer.Deserialize<OfflineDataV2>(entryStream);
                        var dataSize = data.Length;
                        finalData = MergeOfflineFiles(finalData, existingData);
                        data = JsonSerializer.Serialize(finalData, jso); //Need to re-serialize it here, THATS the issue.
                        if (data.Length < dataSize)
                        {
                            Debugger.Break();
                            Console.WriteLine("Data got smaller after merging! check this out!");
                        }

                        entryStream.Position = 0;
                        entryStream.SetLength(data.Length);
                    }
                    else
                    {
                        entry = inner_zip.CreateEntry(plusCode + ".json", CompressionLevel.Optimal);
                        entryStream = entry.Open();
                    }

                    using (var streamWriter = new StreamWriter(entryStream))
                        streamWriter.Write(data);
                    entryStream.Close();
                    entryStream.Dispose();
                }

                sw.Stop();
                Log.WriteLog("Created and saved offline data for " + plusCode + " in " + sw.Elapsed);
            }
            else
            {
                GenericData.SetAreaData(plusCode, "offlineV2", data);
            }
        }

        public static void MakeOfflineJsonFromOfflineTable(string plusCode, Polygon bounds = null, bool saveToFile = true, ZipArchive inner_zip = null, List<DbTables.OfflinePlace> places = null, IEnumerable<(int, string)> nameHashes = null)
        {
            //Make offline data for PlusCode6s, repeatedly if the one given is a 4 or 2.
            if (bounds == null)
            {
                var dbB = new PraxisContext();
                var settings = dbB.ServerSettings.FirstOrDefault();
                bounds = new GeoArea(settings.SouthBound, settings.WestBound, settings.NorthBound, settings.EastBound).ToPolygon();
                dbB.Dispose();
            }

            var area = plusCode.ToPolygon();
            if (!area.Intersects(bounds))
                return;

            if (plusCode.Length < 6)
            {
                if (!PraxisCore.Place.DoOfflinePlacesExist(plusCode.ToGeoArea(), places))
                    return;

                if (plusCode.Length == 4)
                {
                    var folder2 = string.Concat(filePath, plusCode.AsSpan(0, 2));
                    var file = string.Concat(folder2, "\\", plusCode.AsSpan(0, 4), ".zip");
                    Directory.CreateDirectory(folder2);
                    if (!File.Exists(file))
                    {
                        inner_zip = new ZipArchive(File.Create(file), ZipArchiveMode.Update);
                    }
                    else
                    {
                        try
                        {
                            inner_zip = ZipFile.Open(file, ZipArchiveMode.Update);
                        }
                        catch (Exception ex)
                        {
                            //file is probably corrupt, delete it.
                            Log.WriteLog("File " + file + " could not be opened, assuming corrupt. Deleting and recreating");
                            File.Delete(file);
                            inner_zip = new ZipArchive(File.Create(file), ZipArchiveMode.Update);
                        }
                    }

                    try
                    {
                        //Far future TODO: Work out out to start loading the next set of places from the DB while processing data for the current set
                        //OR find a way to consistently improve MariaDB performance reading places where there's huge ones.
                        Console.WriteLine("Loading places for " + plusCode);
                        Stopwatch load = Stopwatch.StartNew();
                        places = Place.GetOfflinePlaces(plusCode.ToGeoArea().PadGeoArea(ConstantValues.resolutionCell8));
                        nameHashes = MakeNameTablePieces(places.Where(p => p.Name != null).Select(p => p.Name).ToList()).Index();
                        var nameDict = nameHashes.ToDictionary(k => k.Item1.ToString(), v => v.Item2);

                        //Write nametable to zip first.
                        Stream nameStream;
                        var namedata = JsonSerializer.Serialize(nameDict); //TODO: get this to output reasonably shaped data instead of nothings.
                        var nameEntry = inner_zip.GetEntry("nametable.json");
                        if (nameEntry != null)
                        {
                            nameStream = nameEntry.Open();
                            
                            nameStream.Position = 0;
                            nameStream.SetLength(namedata.Length);
                        }
                        else
                        {
                            nameEntry = inner_zip.CreateEntry("nametable.json", CompressionLevel.Optimal);
                            nameStream = nameEntry.Open();
                        }
                        using (var streamWriter = new StreamWriter(nameStream))
                            streamWriter.Write(namedata);
                        nameStream.Close();
                        nameStream.Dispose();

                        //For the really big areas, if we crop it once here, should save about 3 minutes of processing later.
                        //foreach (var place in places)
                        Parallel.ForEach(places, (place) => place.ElementGeometry = place.ElementGeometry.Intersection(area));
                        load.Stop();
                        Console.WriteLine("Places for " + plusCode + " loaded in " + load.Elapsed);
                    }
                    catch (Exception ex)
                    {
                        //Do nothing, we'll load places up per Cell6 if we can't pull the whole Cell4 into RAM.
                        Console.WriteLine("Places for " + plusCode + " wouldn't load, doing it per Cell6");
                        places = null;
                    }

                    //This block isnt limited by IO, and will suffer from swapping threads constantly.
                    ParallelOptions po = new ParallelOptions() { MaxDegreeOfParallelism = Environment.ProcessorCount };
                    Parallel.ForEach(ConstantValues.GetCellCombos(), po, pair =>
                    {
                        MakeOfflineJsonFromOfflineTable(plusCode + pair, bounds, saveToFile, inner_zip, places, nameHashes);
                    });

                    bool removeFile = false;
                    if (inner_zip.Entries.Count == 0)
                        removeFile = true;

                    if (inner_zip != null)
                        inner_zip.Dispose();

                    if (removeFile)
                        File.Delete(filePath + plusCode.Substring(0, 2) + "\\" + plusCode.Substring(0, 4) + ".zip");

                    return;
                }
                else
                {
                    var doneCell2s = File.ReadAllText("lastOfflineEntry.txt");
                    if (doneCell2s.Contains(plusCode) && plusCode != "")
                        return;

                    foreach (var pair in ConstantValues.GetCellCombos())
                        MakeOfflineJsonFromOfflineTable(plusCode + pair, bounds, saveToFile, inner_zip, places, nameHashes);
                    File.AppendAllText("lastOfflineEntry.txt", "|" + plusCode);
                    return;
                }
            }

            if (plusCode.Length == 2)
            {
                var folder = string.Concat(filePath, plusCode); //.Substring(0, 2)
                Directory.CreateDirectory(folder);
            }

            var sw = Stopwatch.StartNew();
            //TODO: this needs to use the name hashes index.
            var finalData = MakeEntriesFromOffline(plusCode, string.Join(",", styles), places, nameHashes);
            if (finalData == null || (finalData.nameTable == null && finalData.entries.Count == 0))
                return;

            JsonSerializerOptions jso = new JsonSerializerOptions();
            jso.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
            string data = JsonSerializer.Serialize(finalData, jso);
            
            if (saveToFile)
            {
                lock (zipLock)
                {
                    Stream entryStream;
                    var entry = inner_zip.GetEntry(plusCode + ".json");
                    if (entry != null)
                    {
                        entryStream = entry.Open();
                        OfflineDataV2 existingData = JsonSerializer.Deserialize<OfflineDataV2>(entryStream);
                        var dataSize = data.Length;
                        finalData = MergeOfflineFiles(finalData, existingData);
                        data = JsonSerializer.Serialize(finalData, jso); //Need to re-serialize it here, THATS the issue.
                        if (data.Length < dataSize)
                        {
                            Debugger.Break();
                            Console.WriteLine("Data got smaller after merging! check this out!");
                        }

                        entryStream.Position = 0;
                        entryStream.SetLength(data.Length);
                    }
                    else
                    {
                        entry = inner_zip.CreateEntry(plusCode + ".json", CompressionLevel.Optimal);
                        entryStream = entry.Open();
                    }

                    using (var streamWriter = new StreamWriter(entryStream))
                        streamWriter.Write(data);
                    entryStream.Close();
                    entryStream.Dispose();
                }

                sw.Stop();
                Log.WriteLog("Created and saved offline data for " + plusCode + " in " + sw.Elapsed);
            }
            else
            {
                GenericData.SetAreaData(plusCode, "offlineV2", data);
            }
        }

        public static OfflineDataV2 MakeEntries(string plusCode, string stylesToUse, List<DbTables.Place> places = null)
        {
            var counter = 1;
            try
            {
                var cell = plusCode.ToGeoArea();
                var area = plusCode.ToPolygon();
                var styles = stylesToUse.Split(",");

                //Adding variables here so that an instance can process these at higher or lower accuracy if desired. Higher accuracy or not simplifying items
                //will make larger files but the output would be a closer match to the server's images.

                var min = cell.Min;
                Dictionary<string, int> nametable = new Dictionary<string, int>(); //name, id
                var nameIdCounter = 0;

                if (!PraxisCore.Place.DoPlacesExist(cell, places))
                    return null;

                var finalData = new OfflineDataV2();
                finalData.olc = plusCode;
                finalData.entries = new Dictionary<string, List<OfflinePlaceEntry>>();
                foreach (var style in styles)
                {
                    var placeData = PraxisCore.Place.GetPlaces(cell, source: places, styleSet: style, dataKey: style, skipTags: true);

                    if (placeData.Count == 0)
                        continue;
                    ConcurrentBag<OfflinePlaceEntry> entries = new ConcurrentBag<OfflinePlaceEntry>();
                    var names = placeData.Where(p => !string.IsNullOrWhiteSpace(p.Name) && !nametable.ContainsKey(p.Name)).Select(p => p.Name).Distinct();
                    foreach (var name in names)
                        nametable.Add(name, ++nameIdCounter);

                    //foreach (var place in placeData)
                    Parallel.ForEach(placeData, (place) => //we save data and re-order stuff after.
                    {
                        var sizeOrder = place.DrawSizeHint;
                        var geo = place.ElementGeometry.Intersection(area);
                        if (simplifyRes > 0)
                            geo = geo.Simplify(simplifyRes);
                        if (geo.IsEmpty)
                            return; // continue; //Probably an element on the border thats getting pulled in by buffer.

                        int? nameID = null;
                        if (!string.IsNullOrWhiteSpace(place.Name))
                        {
                            if (nametable.TryGetValue(place.Name, out var nameval))
                                nameID = nameval;
                        }

                        var styleEntry = TagParser.allStyleGroups[style][place.StyleName];

                        //I'm locking these geometry items to a tile, So I convert these points in the geometry to integers, effectively
                        //letting me draw Cell11 pixel-precise points from this info, and is shorter stringified for JSON vs floats/doubles.
                        var coordSets = GetCoordEntries(geo, cell.Min, xRes, yRes); //Original human-readable strings
                        foreach (var coordSet in coordSets)
                        {
                            if (coordSet == "")
                                //if (coordSet.Count == 0)
                                continue;

                            var offline = new OfflinePlaceEntry();
                            offline.nid = nameID;
                            offline.tid = styleEntry.MatchOrder; //Client will need to know what this ID means from the offline style endpoint output.

                            offline.gt = geo.GeometryType == "Point" ? 1 : geo.GeometryType == "LineString" ? 2 : styleEntry.PaintOperations.All(p => p.FillOrStroke == "stroke") ? 2 : 3;
                            offline.p = coordSet;
                            offline.s = sizeOrder;
                            offline.lo = styleEntry.PaintOperations.Min(p => p.LayerId);
                            offline.OsmId = place.SourceItemID;
                            entries.Add(offline);
                        }
                    });
                    //TODO: determine why one south america place was null.
                    //Smaller number layers get drawn first, and bigger places get drawn first.
                    finalData.entries[style] = entries.Where(e => e != null).OrderBy(e => e.lo).ThenByDescending(e => e.s).ToList();
                    foreach (var e in finalData.entries[style])
                    {
                        //Dont save this to the output file.
                        e.s = null;
                        e.lo = null;
                    }
                }

                if (finalData.entries.Count == 0)
                    return null;

                finalData.nameTable = nametable.Count > 0 ? nametable.ToDictionary(k => k.Value, v => v.Key) : null;

                finalData.dateGenerated = DateTime.UtcNow.ToUnixTime();
                return finalData;
            }
            catch (MySqlException ex1)
            {
                counter++;
                System.Threading.Thread.Sleep(1000 * counter);
                return MakeEntries(plusCode, stylesToUse, places);
            }
            catch (Exception ex)
            {
                Log.WriteLog("Caught unexpected error: " + ex.Message);
                return null;
            }
        }

        //This is for writing directly to offline format from a pbf. This expects to be run on each individual place for one Cell6 at a time.
        //RIght now, names will either need passed in or handled before/after this call.
        public static List<OfflinePlaceEntry> MakeEntriesForOnePlace(string plusCode, string style, FundamentalOsm place)
        {
            //var counter = 1;
            try
            {
                var cell = plusCode.ToGeoArea();
                var area = plusCode.ToPolygon();

                //var styles = stylesToUse.Split(",");

                //Adding variables here so that an instance can process these at higher or lower accuracy if desired. Higher accuracy or not simplifying items
                //will make larger files but the output would be a closer match to the server's images.

                var min = cell.Min;
                Dictionary<string, int> nametable = new Dictionary<string, int>(); //name, id
                var nameIdCounter = 0;

                
                //if this style doesn't match this place, skip this
                var styleData = TagParser.GetStyleEntry(place.tags, style);
                if (styleData.Name == TagParser.defaultStyle.Name)
                {
                    return null;
                }
                List<OfflinePlaceEntry> entries = new List<OfflinePlaceEntry>();
                
                var name = TagParser.GetName(place.tags);
                var geo = GeometrySupport.ConvertFundamentalOsmToOfflineV2Entry(place, plusCode, style);
                if (geo == null)
                    return null;

                int? nameID = null;
                if (!string.IsNullOrWhiteSpace(name))
                {
                    if (nametable.TryGetValue(name, out var nameval))
                        nameID = nameval;
                }

                foreach (var e in geo)
                    e.nid = nameID;

                return geo;
            }
            catch (Exception ex)
            {
                Log.WriteLog("Caught unexpected error: " + ex.Message);
                return null;
            }
        }

        //NOTE: Take any optimizations made here, and apply to the MakeEntries function so they stay on par.
        public static OfflineDataV2 MakeEntriesFromOffline(string plusCode, string stylesToUse, List<DbTables.OfflinePlace> places = null, IEnumerable<(int, string)> nameData = null)
        {
            var counter = 1;
            try
            {
                var cell = plusCode.ToGeoArea();
                var area = plusCode.ToPolygon();

                var styles = stylesToUse.Split(",");

                //Adding variables here so that an instance can process these at higher or lower accuracy if desired. Higher accuracy or not simplifying items
                //will make larger files but the output would be a closer match to the server's images.

                var min = cell.Min;
                Dictionary<string, int> nametable = new Dictionary<string, int>(); //name, id
                var nameIdCounter = 0;

                if (!PraxisCore.Place.DoOfflinePlacesExist(cell, places))
                    return null;

                var finalData = new OfflineDataV2();
                finalData.olc = plusCode;
                finalData.version = 3;
                finalData.entries = new Dictionary<string, List<OfflinePlaceEntry>>();
                foreach (var style in styles)
                {
                    var placeData = PraxisCore.Place.GetOfflinePlaces(cell, source: places);

                    if (placeData.Count == 0)
                        continue;
                    ConcurrentBag<OfflinePlaceEntry> entries = new ConcurrentBag<OfflinePlaceEntry>();
                    var names = placeData.Where(p => !string.IsNullOrWhiteSpace(p.Name) && !nametable.ContainsKey(p.Name)).Select(p => p.Name).Distinct();
                    foreach (var name in names)
                        nametable.Add(name, ++nameIdCounter);

                    //foreach (var place in placeData)
                    Parallel.ForEach(placeData, (place) => //we save data and re-order stuff after.
                    {
                        var sizeOrder = place.DrawSizeHint;
                        var geo = place.ElementGeometry.Intersection(area);
                        if (simplifyRes > 0)
                            geo = geo.Simplify(simplifyRes);
                        if (geo.IsEmpty)
                            return; // continue; //Probably an element on the border thats getting pulled in by buffer.

                        int nameID = 0;
                        if (place.Name != null)
                        {
                            nametable.TryGetValue(place.Name, out nameID);
                        }

                        var styleEntry = TagParser.allStyleGroups[style][place.StyleName];

                        //I'm locking these geometry items to a tile, So I convert these points in the geometry to integers, effectively
                        //letting me draw Cell11 pixel-precise points from this info, and is shorter stringified for JSON vs floats/doubles.
                        var coordSets = GetCoordEntries(geo, cell.Min, xRes, yRes); //Original human-readable strings
                        foreach (var coordSet in coordSets)
                        {
                            if (coordSet == "")
                                continue;

                            var offline = new OfflinePlaceEntry();
                            //offline.nid = nameID;
                            if (!string.IsNullOrWhiteSpace(place.Name))
                            {
                                var nameBits = place.Name.Split(' ');
                                var name = "";

                                foreach(var nb in nameBits)
                                    name += nameData.First(n => n.Item2 == nb).Item1 + " ";

                                offline.name = name.Trim();
                            }
                            offline.tid = styleEntry.MatchOrder; //Client will need to know what this ID means from the offline style endpoint output.

                            offline.gt = geo.GeometryType == "Point" ? 1 : geo.GeometryType == "LineString" ? 2 : styleEntry.PaintOperations.All(p => p.FillOrStroke == "stroke") ? 2 : 3;
                            offline.p = coordSet;
                            offline.s = sizeOrder;
                            offline.lo = styleEntry.PaintOperations.Min(p => p.LayerId);
                            offline.OsmId = place.SourceItemID;
                            entries.Add(offline);
                        }
                    });
                    //TODO: determine why one south america place was null.
                    //Smaller number layers get drawn first, and bigger places get drawn first.
                    finalData.entries[style] = entries.OrderBy(e => e.lo).ThenByDescending(e => e.s).ToList();
                    foreach (var e in finalData.entries[style])
                    {
                        //Dont save this to the output file.
                        e.s = null;
                        e.lo = null;
                    }
                }

                if (finalData.entries.Count == 0)
                    return null;

                //finalData.nameTable = nametable.Count > 0 ? nametable.ToDictionary(k => k.Value, v => v.Key) : null;

                finalData.dateGenerated = DateTime.UtcNow.ToUnixTime();
                return finalData;
            }
            catch (MySqlException ex1)
            {
                counter++;
                System.Threading.Thread.Sleep(1000 * counter);
                return MakeEntriesFromOffline(plusCode, stylesToUse, places);
            }
            catch (Exception ex)
            {
                Log.WriteLog("Caught unexpected error: " + ex.Message);
                return null;
            }
        }

        public static List<string> GetCoordEntries(Geometry geo, GeoPoint min, double xRes = ConstantValues.resolutionCell11Lon, double yRes = ConstantValues.resolutionCell11Lat)
        {
            List<string> points = new List<string>(geo.Coordinates.Length);

            if (geo.GeometryType == "MultiPolygon")
            {
                foreach (Polygon poly in ((MultiPolygon)geo).Geometries) //This should be the same as the Polygon code below.
                {
                    points.AddRange(GetPolygonPoints(poly, min, xRes, yRes));
                }
            }
            else if (geo.GeometryType == "Polygon")
            {
                points.AddRange(GetPolygonPoints(geo as Polygon, min, xRes, yRes));
            }
            else
                points.Add(string.Join("|", geo.Coordinates.Select(c => (int)Math.Round((c.X - min.Longitude) / xRes) + "," + ((int)Math.Round((c.Y - min.Latitude) / yRes)))));

            if (points.Count == 0)
            {
                //System.Diagnostics.Debugger.Break();
            }

            return points;
        }

        public static List<string> GetPolygonPoints(Polygon p, GeoPoint min, double xRes = ConstantValues.resolutionCell11Lon, double yRes = ConstantValues.resolutionCell11Lat)
        {
            List<string> results = new List<string>(p.Coordinates.Length);
            if (p.Holes.Length == 0)
                results.Add(string.Join("|", p.Coordinates.Select(c => (int)Math.Round((c.X - min.Longitude) / xRes) + "," + ((int)Math.Round((c.Y - min.Latitude) / yRes)))));
            else
            {
                //Split this polygon  into smaller pieces, split on the center of each hole present on latitude
                //West to east direction chosen arbitrarily.
                var westEdge = p.Coordinates.Min(c => c.X);
                var eastEdge = p.Coordinates.Max(c => c.X);
                var northEdge = p.Coordinates.Max(c => c.Y);
                var southEdge = p.Coordinates.Min(c => c.Y);


                List<double> splitPoints = new List<double>(p.Holes.Length);
                foreach (var hole in p.Holes.OrderBy(h => h.Centroid.X))
                    splitPoints.Add(hole.Centroid.X);
                splitPoints.Add(eastEdge);

                Polygon splitPoly;
                Geometry subPoly;
                var lastWest = westEdge;
                foreach (var point in splitPoints)
                {
                    try
                    {
                        splitPoly = new GeoArea(southEdge, lastWest, northEdge, point).ToPolygon();
                        subPoly = p.Intersection(splitPoly);

                        //Still need to check that we have reasonable geometry here.
                        if (subPoly.GeometryType == "Polygon")
                        {
                            var sp = GeometrySupport.CCWCheck(subPoly as Polygon);
                            results.AddRange(GetPolygonPoints(sp, min, xRes, yRes));
                        }
                        else if (subPoly.GeometryType == "MultiPolygon")
                        {
                            foreach (Polygon p2 in ((MultiPolygon)subPoly).Geometries)
                            {
                                var sp2 = GeometrySupport.CCWCheck(p2);
                                results.AddRange(GetPolygonPoints(sp2, min, xRes, yRes));
                            }
                        }
                        else
                            Log.WriteLog("Offline process error: Got geoType " + subPoly.GeometryType + ", which wasn't expected");
                    }
                    catch (Exception ex)
                    {
                        Log.WriteLog("Offline process error: " + ex.Message);
                    }
                    finally
                    {
                        lastWest = point;
                    }
                }
            }
            return results.Distinct().ToList(); //In the unlikely case splitting ends up processing the same part twice
        }

        static readonly JsonSerializerOptions jso = new JsonSerializerOptions() { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };
        public static void MakeMinimizedOfflineData(string plusCode, Polygon bounds = null, bool saveToFile = true, ZipArchive inner_zip = null, List<DbTables.OfflinePlace> places = null)
        {
            //This produces JSON with 1 row per item and a few fields:
            //Name (table in the file), PlusCode (centerpoint), radius, and terrain type.
            //This is worth considering for games that DONT need geometry and can do a little bit of lookup on their own.
            //This may also be created per Cell2/4/6 block for comparison vs drawable data.

            //Make offline data for PlusCode6s, repeatedly if the one given is a 4 or 2.
            if (bounds == null)
            {
                var dbB = new PraxisContext();
                var settings = dbB.ServerSettings.FirstOrDefault();
                bounds = new GeoArea(settings.SouthBound, settings.WestBound, settings.NorthBound, settings.EastBound).ToPolygon();
                dbB.Dispose();
            }

            //Called with an empty string, to mean 'run for all Cell2s'
            if (plusCode == "")
            {
                foreach (var pair in ConstantValues.GetCell2Combos())
                    MakeMinimizedOfflineData(plusCode + pair, bounds, saveToFile);
                return;
            }

            var area = plusCode.ToPolygon();
            if (!area.Intersects(bounds))
                return;

            if (plusCode.Length < 6)
            {
                //if (!PraxisCore.Place.DoPlacesExist(plusCode.ToGeoArea(), places))
                if (!PraxisCore.Place.DoOfflinePlacesExist(plusCode.ToGeoArea(), places))
                    return;

                if (plusCode.Length == 4)
                {
                    if (!File.Exists(filePath + plusCode.Substring(0, 2) + "\\" + plusCode.Substring(0, 4) + ".zip"))
                    {
                        inner_zip = new ZipArchive(File.Create(filePath + plusCode.Substring(0, 2) + "\\" + plusCode.Substring(0, 4) + ".zip"), ZipArchiveMode.Update);
                    }
                    else
                        inner_zip = ZipFile.Open(filePath + plusCode.Substring(0, 2) + "\\" + plusCode.Substring(0, 4) + ".zip", ZipArchiveMode.Update);

                    try
                    {
                        Stopwatch load = Stopwatch.StartNew();
                        places = Place.GetOfflinePlaces(plusCode.ToGeoArea()); // Not padded, because this isn't drawing stuff.
                        Parallel.ForEach(places, (place) => {
                            place.ElementGeometry = place.ElementGeometry.Intersection(area);
                        });
                        load.Stop();
                        Console.WriteLine("Places loaded in " + load.Elapsed + ", count " + places.Count.ToString() + ", biggest " + places.Max(p => p.ElementGeometry.Coordinates.Length).ToString());
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("Places for " + plusCode + " wouldn't load, doing it per Cell6");
                        places = null;
                    }

                    //This block isnt limited by IO, and will suffer from swapping threads constantly.
                    ParallelOptions po = new ParallelOptions() { MaxDegreeOfParallelism = Environment.ProcessorCount };
                    Parallel.ForEach(ConstantValues.GetCellCombos(), po, pair =>
                    {
                        MakeMinimizedOfflineData(plusCode + pair, bounds, saveToFile, inner_zip, places);
                    });

                    bool removeFile = false;
                    if (inner_zip.Entries.Count == 0)
                        removeFile = true;

                    if (inner_zip != null)
                        inner_zip.Dispose();

                    if (removeFile)
                        File.Delete(filePath + plusCode.Substring(0, 2) + "\\" + plusCode.Substring(0, 4) + ".zip");

                    return;
                }
                else if (plusCode.Length == 2)
                {
                    var doneCell2s = File.ReadAllText("lastOfflineEntry.txt");
                    if (doneCell2s.Contains(plusCode) && plusCode != "")
                        return;

                    var folder = string.Concat(filePath, plusCode.AsSpan(0, 2));
                    Directory.CreateDirectory(folder);
                    foreach (var pair in ConstantValues.GetCellCombos())
                        MakeMinimizedOfflineData(plusCode + pair, bounds, saveToFile);

                    File.AppendAllText("lastOfflineEntry.txt", "|" + plusCode);
                    return;
                }
            }

            var sw = Stopwatch.StartNew();
            var finalData = MakeMinimizedOfflineEntries(plusCode, string.Join(",", styles), places);
            if (finalData == null || (finalData.nameTable == null && finalData.entries.Count == 0))
                return;

            string data = JsonSerializer.Serialize(finalData, jso);

            if (saveToFile)
            {
                lock (zipLock)
                {
                    Stream entryStream;
                    var entry = inner_zip.GetEntry(plusCode + ".json");
                    if (entry != null)
                    {
                        entryStream = entry.Open();
                        OfflineDataV2Min existingData = JsonSerializer.Deserialize<OfflineDataV2Min>(entryStream);
                        finalData = MergeMinimumOfflineFiles(finalData, existingData);
                        entryStream.Position = 0;
                    }
                    else
                    {
                        try
                        {
                            entry = inner_zip.CreateEntry(plusCode + ".json", CompressionLevel.Optimal);
                            entryStream = entry.Open();
                        }
                        catch (Exception ex)
                        {
                            Log.WriteLog("Error reading " + plusCode + ": " + ex.Message);
                            //Zip file is probably corrupt, skip it
                            return;
                        }
                    }

                    using (var streamWriter = new StreamWriter(entryStream))
                        streamWriter.Write(data);
                    entryStream.Close();
                    entryStream.Dispose();
                }
            }
            else
            {
                GenericData.SetAreaData(plusCode, "offlineV2", data);
            }


            sw.Stop();
            Log.WriteLog("Created and saved minimized offline data for " + plusCode + " in " + sw.Elapsed);
        }

        public static OfflineDataV2Min MakeMinimizedOfflineEntries(string plusCode, string stylesToUse, List<DbTables.OfflinePlace> places = null)
        {
            var styles = stylesToUse.Split(",");

            var cell = plusCode.ToGeoArea();
            var area = plusCode.ToPolygon();

            //Minimized offline files do not benefit from variables, they're fairly fixed and changing anything here doesn't really help.

            var min = cell.Min;
            Dictionary<string, int> nametable = new Dictionary<string, int>(); //name, id
            var nameIdCounter = 0;

            if (!PraxisCore.Place.DoOfflinePlacesExist(cell, places))
                return null;

            const double innerRes = ConstantValues.resolutionCell10;

            var finalData = new OfflineDataV2Min();
            finalData.olc = plusCode;
            finalData.entries = new Dictionary<string, List<MinOfflineData>>();
            foreach (var style in styles)
            {
                var placeData = PraxisCore.Place.GetOfflinePlaces(cell, source: places);

                if (placeData.Count == 0)
                    continue;
                ConcurrentBag<MinOfflineData> entries = new ConcurrentBag<MinOfflineData>();
                var names = placeData.Where(p => !string.IsNullOrWhiteSpace(p.Name) && !nametable.ContainsKey(p.Name)).Select(p => p.Name).Distinct();
                foreach (var name in names)
                    nametable.Add(name, ++nameIdCounter);

                //foreach (var place in placeData)
                Parallel.ForEach(placeData, (place) =>
                {
                    //This is a catch-fix for a different issue, where apparently some closed lineStrings aren't converted to polygons on load.
                    if (place.ElementGeometry.GeometryType == "LineString" && ((LineString)place.ElementGeometry).IsClosed)
                        place.ElementGeometry = Singletons.geometryFactory.CreatePolygon(place.ElementGeometry.Coordinates);

                    Geometry geo = Singletons.geometryFactory.CreateEmpty(Dimension.Surface);
                    try
                    {
                        geo = place.ElementGeometry.Intersection(area);
                    }
                    catch (Exception ex)
                    {
                        //Do nothing for now.
                    }
                    if (geo.IsEmpty)
                        return; // continue; //Probably an element on the border thats getting pulled in by buffer.

                    int? nameID = null;
                    if (!string.IsNullOrWhiteSpace(place.Name))
                    {
                        if (nametable.TryGetValue(place.Name, out var nameval))
                            nameID = nameval;
                    }

                    var styleEntry = TagParser.allStyleGroups[style][place.StyleName];
                    if (geo.GeometryType == "Point")
                    {
                        var offline = new MinOfflineData();
                        offline.nid = nameID;
                        offline.c = (int)Math.Round((geo.Coordinate.X - min.Longitude) / innerRes) + "," + ((int)Math.Round((geo.Coordinate.Y - min.Latitude) / innerRes));
                        offline.r = 2; //5 == Cell11 resolution. Use 22 for Cell12, use 2 for Cell10 (the space the point is in, and the ones surrounding it.
                        offline.tid = styleEntry.MatchOrder;
                        entries.Add(offline);
                    }
                    else if (geo.GeometryType == "Polygon")
                    {
                        var offline = new MinOfflineData();
                        offline.nid = nameID;
                        offline.c = (int)Math.Round((geo.Centroid.X - min.Longitude) / innerRes) + "," + ((int)Math.Round((geo.Centroid.Y - min.Latitude) / innerRes));
                        offline.r = Math.Max(2, (int)Math.Round(Math.Sqrt(geo.Area / Math.PI) / ConstantValues.resolutionCell10)); //Get area in degrees, conver to Cell10 pixels, minimum 2.
                        offline.tid = styleEntry.MatchOrder;
                        entries.Add(offline);
                    }
                    else if (geo.GeometryType == "MultiPolygon")
                    {
                        foreach (var p in ((MultiPolygon)geo).Geometries)
                        {
                            var offline = new MinOfflineData();
                            offline.nid = nameID;
                            offline.c = (int)Math.Round((p.Centroid.X - min.Longitude) / innerRes) + "," + ((int)Math.Round((p.Centroid.Y - min.Latitude) / innerRes));
                            offline.r = Math.Max(2, (int)Math.Round(Math.Sqrt(p.Area / Math.PI) / ConstantValues.resolutionCell10)); //Get area in degrees, conver to Cell10 pixels, minimum 2.
                            offline.tid = styleEntry.MatchOrder;
                            entries.Add(offline);
                        }
                    }
                    else if (geo.GeometryType == "LineString")
                    {
                        var lp = geo as LineString;
                        if (lp.IsClosed) //Treat this as a polygon.
                        {
                            var offline = new MinOfflineData();
                            offline.nid = nameID;
                            offline.c = (int)Math.Round((lp.Centroid.X - min.Longitude) / innerRes) + "," + ((int)Math.Round((lp.Centroid.Y - min.Latitude) / innerRes));
                            offline.r = (int)Math.Round(((lp.EnvelopeInternal.Width + lp.EnvelopeInternal.Height) * 0.5) / innerRes); //Area is 0 on lines, so use the old formula
                            offline.tid = styleEntry.MatchOrder;
                            entries.Add(offline);
                        }
                        else
                        {
                            //We're gonna assumed this is a named trail. Make the start and end of it Points (radius = 2) with the trail's name.
                            var offline = new MinOfflineData();
                            offline.nid = nameID;
                            offline.c = (int)Math.Round((geo.Coordinates.First().X - min.Longitude) / innerRes) + "," + ((int)Math.Round((geo.Coordinates.First().Y - min.Latitude) / innerRes));
                            offline.r = 2;
                            offline.tid = styleEntry.MatchOrder;
                            entries.Add(offline);

                            var offline2 = new MinOfflineData();
                            offline2.nid = nameID;
                            offline2.c = (int)Math.Round((geo.Coordinates.Last().X - min.Longitude) / innerRes) + "," + ((int)Math.Round((geo.Coordinates.Last().Y - min.Latitude) / innerRes));
                            offline2.r = 2;
                            offline2.tid = styleEntry.MatchOrder;
                            entries.Add(offline2);
                        }
                    }
                    else if (place.ElementGeometry.GeometryType == "MultiLineString")
                    {
                        //Not totally sure why this would show up, but again assume its segments of a named trail
                        var mls = place.ElementGeometry as MultiLineString;
                        foreach (var line in mls)
                        {
                            var offline = new MinOfflineData();
                            offline.nid = nameID;
                            offline.c = (int)Math.Round((geo.Coordinates.First().X - min.Longitude) / innerRes) + "," + ((int)Math.Round((geo.Coordinates.First().Y - min.Latitude) / innerRes));
                            offline.r = 2;
                            offline.tid = styleEntry.MatchOrder;
                            entries.Add(offline);

                            var offline2 = new MinOfflineData();
                            offline2.nid = nameID;
                            offline2.c = (int)Math.Round((geo.Coordinates.Last().X - min.Longitude) / innerRes) + "," + ((int)Math.Round((geo.Coordinates.Last().Y - min.Latitude) / innerRes));
                            offline2.r = 2;
                            offline2.tid = styleEntry.MatchOrder;
                            entries.Add(offline2);
                        }
                    }
                });
                var finalEntries = entries.OrderByDescending(e => e.r).ToList(); //so they'll be drawn biggest to smallest for sure.
                finalData.entries[style] = finalEntries;
            }

            if (finalData.entries.Count == 0)
                return null;
            finalData.nameTable = nametable.Count > 0 ? nametable.ToDictionary(k => k.Value, v => v.Key) : null;

            return finalData;
        }

        public static OfflineDataV2Min MergeMinimumOfflineFiles(OfflineDataV2Min existing, OfflineDataV2Min adding)
        {
            //Step 1: Update name table
            Dictionary<int, int> newNameMap = new Dictionary<int, int>(); //<addingTableKey, exisitngTAbleKey>
            if (existing.nameTable == null)
                existing.nameTable = new Dictionary<int, string>();

            int maxKey = existing.nameTable.Count;
            if (adding.nameTable != null)
            {
                foreach (var name in adding.nameTable)
                {
                    if (existing.nameTable.ContainsValue(name.Value))
                    {
                        newNameMap.Add(name.Key, existing.nameTable.First(n => n.Value == name.Value).Key);
                    }
                    else
                    {
                        existing.nameTable.Add(++maxKey, name.Value);
                        newNameMap.Add(name.Key, maxKey);
                    }
                }
            }

            //Step 2: merge sets of entries
            foreach (var entryList in adding.entries)
            {
                if (!existing.entries.TryGetValue(entryList.Key, out var entry))
                    existing.entries.Add(entryList.Key, entryList.Value);
                else if (entry != null)
                {
                    //merge entries
                    var list2 = entryList.Value;
                    list2 = list2.Select(e => new MinOfflineData() { c = e.c, r = e.r, tid = e.tid, nid = e.nid.HasValue ? newNameMap[e.nid.Value] : null }).ToList();
                    //Remove duplicates
                    var remove = list2.Where(l2 => entry.Any(l1 => l1.c == l2.c && l1.nid == l2.nid && l1.tid == l2.tid && l1.r == l2.r)).ToList();
                    foreach (var r in remove)
                        list2.Remove(r);

                    entry.AddRange(list2);
                }
            }

            if (existing.nameTable.Count == 0 && (adding.nameTable == null || adding.nameTable.Count == 0))
                existing.nameTable = null;

            return existing;
        }


        public static OfflineDataV2 MergeOfflineFiles(OfflineDataV2 existing, OfflineDataV2 adding)
        {
            OfflineDataV2 bigger, smaller;

            if (existing.entries.Sum(e => e.Value.Count()) > adding.entries.Sum(e => e.Value.Count()))
            {
                bigger = existing;
                smaller = adding;
            }
            else
            {
                bigger = adding;
                smaller = existing;
            }

            //Step 1: Update name table
            Dictionary<int, int> newNameMap = new Dictionary<int, int>(); //<addingTableKey, exisitngTAbleKey>
            if (bigger.nameTable == null)
                bigger.nameTable = new Dictionary<int, string>();

            int maxKey = bigger.nameTable.Count;
            if (smaller.nameTable != null)
            {
                foreach (var name in smaller.nameTable)
                {
                    if (bigger.nameTable.ContainsValue(name.Value))
                    {
                        newNameMap.Add(name.Key, bigger.nameTable.First(n => n.Value == name.Value).Key);
                    }
                    else
                    {
                        bigger.nameTable.Add(++maxKey, name.Value);
                        newNameMap.Add(name.Key, maxKey);
                    }
                }
            }
            
            //Step 2: merge sets of entries
            var allEntries = bigger.entries.Keys.Union(smaller.entries.Keys).ToList();
            foreach (var entryList in allEntries)
            {
                var hasBig = bigger.entries.TryGetValue(entryList, out var bigEntry);
                var hasSmall = smaller.entries.TryGetValue(entryList, out var smallEntry);

                //TODO: I still need to check for updated nametable entries when dropping in a whole block from the other file.
                if (hasBig && !hasSmall)
                {
                    foreach (var e in bigEntry)
                        if (e.nid > 0 && newNameMap.TryGetValue(e.nid.Value, out var newId))
                            e.nid = newId;
                    bigger.entries[entryList] = bigEntry;
                }
                else if (!hasBig && hasSmall)
                {
                    foreach (var e in smallEntry)
                        if (e.nid > 0 && newNameMap.TryGetValue(e.nid.Value, out var newId))
                            e.nid = newId;
                    bigger.entries[entryList] = smallEntry;
                }
                    
                else if (hasBig && hasSmall)
                {
                    //NOW we merge the two sets to bigger.
                    foreach (var e in smallEntry)
                    {
                        if (e.nid > 0 && newNameMap.TryGetValue(e.nid.Value, out var newId))
                            e.nid = newId;
                        //else //nope, this needs that sencond part of the and specifically to be an error.
                        //Log.WriteLog("Found an error merging files - nid not found in table at " + bigger.olc);
                    }
                    bigEntry.AddRange(smallEntry.Where(l2 => !bigEntry.Any(l1 => l1.p == l2.p && l1.nid == l2.nid && l1.tid == l2.tid && l1.gt == l2.gt)));
                }
            }

            if (bigger.nameTable.Count == 0 && (smaller.nameTable == null || smaller.nameTable.Count == 0))
                bigger.nameTable = null;

            return bigger;
        }

        public static HashSet<string> MakeNameTablePieces(List<string> allNames)
        {
            var db = new PraxisContext();

            var nameHashes = new HashSet<string>();
            foreach(var name in allNames)
                foreach(var piece in name.Split(' '))
                    nameHashes.Add(piece);

            return nameHashes;
        }

        public static string GetMinimizedName(string name, ref HashSet<string> nameHashes)
        {
            var parts = name.Split(' ');
            for(int i = 0; i <= parts.Count(); i++)
            {
                parts[i] = nameHashes.Index().First(n => n.Item == parts[i]).Index.ToString();
            }

            return String.Join(' ', parts);
        }
    }


    public class IndexEntry
    {
        public int Index { get; set; }
        public string Name { get; set; }

        public static implicit operator IndexEntry((int, string) tuple)
        {
            return new IndexEntry() { Index = tuple.Item1, Name = tuple.Item2 };
        }
    }

}
