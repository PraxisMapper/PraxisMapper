using DatabaseAccess;
using DatabaseAccess.Support;
using EFCore.BulkExtensions;
using Google.OpenLocationCode;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite;
using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Utilities;
using NetTopologySuite.Precision;
using OsmSharp.Changesets;
using OsmSharp.Streams;
using OsmSharp.Streams.Filters;
using OsmSharp.Tags;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Xml;
using static DatabaseAccess.DbTables;
using static DatabaseAccess.MapSupport;

//TODO: some node names are displaying in the debug console as "?????? ????". See Siberia. This should all be unicode and that should work fine. Maybe check display font?
//TODO: since some of these .pbf files become larger as trimmer JSON instead of smaller, maybe I should try a path that writes directly to DB from PBF?
//TODO: optimize data - If there are multiple nodes within a 10-cell's distance of each other, consolidate those into one node (but not if they're part of a relation)?
//TODO: ponder how to remove inner polygons from a larger outer polygon. This is probably a NTS function of some kind, but only applies to relations. Ways alone won't do this. This would involve editing the data loaded, not just converting it.
//TODO: functionalize stuff to smaller pieces.
//TODO: Add high-verbosity logging messages.
//TODO: set option flag to enable writing MapData entries to DB or File. Especially since bulk inserts won't fly for MapData from files, apparently.

namespace OsmXmlParser
{
    //These record trim down OSM data to just the parts I need.
    
    class Program
    {
        //NOTE: OSM data license allows me to use the data but requires acknowleging OSM as the data source

        public static string parsedJsonPath = @"D:\Projects\OSM Server Info\Trimmed JSON Files\";
        public static string parsedPbfPath = @"D:\Projects\OSM Server Info\XmlToProcess\";

        //Constants
        public static double PlusCode10Resolution = .000125;
        public static double PlusCode8Resolution = .0025;
        public static double PlusCode6Resolution = .05;

        static void Main(string[] args)
        {
            if (args.Count() == 0)
            {
                Console.WriteLine("You must pass an arguement to this application");
                //TODO: list args
                return;
            }

            //Check for logging arguement first before running any commands.
            if (args.Any(a => a == "-v" || a == "-verbose"))
                Log.Verbosity = Log.VerbosityLevels.High;

            if (args.Any(a => a == "-noLogs"))
                Log.Verbosity = Log.VerbosityLevels.Off;

            //If multiple args are supplied, run them in the order that make sense, not the order the args are supplied.

            if (args.Any(a => a == "-makeDbInfrastructure")) //create all the Db-side things this app needs that EFCore can't do automatically.
            {
                GpsExploreContext db = new GpsExploreContext();
                db.Database.ExecuteSqlRaw(GpsExploreContext.MapDataValidTrigger);
                db.Database.ExecuteSqlRaw(GpsExploreContext.MapDataIndex);
            }

            if (args.Any(a => a == "-cleanDB"))
            {
                CleanDb();
            }

            if (args.Any(a => a == "-resetXml" || a == "-resetPbf")) //update both anyways.
            {
                List<string> filenames = System.IO.Directory.EnumerateFiles(@"D:\Projects\OSM Server Info\XmlToProcess\", "*.*Done").ToList();
                foreach (var file in filenames)
                {
                    File.Move(file, file.Substring(0, file.Length - 4));
                }
            }

            if (args.Any(a => a == "-resetJson"))
            {
                List<string> filenames = System.IO.Directory.EnumerateFiles(@"D:\Projects\OSM Server Info\Trimmed JSON Files\", "*.jsonDone").ToList();
                foreach (var file in filenames)
                {
                    File.Move(file, file.Substring(0, file.Length - 4));
                }
            }

            if (args.Any(a => a == "-trimXmlFiles"))
            {
                MakeAllSerializedFiles();
            }


            if (args.Any(a => a == "-trimPbfFiles"))
            {
                MakeAllSerializedFilesFromPBF();
            }

            if (args.Any(a => a == "-readRawWays"))
            {
                AddRawWaystoDBFromFiles();
            }

            if (args.Any(a => a == "-readMapData"))
            {
                AddMapDataToDBFromFiles();
            }

            if (args.Any(a => a == "-removeDupes"))
            {
                RemoveDuplicates();
            }

            if (args.Any(a => a == "-loadRawOsmData"))
            {
                //TODO: finish this logic. This does create a much bigger database than using NTS Geography types.
                GetMinimumDataFromPbf();
            }

            if (args.Any(a => a == "-createStandalone"))
            {
                //Near-future plan: make an app that covers an area and pulls in all data for it.
                //like a university or a park. Draws ALL objects there to an SQLite DB used directly by an app, no data connection.
                //Second arg: area covered somehow (way or relation ID of thing to use? big plus code?)
            }

            return;
        }

        //This file should be obsoleted by serializing MapData types to file instead.
        public static void AddRawWaystoDBFromFiles()
        {
            //These are MapData items in the DB, unlike the other types that match names in code and DB tables.
            //This function is pretty slow. I should figure out how to speed it up. Approx. 6,000 ways per second right now.
            //TODO: don't insert duplicate objects. ~400,000 ways get inserted more than once for some reason. Doesn't seem to impact performance but could be improved.
            //--Probably need to track down which partial files overlap. LocalCity.osm is a few.
            var factory = NtsGeometryServices.Instance.CreateGeometryFactory(srid: 4326); //SRID matches Plus code values.
            foreach (var file in System.IO.Directory.EnumerateFiles(parsedJsonPath, "*-RawWays.json"))
            {
                GpsExploreContext db = new GpsExploreContext();
                db.ChangeTracker.AutoDetectChangesEnabled = false; //Allows single inserts to operate at a reasonable speed. Nothing else edits this table.
                List<Way> entries = ReadRawWaysToMemory(file);

                Log.WriteLog("Processing " + entries.Count() + " ways from " + file);
                int errorCount = 0;
                int loopCount = 0;
                System.Diagnostics.Stopwatch timer = new System.Diagnostics.Stopwatch();
                timer.Start();
                foreach (Way w in entries)
                {
                    loopCount++;
                    if (w.nds.Any(n => n == null || n.lat == null || n.lon == null))
                    {
                        Log.WriteLog("Way " + w.id + " " + w.name + " rejected for having unusable nodes.");
                        errorCount++;
                        continue;
                    }
                    if (timer.ElapsedMilliseconds > 10000)
                    {
                        Log.WriteLog("Processed " + loopCount + " ways so far");
                        timer.Restart();
                    }
                    try
                    {
                        MapData md = new MapData();
                        md.name = w.name;
                        md.WayId = w.id;
                        md.type = w.AreaType;

                        //Adding support for single lines. A lot of rivers/streams/footpaths are treated this way.
                        if (w.nds.First().id != w.nds.Last().id)
                        {
                            LineString temp2 = factory.CreateLineString(w.nds.Select(n => new Coordinate(n.lon, n.lat)).ToArray());
                            md.place = temp2;
                        }
                        else
                        {
                            Polygon temp = factory.CreatePolygon(w.nds.Select(n => new Coordinate(n.lon, n.lat)).ToArray());
                            if (!temp.Shell.IsCCW)
                            {
                                temp = (Polygon)temp.Reverse();
                                if (!temp.Shell.IsCCW)
                                {
                                    Log.WriteLog("Way " + w.id + " needs more work to be parsable, it's not counter-clockwise forward or reversed.");
                                    errorCount++;
                                    continue;
                                }
                                if (!temp.IsValid)
                                {
                                    Log.WriteLog("Way " + w.id + " needs more work to be parsable, it's not valid according to its own internal check.");
                                    errorCount++;
                                    continue;
                                }
                            }
                            md.place = temp;
                        }

                        //Trying to add each entry indiviudally to detect additional errors for now.
                        db.MapData.Add(md);
                        db.SaveChanges();
                    }
                    catch (Exception ex)
                    {
                        errorCount++;
                        while (ex.InnerException != null)
                            ex = ex.InnerException;

                        Log.WriteLog(file + " | " + ex.Message + " | " + w.name + " " + w.id);
                        //Common messages:
                        //points must form a closed line.
                        //Still getting a CCW error on save?
                        //at least 1 way has nodes with null coordinates, cant use those either
                        //number of points must be 0 or >3 - this one's new.
                        //app domain was unloaded due to memory pressure - also new for this. SQL internal issue?
                    }
                }
                Log.WriteLog("Added " + file + " to dB at " + DateTime.Now);
                Log.WriteLog(errorCount + " ways excluded due to errors (" + ((errorCount / entries.Count()) * 100) + "%)");

                File.Move(file, file + "Done");
            }
        }

        public static void AddMapDataToDBFromFiles()
        {
            //This function is pretty slow. I should figure out how to speed it up. Approx. 6,000 ways per second right now.
            var factory = NtsGeometryServices.Instance.CreateGeometryFactory(srid: 4326); //SRID matches Plus code values.
            foreach (var file in System.IO.Directory.EnumerateFiles(parsedJsonPath, "*-MapData.json"))
            {
                Log.WriteLog("Starting MapData read from  " + file + " at " + DateTime.Now);
                GpsExploreContext db = new GpsExploreContext();
                db.ChangeTracker.AutoDetectChangesEnabled = false; //Allows single inserts to operate at a reasonable speed (~6000 per second). Nothing else edits this table.
                List<MapData> entries = ReadMapDataToMemory(file);

                Log.WriteLog("Processing " + entries.Count() + " ways from " + file);
                System.Diagnostics.Stopwatch timer = new System.Diagnostics.Stopwatch();
                timer.Start();
                //db.BulkInsert<MapData>(entries); //throws errors around typing.

                db.MapData.AddRange(entries);
                db.SaveChanges();
                
                Log.WriteLog("Added " + file + " to dB at " + DateTime.Now);
                //Log.WriteLog(errorCount + " ways excluded due to errors (" + ((errorCount / entries.Count()) * 100) + "%)"); //Errored entries were already excluded from the file before it was written.

                File.Move(file, file + "Done");
            }
        }

        public static void CleanDb()
        {
            Log.WriteLog("Cleaning DB at " + DateTime.Now);
            GpsExploreContext osm = new GpsExploreContext();
            osm.Database.SetCommandTimeout(900);

            osm.Database.ExecuteSqlRaw("TRUNCATE TABLE AreaTypes");
            Log.WriteLog("AreaTypes cleaned at " + DateTime.Now);

            osm.Database.ExecuteSqlRaw("TRUNCATE TABLE MapData");
            Log.WriteLog("MapData cleaned at " + DateTime.Now);

            Log.WriteLog("DB cleaned at " + DateTime.Now);
        }

        public static void MakeAllSerializedFiles()
        {
            //Getting away from this path for now, sticking to PBF
            return;

            ////This function is meant to let me save time in the future by giving me the core data I can re-process without the extra I don't need.
            ////It's also in a smaller format than XML to boot.
            ////For later processsing, I will want to work on the smaller files (unless I'm adding data to this intermediate step).
            ////Loading the smaller files directly to the in-memory representation would be so much faster than reading XML tag by tag.
            //List<string> filenames = System.IO.Directory.EnumerateFiles(@"D:\Projects\OSM Server Info\XmlToProcess\", "*.osm").ToList();

            //foreach (string filename in filenames)
            //{
            //    ways = null;
            //    nodes = null;
            //    SPOI = null;
            //    ways = new List<Way>();
            //    nodes = new List<Node>();
            //    SPOI = new List<SinglePointsOfInterest>();

            //    Log.WriteLog("Starting " + filename + " way read at " + DateTime.Now);
            //    XmlReaderSettings xrs = new XmlReaderSettings();
            //    xrs.IgnoreWhitespace = true;
            //    XmlReader osmFile = XmlReader.Create(filename, xrs);
            //    osmFile.MoveToContent();

            //    System.Diagnostics.Stopwatch timer = new System.Diagnostics.Stopwatch();
            //    timer.Start();

            //    //We do this in 2 steps, because this seems to minimize memory-related errors
            //    //Read the Ways first, identify the ones we want by tags, and track which nodes get referenced by those ways.
            //    //The second run will load those nodes into memory to be add their data to the ways.
            //    string destFolder = @"D:\Projects\OSM Server Info\Trimmed JSON Files\";
            //    bool firstWay = true;
            //    bool exitEarly = false;
            //    //first read - save Way info to process later.
            //    while (osmFile.Read() && !exitEarly)
            //    {
            //        if (timer.ElapsedMilliseconds > 10000)
            //        {
            //            //Report progress on the console.
            //            Log.WriteLog("Processed " + ways.Count() + " ways so far");
            //            timer.Restart();
            //        }

            //        switch (osmFile.Name)
            //        {
            //            case "way":
            //                if (firstWay) { Log.WriteLog("First Way entry found at " + DateTime.Now); firstWay = false; }

            //                var w = new Way();
            //                w.id = osmFile.GetAttribute("id").ToLong();

            //                ParseWayDataV2(w, osmFile.ReadSubtree()); //Saves a list of nodeRefs, doesnt look for actual nodes.

            //                if (!string.IsNullOrWhiteSpace(w.AreaType))
            //                    ways.Add(w);
            //                break;
            //            case "relation":
            //                exitEarly = true;
            //                break;
            //        }
            //    }

            //    osmFile.Close(); osmFile.Dispose(); exitEarly = false;
            //    osmFile = XmlReader.Create(filename, xrs);
            //    osmFile.MoveToContent();

            //    var nodesToSave = ways.SelectMany(w => w.nodRefs).ToHashSet<long>();

            //    //second read - load Node data for ways and SPOIs.
            //    while (osmFile.Read() && !exitEarly)
            //    {
            //        if (timer.ElapsedMilliseconds > 10000)
            //        {
            //            //Report progress on the console.
            //            Log.WriteLog("Processed " + nodes.Count() + " nodes so far");
            //            timer.Restart();
            //        }

            //        switch (osmFile.Name)
            //        {
            //            case "node":
            //                if (osmFile.NodeType == XmlNodeType.Element) //sometimes is EndElement if we had tags we ignored.
            //                {
            //                    var n = new Node();
            //                    n.id = osmFile.GetAttribute("id").ToLong();
            //                    n.lat = osmFile.GetAttribute("lat").ToDouble();
            //                    n.lon = osmFile.GetAttribute("lon").ToDouble();
            //                    if (nodesToSave.Contains(n.id))
            //                        if (n.id != null && n.lat != null && n.lon != null)
            //                            nodes.Add(n);

            //                    //This data below doesn't need saved in RAM, so we remove it after processing for SPOI and don't include it in the base Node object.
            //                    var tags = parseNodeTags(osmFile.ReadSubtree());
            //                    string name = tags.Where(t => t.k == "name").Select(t => t.v).FirstOrDefault();
            //                    string nodetype = GetType(tags);
            //                    //Now checking if this node is individually interesting.
            //                    if (nodetype != "")
            //                        SPOI.Add(new SinglePointsOfInterest() { name = name, lat = n.lat, lon = n.lon, NodeID = n.id, NodeType = nodetype, PlusCode = GetPlusCode(n.lat, n.lon) });
            //                }
            //                break;
            //            case "way":
            //                exitEarly = true;
            //                break;
            //        }
            //    }

            //    Log.WriteLog("Attempting to convert " + nodes.Count() + " nodes to lookup at " + DateTime.Now);
            //    nodeLookup = (Lookup<long, Node>)nodes.ToLookup(k => k.id, v => v);

            //    List<long> waysToRemove = new List<long>();
            //    foreach (Way w in ways)
            //    {
            //        foreach (long nr in w.nodRefs)
            //        {
            //            w.nds.Add(nodeLookup[nr].FirstOrDefault());
            //        }
            //        w.nodRefs = null; //free up a little memory we won't use again.

            //        //now that we have nodes, lets do a little extra processing.
            //        var latSpread = w.nds.Max(n => n.lat) - w.nds.Min(n => n.lat);
            //        var lonSpread = w.nds.Max(n => n.lon) - w.nds.Min(n => n.lon);

            //        if (latSpread <= PlusCode10Resolution && lonSpread <= PlusCode10Resolution)
            //        {
            //            //this is small enough to be an SPOI instead
            //            var calcedCode = new OpenLocationCode(w.nds.Average(n => n.lat), w.nds.Average(n => n.lon));
            //            var reverseDecode = calcedCode.Decode();
            //            var spoiFromWay = new SinglePointsOfInterest()
            //            {
            //                lat = reverseDecode.CenterLatitude,
            //                lon = reverseDecode.CenterLongitude,
            //                NodeType = w.AreaType,
            //                PlusCode = calcedCode.Code.Replace("+", ""),
            //                PlusCode8 = calcedCode.Code.Substring(0, 8),
            //                name = w.name,
            //                NodeID = w.id //Will have to remember this could be a node or a way in the future.
            //            };
            //            SPOI.Add(spoiFromWay);
            //            waysToRemove.Add(w.id);
            //            w.nds = null; //free up a small amount of RAM now instead of later.
            //        }
            //    }
            //    //now remove ways we converted to SPOIs.
            //    foreach (var wtr in waysToRemove)
            //        ways.Remove(ways.Where(w => w.id == wtr).FirstOrDefault());

            //    Log.WriteLog("Ways populated with Nodes at " + DateTime.Now);
            //    nodes = null; //done with these now, can free up RAM again.

            //    //Moved here while working on reading Ways for items that are too small.
            //    Log.WriteLog("Done reading Node objects at " + DateTime.Now);
            //    WriteSPOIsToFile(destFolder + System.IO.Path.GetFileNameWithoutExtension(filename) + "-SPOIs.json");
            //    SPOI = null;

            //    WriteRawWaysToFile(destFolder + System.IO.Path.GetFileNameWithoutExtension(filename) + "-RawWays.json");

            //    //I don't currently use the processed way data set, since spatial indexes are efficient enough
            //    //I might return to using an abbreviated data set, but not for now, and I'll need a better way to approximate this when I do.
            //    //List<ProcessedWay> pws = new List<ProcessedWay>();
            //    //foreach (Way w in ways)
            //    //{
            //    //    var pw = ProcessWay(w);
            //    //    if (pw != null)
            //    //        pws.Add(pw);
            //    //}
            //    //WriteProcessedWaysToFile(destFolder + System.IO.Path.GetFileNameWithoutExtension(filename) + "-ProcessedWays.json", ref pws);
            //    //pws = null;
            //    //nodeLookup = null;

            //    osmFile.Close(); osmFile.Dispose();
            //    Log.WriteLog("Processed " + filename + " at " + DateTime.Now);
            //    File.Move(filename, filename + "Done"); //We made it all the way to the end, this file is done.
            //}
        }

        public static void MakeAllSerializedFilesFromPBF()
        {
            string destFolder = @"D:\Projects\OSM Server Info\Trimmed JSON Files\";
            List<string> filenames = System.IO.Directory.EnumerateFiles(@"D:\Projects\OSM Server Info\XmlToProcess\", "*.pbf").ToList();
            var factory = NtsGeometryServices.Instance.CreateGeometryFactory(srid: 4326);

            foreach (string filename in filenames)
            {
                string destFilename = System.IO.Path.GetFileName(filename).Replace(".osm.pbf", "");
                List<DatabaseAccess.Support.Node> nodes = new List<DatabaseAccess.Support.Node>();
                List<DatabaseAccess.Support.Way> ways = new List<DatabaseAccess.Support.Way>();
                List<MapData> processedEntries = new List<MapData>();
                //Minimizes time spend boosting capacity and copying the internal values later.
                nodes.Capacity = 100000;
                ways.Capacity = 100000;
                processedEntries.Capacity = 100000;

                Log.WriteLog("Starting " + filename + " relation read at " + DateTime.Now);
                var osmRelations = GetRelationsFromPbf(filename);
                var wayList = osmRelations.SelectMany(r => r.Members).ToList(); //ways that need tagged as the relation's type if they dont' have their own.

                Log.WriteLog("Checking " + osmRelations.Count() + " relations with " + wayList.Count() + " ways at " + DateTime.Now);

                List<RelationMember> waysFromRelations = new List<RelationMember>();
                foreach (var stuff in osmRelations)
                {
                    string relationType = MapSupport.GetType(stuff.Tags);
                    string name = GetElementName(stuff.Tags);
                    foreach (var member in stuff.Members)
                        waysFromRelations.Add(new RelationMember() { Id = member.Id, type = relationType, name = name });
                }
                var wayLookup = waysFromRelations.ToLookup(k => k.Id, v => v);

                Log.WriteLog("Starting " + filename + " way read at " + DateTime.Now);
                var osmWays = GetWaysFromPbf(filename, wayLookup);
                Lookup<long, long> nodeLookup = (Lookup<long, long>)osmWays.SelectMany(w => w.Nodes).Distinct().ToLookup(k => k, v => v);
                Log.WriteLog("Found " + osmWays.Count() + " ways with " + nodeLookup.Count() + " nodes");

                Log.WriteLog("Starting " + filename + " node read at " + DateTime.Now);
                var osmNodes = GetNodesFromPbf(filename, nodeLookup);
                Log.WriteLog("Creating node lookup for " + osmNodes.Count() + " nodes"); //33 million nodes across 2 million ways will tank this app at 16GB RAM
                var osmNodeLookup = osmNodes.ToLookup(k => k.Id, v => v); //Seeing if NodeReference saves some RAM
                Log.WriteLog("Found " + osmNodeLookup.Count() + " unique nodes");

                //Write nodes as mapdata if they're tagged separately from other things.
                Log.WriteLog("Finding tagged nodes at " + DateTime.Now);
                var taggedNodes = osmNodes.Where(n => n.name != "" && n.type != "").ToList();
                processedEntries.AddRange(taggedNodes.Select(s => MapSupport.ConvertNodeToMapData(s)));
                taggedNodes = null;

                //This is now the slowest part of the processing function.
                Log.WriteLog("Converting " + osmWays.Count() + " OsmWays to my Ways at " + DateTime.Now);
                ways.Capacity = osmWays.Count();
                ways = osmWays.Select(w => new DatabaseAccess.Support.Way()
                {
                    id = w.Id.Value,
                    name = GetElementName(w.Tags),
                    AreaType = MapSupport.GetType(w.Tags),
                    nodRefs = w.Nodes.ToList()
                })
                .ToList();
                osmWays = null; //free up RAM we won't use again.
                Log.WriteLog("List created at " + DateTime.Now);

                int wayCounter = 0;
                foreach (Way w in ways)
                {
                    wayCounter++;
                    if (wayCounter % 10000 == 0)
                        Log.WriteLog(wayCounter + " processed so far");

                    foreach (long nr in w.nodRefs)
                    {
                        var osmNode = osmNodeLookup[nr].FirstOrDefault();
                        var myNode = new Node() { id = osmNode.Id, lat = osmNode.lat, lon = osmNode.lon };
                        w.nds.Add(myNode);
                    }
                    w.nodRefs = null; //free up a little memory we won't use again.

                    //We should now be removing any ways that are processed by a Relation, so this is unnecessary
                    //Except that we do the relation processing after this?
                    //Check if this way is from a relation, and if so attach the relation's data.
                    if (string.IsNullOrWhiteSpace(w.name))
                    {
                        var relation = wayLookup[w.id].FirstOrDefault();
                        if (relation != null)
                            if (!string.IsNullOrWhiteSpace(relation.name))
                                w.name = relation.name;
                    }
                    if (string.IsNullOrWhiteSpace(w.AreaType))
                    {
                        var relation = wayLookup[w.id].FirstOrDefault();
                        if (relation != null)
                            if (!string.IsNullOrWhiteSpace(relation.type))
                                w.AreaType = relation.type;
                    }
                }

                Log.WriteLog("Ways populated with Nodes at " + DateTime.Now);
                osmNodes = null; //done with these now, can free up RAM again.

                //TODO: Use Relations to do some work on Ways. This needs done after loading way and node data, since i'll be processing it with those coordinates.
                //--Remove Inner ways from Outer Ways
                //--combine multiple polygons into a single mapdata entry? This should be doable.
                GpsExploreContext db = new GpsExploreContext();
                foreach (var r in osmRelations)
                {
                    //Determine if we need to process this relation.
                    //if all ways are closed outer polygons, we can skip this.
                    //if all ways are lines that connect, we need to make it a polygon.
                    //We can't always rely on tags being correct.

                    //I might need to check if these are usable ways before checking if they're already handled by the relation
                    //I also wonder if a relation doesn't include all the ways if some are in different boundaries for a file?

                    if (r.Members.Any(m => m.Type == OsmSharp.OsmGeoType.Relation))
                    {
                        Log.WriteLog("Relation " + r.Id + " " + GetElementName(r.Tags) + " is a meta-relation, skipping.");
                        continue;
                    }

                    if (r.Members.Any(m => m.Type == OsmSharp.OsmGeoType.Node))
                    {
                        Log.WriteLog("Relation " + r.Id + " " + GetElementName(r.Tags) + " has a standalone node, skipping.");
                        continue;
                    }

                    //Check members for closed shape
                    var shapeList = new List<Way>();
                    //var allClosedShapes = true;
                    foreach (var m in r.Members)
                    {
                        var maybeWay = ways.Where(way => way.id == m.Id).FirstOrDefault();
                        if (maybeWay != null)
                            shapeList.Add(maybeWay);
                        else
                        {
                            Log.WriteLog("Relation " + r.Id + " " + GetElementName(r.Tags) + " references ways not found in the file. Cannot process.");
                            break;
                        }
                    }
                    var listToRemoveLater = shapeList.ToList();

                    //Now we have our list of Ways. Check if there's lines that need made into a polygon.
                    if (shapeList.Any(s => s.nds.Count == 0))
                    {
                        Log.WriteLog("Relation " + r.Id + " " + GetElementName(r.Tags) + " has ways with 0 nodes.");
                    }

                    var poly = GetPolygonFromWays(shapeList, r);
                    if (poly == null)
                    {
                        //error converting it
                        Log.WriteLog("Relation " + r.Id + " " + GetElementName(r.Tags) + " failed to get a polygon from ways. Error.");
                        continue;
                    }

                    MapData md = new MapData();
                    md.name = GetElementName(r.Tags);
                    md.type = MapSupport.GetType(r.Tags);
                    md.RelationId = r.Id.Value;
                    
                    if (!poly.Shell.IsCCW)
                        poly = (Polygon)poly.Reverse();
                    if (!poly.Shell.IsCCW)
                    {
                        Log.WriteLog("Relation " + r.Id + " " + GetElementName(r.Tags) + " after change to polygon, still isn't CCW in either order.");
                        continue;
                    }

                    md.place = poly;
                    processedEntries.Add(md);
                    
                    //Now remove these ways to be excluded from later processing, since they're already handled as a mapdata entry for a relation.
                    foreach (var mw in listToRemoveLater)
                    {
                        ways.Remove(mw);
                    }
                    //} //end turn-lines-into-polygon block
                    //TODO: parse inner and outer polygons to correctly create empty spaces inside larger shape.
                }

                processedEntries.AddRange(ways.Select(w =>ConvertWayToMapData(w)));

                WriteMapDataToFile(destFolder + destFilename + "-MapData.json", ref processedEntries);
                Log.WriteLog("Processed " + filename + " at " + DateTime.Now);
                File.Move(filename, filename + "Done"); //We made it all the way to the end, this file is done.
                ways = null;
                Log.WriteLog("Manually calling GC at " + DateTime.Now);
                GC.Collect(); //Ask to clean up memory. Takes about a second on small files, a while if we've been paging to disk.
            }
        }

        private static Polygon GetPolygonFromWays(List<Way> shapeList, OsmSharp.Relation r)
        {
            //A common-ish case looks like the outer entries are lines that join togetehr, and inner entries are polygons.
            //Let's see if we can build a polygon (or more, possibly)
            List<Coordinate> possiblePolygon = new List<Coordinate>();
            //from the first line, find the line that starts with the same endpoint (or ends with the startpoint, but reverse that path).
            //continue until a line ends with the first node. That's a closed shape.

            if (shapeList.Count == 0)
            {
                Log.WriteLog("Relation " + r.Id + " " + GetElementName(r.Tags) + " has 0 ways in shapelist?");
                return null;
            }

            //Remove inner polygons from sets for now.
            var innerEntries = r.Members.Where(m => m.Role == "inner").Select(m => m.Id).ToList();
            var outerEntries = r.Members.Where(m => m.Role == "outer").Select(m => m.Id).ToList();
            List<Way> innerPolys = new List<Way>();

            //Not all ways are tagged for this, so we can't always rely on this.
            if (outerEntries.Count > 0)
                shapeList = shapeList.Where(s => outerEntries.Contains(s.id)).ToList();

            if (innerEntries.Count > 0)
                innerPolys = shapeList.Where(s => innerEntries.Contains(s.id)).ToList();

            var firstShape = shapeList.First();
            shapeList.Remove(firstShape);
            var nextStartnode = firstShape.nds.Last();
            var closedShape = false;
            var isError = false;
            possiblePolygon.AddRange(firstShape.nds.Where(n => n.id != nextStartnode.id).Select(n => new Coordinate(n.lon, n.lat)).ToList());
            while (closedShape == false)
            {
                var allPossibleLines = shapeList.Where(s => s.nds.First().id == nextStartnode.id).ToList();
                if (allPossibleLines.Count > 1)
                {
                    Log.WriteLog("Relation "  +r.Id + " " + GetElementName(r.Tags) + " has multiple possible lines to follow, might not process correctly.");
                }
                var lineToAdd = shapeList.Where(s => s.nds.First().id == nextStartnode.id && s.nds.First().id != s.nds.Last().id).FirstOrDefault();
                if (lineToAdd == null)
                {
                    //check other direction
                    var allPossibleLinesReverse = shapeList.Where(s => s.nds.Last().id == nextStartnode.id).ToList();
                    if (allPossibleLinesReverse.Count > 1)
                    {
                        Log.WriteLog("Way has multiple possible lines to follow, might not process correctly (Reversed Order).");
                    }
                    lineToAdd = shapeList.Where(s => s.nds.Last().id == nextStartnode.id && s.nds.First().id != s.nds.Last().id).FirstOrDefault(); 
                    if (lineToAdd == null)
                    {
                        Log.WriteLog("Relation " + r.Id + " " + GetElementName(r.Tags) + " doesn't seem to have properly connecting lines, can't process");
                        closedShape = true;
                        isError = true;
                    }
                    else
                        lineToAdd.nds.Reverse();
                }
                if (!isError)
                {
                    possiblePolygon.AddRange(lineToAdd.nds.Where(n => n.id != nextStartnode.id).Select(n => new Coordinate(n.lon, n.lat)).ToList());
                    nextStartnode = lineToAdd.nds.Last();
                    shapeList.Remove(lineToAdd);

                    if (possiblePolygon.First().Equals(possiblePolygon.Last()))
                        closedShape = true;
                }
            }
            if (isError)
                return null;

            if (possiblePolygon.Count <= 3)
            {
                Log.WriteLog("Relation " + r.Id + " " + GetElementName(r.Tags) + " didn't find enough points to turn into a polygon. Probably an error.");
                return null;
            }

            var factory = NtsGeometryServices.Instance.CreateGeometryFactory(srid: 4326);
            var poly = factory.CreatePolygon(possiblePolygon.ToArray());
            //TODO: add inner rings
            foreach (var ir in innerPolys)
            {
                if (ir.nds.First().id == ir.nds.Last().id)
                {
                    var innerP = factory.CreateLineString(WayToCoordArray(ir));
                    poly.InteriorRings.Append(innerP);
                }
            }
            return poly;
        }

        private static List<OsmSharp.Relation> GetRelationsFromPbf(string filename)
        {
            List<OsmSharp.Relation> filteredRelations = new List<OsmSharp.Relation>();
            using (var fs = File.OpenRead(filename))
            {
                var source = new PBFOsmStreamSource(fs);

                var progress = source.ShowProgress();

                //filter out data here
                //Now this is my default filter.
                filteredRelations = progress.Where(p => p.Type == OsmSharp.OsmGeoType.Relation &&
                        (p.Tags.Contains("natural", "water") ||
                        p.Tags.Contains("natural", "wetlands") ||
                        p.Tags.Contains("leisure", "park") ||
                        p.Tags.Contains("natural", "beach") ||
                        p.Tags.Contains("leisure", "beach_resort") ||
                        p.Tags.Contains("amenity", "university") ||
                        p.Tags.Contains("amenity", "college") ||
                        p.Tags.Contains("leisure", "nature_reserve") ||
                        p.Tags.Contains("landuse", "cemetery") ||
                        p.Tags.Contains("amenity", "grave_yard") ||
                        p.Tags.Contains("shop", "mall") ||
                        p.Tags.Contains("landuse", "retail") ||
                        p.Tags.Any(t => t.Key == "historic") ||
                        p.Tags.Any(t => t.Key == "waterway") || //newest tag, lets me see a lot more rivers and such.
                        p.Tags.Any(t => t.Key == "tourism" && relevantTourismValues.Contains(t.Value)) ||
                        p.Tags.Any(t => t.Key == "highway" && relevantHighwayValues.Contains(t.Value))
                        ))
                    .Select(r => (OsmSharp.Relation)r)
                    .ToList();

                source.Dispose();
            }
            return filteredRelations;
        }

        public static List<OsmSharp.Way> GetWaysFromPbf(string filename, ILookup<long, RelationMember> wayList)
        {
            //REMEMBER: if tag combos get edited, here, also update GetType to look at that combo too, or else data looks wrong.
            List<OsmSharp.Way> filteredWays = new List<OsmSharp.Way>();
            using (var fs = File.OpenRead(filename))
            {
                var source = new PBFOsmStreamSource(fs);

                var progress = source.ShowProgress();

                //filter out data here
                //Now this is my default filter.
                filteredWays = progress.Where(p => p.Type == OsmSharp.OsmGeoType.Way &&
                    (wayList[p.Id.Value].Count() > 0 ||
                        (p.Tags.Contains("natural", "water") ||
                        p.Tags.Contains("natural", "wetlands") ||
                        p.Tags.Contains("leisure", "park") ||
                        p.Tags.Contains("natural", "beach") ||
                        p.Tags.Contains("leisure", "beach_resort") ||
                        p.Tags.Contains("amenity", "university") ||
                        p.Tags.Contains("amenity", "college") ||
                        p.Tags.Contains("leisure", "nature_reserve") ||
                        p.Tags.Contains("landuse", "cemetery") ||
                        p.Tags.Contains("amenity", "grave_yard") ||
                        p.Tags.Contains("shop", "mall") ||
                        p.Tags.Contains("landuse", "retail") ||
                        p.Tags.Any(t => t.Key == "historic") ||
                        p.Tags.Any(t => t.Key == "waterway") ||
                        p.Tags.Any(t => t.Key == "tourism" && relevantTourismValues.Contains(t.Value)) ||
                        p.Tags.Any(t => t.Key == "highway" && relevantHighwayValues.Contains(t.Value) && (!p.Tags.Any(t => t.Key == "footway" && (t.Value == "sidewalk" || t.Value == "crossing"))))
                        )))
                    .Select(w => (OsmSharp.Way)w)
                    .ToList();
                source.Dispose();
            }
            return filteredWays;
        }

        public static void GetMinimumDataFromPbf()
        {
            //A baseline import of OsmData. Only contains data I need to make stuff into geography entries.
            //Much larger in SQL Server this way than the original PBF, or storing Geography data.
            var db = new GpsExploreContext();
            List<string> filenames = System.IO.Directory.EnumerateFiles(@"D:\Projects\OSM Server Info\XmlToProcess\", "*.pbf").ToList();
            foreach (var filename in filenames)
                using (var fs = File.OpenRead(filename))
                {
                    var source = new PBFOsmStreamSource(fs);
                    var progress = source.ShowProgress();

                    var minNodes = progress.Where(p => p.Type == OsmSharp.OsmGeoType.Node)
                        .Select(w => new MinimumNode() { MinimumNodeId = w.Id, Lat = ((OsmSharp.Node)w).Latitude, Lon = ((OsmSharp.Node)w).Longitude })
                        .ToList();

                    db.BulkInsert<MinimumNode>(minNodes);
                    var nodeLookup = minNodes.ToLookup(k => k.MinimumNodeId, v => v);
                    minNodes = null;


                    var ways = progress.Where(p => p.Type == OsmSharp.OsmGeoType.Way)
                        .Select(w => (OsmSharp.Way)w)
                        .ToList();

                    //This is the slow loop in this function
                    List<MinimumWay> minways = new List<MinimumWay>();
                    foreach (var w in ways)
                    {
                        List<long> nodeIds = w.Nodes.ToList();
                        var nodes = nodeLookup.Where(n => nodeIds.Contains(n.Key.Value)).Select(n => n.First()).ToList();
                        MinimumWay mw = new MinimumWay() { MinimumWayId = w.Id, Nodes = nodes };
                        minways.Add(mw);
                    }

                    db.BulkInsert<MinimumWay>(minways);

                }
            return;
        }

        public static List<NodeReference> GetNodesFromPbf(string filename, Lookup<long, long> nLookup)
        {
            //TODO:
            //Consider adding Abandoned buildings/areas, as a brave explorer sort of location?
            List<NodeReference> filteredNodes = new List<NodeReference>();
            using (var fs = File.OpenRead(filename))
            {
                var source = new PBFOsmStreamSource(fs);

                var progress = source.ShowProgress();

                //filter out data here
                //Now this is my default filter.
                //Single nodes are unlikley to be marked Highway. Don't check for that here.
                filteredNodes = progress.Where(p => p.Type == OsmSharp.OsmGeoType.Node &&
                       (nLookup.Contains(p.Id.GetValueOrDefault()) ||
                        (p.Tags.Contains("natural", "water") ||
                        p.Tags.Contains("natural", "wetlands") ||
                        p.Tags.Contains("leisure", "park") ||
                        p.Tags.Contains("leisure", "nature_reserve") ||
                        p.Tags.Contains("natural", "beach") ||
                        p.Tags.Contains("leisure", "beach_resort") ||
                        p.Tags.Contains("amenity", "university") ||
                        p.Tags.Contains("amenity", "college") ||
                        p.Tags.Contains("leisure", "nature_reserve") ||
                        p.Tags.Contains("landuse", "cemetery") ||
                        p.Tags.Contains("amenity", "grave_yard") ||
                        p.Tags.Contains("shop", "mall") ||
                        p.Tags.Contains("landuse", "retail") ||
                        p.Tags.Any(t => t.Key == "historic") ||
                        p.Tags.Any(t => t.Key == "tourism" && relevantTourismValues.Contains(t.Value))
                        )))
                    .Select(n => new NodeReference(n.Id.Value, ((OsmSharp.Node)n).Latitude.Value, ((OsmSharp.Node)n).Longitude.Value, GetElementName(n.Tags), MapSupport.GetType(n.Tags)))
                    .ToList();
            }
            return filteredNodes;
        }

        //This should go away soon, since MapData is replacing Ways for serialization.
        //public static void WriteRawWaysToFile(string filename, ref List<Way> ways)
        //{
        //    System.IO.StreamWriter sw = new StreamWriter(filename);
        //    sw.Write("[" + Environment.NewLine);
        //    foreach (var w in ways)
        //    {
        //        if (w != null && w.id > 0)
        //        {
        //            var test = JsonSerializer.Serialize(w, typeof(Way));
        //            sw.Write(test);
        //            sw.Write("," + Environment.NewLine);
        //        }
        //    }
        //    sw.Write("]");
        //    sw.Close();
        //    sw.Dispose();
        //    Log.WriteLog("All ways were serialized individually and saved to file at " + DateTime.Now);
        //}

        public static void WriteMapDataToFile(string filename, ref List<MapData> mapdata)
        {
            System.IO.StreamWriter sw = new StreamWriter(filename);
            sw.Write("[" + Environment.NewLine);
            foreach (var md in mapdata)
            {
                if (md != null)
                {
                    var recordVersion = new MapDataForJson(md.MapDataId, md.name, md.place.AsText(), md.type, md.WayId, md.NodeId, md.RelationId);
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

        //This should be removed soon in favor of MapData versions.
        public static List<Way> ReadRawWaysToMemory(string filename)
        {
            //Got out of memory errors trying to read files over 1GB through File.ReadAllText, so do those here this way.
            StreamReader sr = new StreamReader(filename);
            List<Way> lw = new List<Way>();
            JsonSerializerOptions jso = new JsonSerializerOptions();
            jso.AllowTrailingCommas = true;

            while (!sr.EndOfStream)
            {
                string line = sr.ReadLine();
                if (line == "[")
                {
                    //start of a file that spaced out every entry on a newline correctly. Skip.
                }
                else if (line.StartsWith("[") && line.EndsWith("]"))
                    lw.AddRange((List<Way>)JsonSerializer.Deserialize(line, typeof(List<Way>), jso)); //whole file is a list on one line. These shouldn't happen anymore.
                else if (line.StartsWith("[") && line.EndsWith(","))
                    lw.Add((Way)JsonSerializer.Deserialize(line.Substring(1, line.Count() - 2), typeof(Way), jso)); //first entry on a file before I forced the brackets onto newlines. Comma at end causes errors, is also trimmed.
                else if (line.StartsWith("]"))
                {
                    //dont do anything, this is EOF
                    Log.WriteLog("EOF Reached for " + filename + "at " + DateTime.Now);
                }
                else
                {
                    lw.Add((Way)JsonSerializer.Deserialize(line.Substring(0, line.Count() - 1), typeof(Way), jso)); //not starting line, trailing comma causes errors
                }
            }

            if (lw.Count() == 0)
                Log.WriteLog("No entries for " + filename + "? why?");

            sr.Close(); sr.Dispose();
            return lw;
        }

        public static List<MapData> ReadMapDataToMemory(string filename)
        {
            //Got out of memory errors trying to read files over 1GB through File.ReadAllText, so do those here this way.
            StreamReader sr = new StreamReader(filename);
            List<MapData> lm = new List<MapData>();
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
                else if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    var jsondata = (List<MapDataForJson>)JsonSerializer.Deserialize(line, typeof(List<MapDataForJson>), jso);

                    lm.AddRange(jsondata.Select(j => new MapData() { name = j.name, MapDataId = j.MapDataId, NodeId = j.NodeId, place = reader.Read(j.place), RelationId = j.RelationId, type = j.type, WayId = j.WayId  } )); //whole file is a list on one line. These shouldn't happen anymore.
                }
                else if (line.StartsWith("[") && line.EndsWith(","))
                {
                    MapDataForJson j = (MapDataForJson)JsonSerializer.Deserialize(line.Substring(1, line.Count() - 2), typeof(MapDataForJson), jso);
                    lm.Add(new MapData() { name = j.name, MapDataId = j.MapDataId, NodeId = j.NodeId, place = reader.Read(j.place), RelationId = j.RelationId, type = j.type, WayId = j.WayId }); //first entry on a file before I forced the brackets onto newlines. Comma at end causes errors, is also trimmed.
                }
                else if (line.StartsWith("]"))
                {
                    //dont do anything, this is EOF
                    Log.WriteLog("EOF Reached for " + filename + "at " + DateTime.Now);
                }
                else
                {
                    MapDataForJson j = (MapDataForJson)JsonSerializer.Deserialize(line.Substring(0, line.Count() - 1), typeof(MapDataForJson), jso);
                    lm.Add(new MapData() { name = j.name, MapDataId = j.MapDataId, NodeId = j.NodeId, place = reader.Read(j.place), RelationId = j.RelationId, type = j.type, WayId = j.WayId }); //first entry on a file before I forced the brackets onto newlines. Comma at end causes errors, is also trimmed.
                }
            }

            if (lm.Count() == 0)
                Log.WriteLog("No entries for " + filename + "? why?");

            sr.Close(); sr.Dispose();
            return lm;
        }

        public static void RemoveDuplicates()
        {
            //I might need to reconsider how i handle duplicates, since different files will have different pieces of some ways.
            Log.WriteLog("Scanning for duplicate entries at " + DateTime.Now);
            var db = new GpsExploreContext();
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            var dupedMapDatas = db.MapData.GroupBy(md => md.WayId)
                .Select(m => new { m.Key, Count = m.Count() })
                .ToDictionary(d => d.Key, v => v.Count)
                .Where(md => md.Value > 1);
            Log.WriteLog("Duped MapDAta loaded at " + DateTime.Now);

            foreach (var dupe in dupedMapDatas)
            {
                var entriesToDelete = db.MapData.Where(md => md.WayId == dupe.Key).ToList();
                db.MapData.RemoveRange(entriesToDelete.Skip(1));
                db.SaveChanges(); //so the app can make partial progress if it needs to restart
            }
            db.SaveChanges();
            Log.WriteLog("Duped MapData entries deleted at " + DateTime.Now);
        }

        public void ImportRawData()
        {
            //Pass in a file, do a simple read of each element to the database.
            //TODO: create base tables for these types.
            //TODO: read only core parts to DB
            //var nodes =
        }

        public void CreateStandaloneDB(GeoArea box)
        {
            //TODO: this whole feature.
            //Parameter: Relation? Area? Lat/Long box covering one of those?
            //pull in all ways that intersect that 
            //process all of the 10-cells inside that area with their ways (will need more types than the current game has)
            //save this data to an SQLite DB for the app to use.

            var mainDb = new GpsExploreContext();
            var sqliteDb = "placeholder";
            var factory = NtsGeometryServices.Instance.CreateGeometryFactory(srid: 4326); //SRID matches Plus code values. //share this here, so i compare the actual algorithms instead of this boilerplate, mandatory entry.
            var polygon = factory.CreatePolygon(MapSupport.MakeBox(box));

            var content = mainDb.MapData.Where(md => md.place.Intersects(polygon)).ToList();
            //var spoiConent = mainDb.SinglePointsOfInterests.Where(s => polygon.Intersects(new Point(s.lon, s.lat))).ToList(); //I think this is the right function, but might be Covers?

            //now, convert everything in content to 10-char plus code data.
            //Is the same logic as Cell6Info, so I should functionalize that.
        }

        public void AnalyzeAllAreas()
        {
            //This reads all cells on the globe, saves the results to a table.
            //Investigating if this is better or worse for a production setup.
            //Estimated to take 54 hours to process global data on my dev PC.
        }

        /* For reference: the tags Pokemon Go appears to be using. I don't need all of these. I have a few it doesn't, as well.
         * POkemon Go is using these as map tiles, not just content. This is not a maptile app.
    KIND_BASIN
    KIND_CANAL
    KIND_CEMETERY - Have
    KIND_CINEMA
    KIND_COLLEGE - Have
    KIND_COMMERCIAL
    KIND_COMMON
    KIND_DAM
    KIND_DITCH
    KIND_DOCK
    KIND_DRAIN
    KIND_FARM
    KIND_FARMLAND
    KIND_FARMYARD
    KIND_FOOTWAY
    KIND_FOREST
    KIND_GARDEN
    KIND_GLACIER
    KIND_GOLF_COURSE
    KIND_GRASS
    KIND_HIGHWAY
    KIND_HOSPITAL
    KIND_HOTEL
    KIND_INDUSTRIAL
    KIND_LAKE
    KIND_LAND
    KIND_LIBRARY
    KIND_MAJOR_ROAD
    KIND_MEADOW
    KIND_MINOR_ROAD
    KIND_NATURE_RESERVE - Have
    KIND_OCEAN
    KIND_PARK - Have
    KIND_PARKING
    KIND_PATH
    KIND_PEDESTRIAN
    KIND_PITCH
    KIND_PLACE_OF_WORSHIP
    KIND_PLAYA
    KIND_PLAYGROUND
    KIND_QUARRY
    KIND_RAILWAY
    KIND_RECREATION_AREA
    KIND_RESERVOIR
    KIND_RETAIL - Have
    KIND_RIVER
    KIND_RIVERBANK
    KIND_RUNWAY
    KIND_SCHOOL
    KIND_SPORTS_CENTER
    KIND_STADIUM
    KIND_STREAM
    KIND_TAXIWAY
    KIND_THEATRE
    KIND_UNIVERSITY - Have
    KIND_URBAN_AREA
    KIND_WATER - Have
    KIND_WETLAND - Have
    KIND_WOOD
         */
    }
}
