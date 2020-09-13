using OsmXmlParser.Classes;
using System;
using System.Collections.Generic;
using System.ComponentModel.Design.Serialization;
using System.Diagnostics.Tracing;
using System.IO.Enumeration;
using System.Linq;
using System.Net;
using System.Xml;
using System.Xml.Serialization;
using Google.OpenLocationCode;
using EFCore.BulkExtensions;
using DatabaseAccess;
using static DatabaseAccess.DbTables;
using NetTopologySuite.Geometries;
using Microsoft.EntityFrameworkCore.ValueGeneration.Internal;
using NetTopologySuite;
using System.Text.Json;
using System.IO;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Net.Http.Headers;
using Microsoft.EntityFrameworkCore.SqlServer.Query.Internal;
using DatabaseAccess.Migrations;
using OsmSharp.Streams;
using OsmSharp.Geo;
using Microsoft.EntityFrameworkCore;

//TODO: some node names are displaying in the debug console as "?????? ????". See Siberia. This should all be unicode and that should work fine.

namespace OsmXmlParser
{
    class Program
    {
        //NOTE: OSM data license allows me to use the data but requires acknowleging OSM as the data source
        //public static List<Bounds> bounds = new List<Bounds>();
        public static List<Node> nodes = new List<Node>();
        public static List<Way> ways = new List<Way>();
        //public static List<Relation> relations = new List<Relation>();

        public static Lookup<long, Node> nodeLookup;
        public static List<string> relevantTags = new List<string>() { "name", "natural", "leisure", "landuse", "amenity", "tourism", "historic", "highway" }; //The keys in tags we process to see if we want it included.
        public static List<string> relevantTourismValues = new List<string>() { "artwork", "attraction", "gallery", "museum", "viewpoint", "zoo" }; //The stuff we care about in the tourism category. Zoo and attraction are debatable.
        public static List<string> relevantHighwayValues = new List<string>() { "path", "bridleway", "cycleway", "footway"}; //The stuff we care about in the tourism category. Zoo and attraction are debatable.
        public static List<SinglePointsOfInterest> SPOI = new List<SinglePointsOfInterest>();

        public static string parsedJsonPath = @"D:\Projects\OSM Server Info\Trimmed JSON Files\";

        //Constants
        public static double PlusCode10Resolution = .000125;
        public static double PlusCode8Resolution = .0025;
        public static double PlusCode6Resolution = .05;

        static void Main(string[] args)
        {

            if (args.Count() == 0)
            {
                Console.WriteLine("You must pass an arguement to this application");
                return;
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
                nodes.Capacity = 10000000;
                ways.Capacity = 10000000;
                MakeAllSerializedFiles();
            }


            if (args.Any(a => a == "-trimPbfFiles"))
            {
                //nodes.Capacity = 10000000;
                ways.Capacity = 10000000;
                MakeAllSerializedFilesFromPBF();
            }

            if (args.Any(a => a == "-readSPOIs"))
            {
                AddSPOItoDBFromFiles(); //takes 13 seconds using bulkInserts. Takes far, far longer without.
            }

            if (args.Any(a => a == "-readRawWays"))
            {
                //Takes ~4 hours at last check.
                AddRawWaystoDBFromFiles();
            }

            if (args.Any(a => a == "-plusCodeSpois")) //should be removable now.
            {
                AddPlusCodesToSPOIs();
            }

            if (args.Any(a => a == "-removeDupes"))
            {
                //a few minutes
                RemoveDuplicates();
            }

            return;
        }

        public static void TryRawWay(Way w)
        {
            //Translate a Way into a Sql Server datatype. This was confirming the minumum logic for this.
            //Something fails if the polygon isn't a closed shape, so I should check if firstpoint == lastpoint, and if not add a copy of firstpoint as the last point?
            //Also fails if the points aren't counter-clockwise ordered. (Occasionally, a way fails to register as CCW in either direction)
            var factory = NtsGeometryServices.Instance.CreateGeometryFactory(srid: 4326); //SRID matches Plus code values.

            MapData md = new MapData();
            md.name = w.name;
            md.WayId = w.id;
            Polygon temp = factory.CreatePolygon(w.nds.Select(n => new Coordinate(n.lon, n.lat)).ToArray());
            if (!temp.Shell.IsCCW)
                temp = (Polygon)temp.Reverse();

            md.place = temp;
            GpsExploreContext db = new GpsExploreContext();
            db.MapData.Add(md);
            db.SaveChanges();
        }

        public static List<Tag> parseNodeTags(XmlReader xr)
        {
            List<Tag> retVal = new List<Tag>();

            while (xr.Read())
            {
                if (xr.Name == "tag")
                {
                    Tag t = new Tag() { k = xr.GetAttribute("k"), v = xr.GetAttribute("v") };
                    //I only want to include tags I'll refer to later.
                    if (relevantTags.Contains(t.k))
                        retVal.Add(t);
                }
            }
            return retVal;
        }

        public static void ParseWayDataV2(Way w, XmlReader xr)
        {
            List<Node> retVal = new List<Node>();

            while (xr.Read())
            {
                if (xr.Name == "nd")
                {
                    long nodeID = xr.GetAttribute("ref").ToLong();
                    w.nodRefs.Add(nodeID);
                }
                else if (xr.Name == "tag")
                {
                    Tag t = new Tag() { k = xr.GetAttribute("k"), v = xr.GetAttribute("v") };
                    //I only want to include tags I'll refer to later.
                    if (relevantTags.Contains(t.k))
                        w.tags.Add(t);
                }

                w.name = w.tags.Where(t => t.k == "name").Select(t => t.v.RemoveDiacritics()).FirstOrDefault();
                w.AreaType = GetType(w.tags);
            }
            return;
        }

        public static void AddSPOItoDBFromFiles()
        {
            GpsExploreContext db = new GpsExploreContext();

            foreach (var file in System.IO.Directory.EnumerateFiles(parsedJsonPath, "*-SPOIs.json"))
            {
                JsonSerializerOptions jso = new JsonSerializerOptions();
                jso.AllowTrailingCommas = true;
                List<SinglePointsOfInterest> entries = (List<SinglePointsOfInterest>)JsonSerializer.Deserialize(File.ReadAllText(file), typeof(List<SinglePointsOfInterest>), jso);

                entries = entries.Where(e => !string.IsNullOrWhiteSpace(e.name)).ToList(); //Some of these exist, they're not real helpful if they're just a tag.
                db.BulkInsert<SinglePointsOfInterest>(entries);
                Log.WriteLog("Added " + file + " to dB at " + DateTime.Now);
            }
        }

        public static void AddRawWaystoDBFromFiles()
        {
            //These are MapData items in the DB, unlike the other types that match names in code and DB tables.
            //This function is pretty slow. I should figure out how to speed it up.
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
                        Log.WriteLog("Processed " + loopCount + " ways so far"); //mdList.Count()
                        timer.Restart();
                    }
                    try
                    {
                        MapData md = new MapData();
                        md.name = w.name;
                        md.WayId = w.id;
                        md.type = w.AreaType;
                        
                        //Adding support for single lines. A lot of rivers and streams are treated this way.
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
                        //But this way is slow with ChangeTracking on, adding ~1 per second. ~2000 per second with Change Tracking off.
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
                    }
                }
                Log.WriteLog("Added " + file + " to dB at " + DateTime.Now);
                Log.WriteLog(errorCount + " ways excluded due to errors (" + ((errorCount / entries.Count()) * 100) + "%)");

                File.Move(file, file + "Done");
            }
        }

        public static string GetType(List<Tag> tags)
        {
            //This is how we will figure out which area a cell counts as now.
            //Should make sure all of these exist in the AreaTypes table I made.
            //TODO: prioritize these tags, since each space gets one value.

            if (tags.Count() == 0)
                return ""; //Shouldn't happen, but as a sanity check if we start adding Nodes later.

            //Water spaces should be displayed. Not sure if I want players to be in them for resources.
            //Water should probably override other values as a safety concern.
            if (tags.Any(t => t.k == "natural" && t.v == "water") 
                || tags.Any(t => t.k == "waterway"))
                return "water";

            //Wetlands should also be high priority.
            if (tags.Any(t => (t.k == "natural" && t.v == "wetland")))
                return "wetland";

            //Parks are good. Possibly core to this game.
            if (tags.Any(t => t.k == "leisure" && t.v == "park"))
                return "park";

            //Beaches are good. Managed beaches are less good but I'll count those too.
            if (tags.Any(t => (t.k == "natural" && t.v == "beach")
            || (t.k == "leisure" && t.v == "beach_resort")))
                return "beach";

            //Universities are good. Primary schools are not so good.  Don't include all education values.
            if (tags.Any(t => (t.k == "amenity" && t.v == "university")
                || (t.k == "amenity" && t.v == "college")))
                return "university";

            //Nature Reserve. Should be included, but possibly secondary to other types inside it.
            if (tags.Any(t => (t.k == "leisure" && t.v == "nature_reserve")))
                return "natureReserve";

            //Cemetaries are ok. They don't seem to appreciate Pokemon Go, but they're a public space and often encourage activity in them (thats not PoGo)
            if (tags.Any(t => (t.k == "landuse" && t.v == "cemetery")
                || (t.k == "amenity" && t.v == "grave_yard")))
                return "cemetery";

            //Malls are a good indoor area to explore.
            if (tags.Any(t => t.k == "shop" && t.v == "mall"))
                return "mall";

            //Generic shopping area is ok. I don't want to advertise businesses, but this is a common area type.
            if (tags.Any(t => (t.k == "landuse" && t.v == "retail")))
                return "retail";

            //I have tourism as a tag to save, but not necessarily sub-sets yet of whats interesting there.
            if (tags.Any(t => (t.k == "tourism" && relevantTourismValues.Contains(t.v))))
                return "tourism"; //TODO: create sub-values for tourism types?

            //I have historical as a tag to save, but not necessarily sub-sets yet of whats interesting there.
            //NOTE: the OSM tag doesn't match my value
            if (tags.Any(t => (t.k == "historic")))
                return "historical";

            //Trail. Will likely show up in varying places for various reasons. Trying to limit this down to hiking trails like in parks and similar.
            //In my local park, i see both path and footway used (Footway is an unpaved trail, Path is a paved one)
            //highway=track is for tractors and other vehicles.  Don't include. that.
            //highway=path is non-motor vehicle, and otherwise very generic. 5% of all Ways in OSM.
            //highway=footway is pedestrian traffic only, maybe including bikes. Mostly sidewalks, which I dont' want to include.
            //highway=bridleway is horse paths, maybe including pedestrians and bikes
            //highway=cycleway is for bikes, maybe including pedesterians.
            if (tags.Any(t => (t.k == "highway" && t.v == "bridleway"))
                || (tags.Any(t => t.k == "highway" && t.v == "path")) //path implies motor_vehicle = no // && !tags.Any(t => t.k =="motor_vehicle" && t.v == "yes")) //may want to check anyways?
                || (tags.Any(t => t.k == "highway" && t.v == "cycleway"))  //I probably want to include these too, though I have none local to see.
                || (tags.Any(t => t.k == "highway" && t.v == "footway") && !tags.Any(t => t.k == "footway" && (t.v == "sidewalk" || t.v == "crossing")))
                )
                return "trail";

            //Possibly of interest:
            //landuse:forest / landuse:orchard  / natural:wood
            //natural:sand may be of interest for desert area?
            //natural:spring / natural:hot_spring
            //Anything else seems un-interesting or irrelevant.

            return ""; //not a way we need to save right now.
        }

        public static string GetType(List<OsmSharp.Tags.Tag> tags)
        {
            List<Tag> converted = tags.Select(t => new Tag() { k = t.Key, v = t.Value }).ToList();
            return GetType(converted);
        }

        public static void CleanDb()
        {
            //Test function to put the DB back to empty.
            GpsExploreContext osm = new GpsExploreContext();
            osm.Database.SetCommandTimeout(900);

            osm.Database.ExecuteSqlRaw("TRUNCATE TABLE AreaTypes");
            Log.WriteLog("AreaTypes cleaned at " + DateTime.Now);

            osm.Database.ExecuteSqlRaw("TRUNCATE TABLE ProcessedWays");
            Log.WriteLog("ProcessedWays cleaned at " + DateTime.Now);

            osm.Database.ExecuteSqlRaw("TRUNCATE TABLE MapData");
            Log.WriteLog("MapData cleaned at " + DateTime.Now);

            osm.Database.ExecuteSqlRaw("TRUNCATE TABLE SinglePointsOfInterests");
            Log.WriteLog("SPOIs cleaned at " + DateTime.Now);

            Log.WriteLog("DB cleaned at " + DateTime.Now);
        }

        public static void MakeAllSerializedFiles()
        {
            //This function is meant to let me save time in the future by giving me the core data I can re-process without the extra I don't need.
            //It's also in a smaller format than XML to boot.
            //For later processsing, I will want to work on the smaller files (unless I'm adding data to this intermediate step).
            //Loading the smaller files directly to the in-memory representation would be so much faster than reading XML tag by tag.
            List<string> filenames = System.IO.Directory.EnumerateFiles(@"D:\Projects\OSM Server Info\XmlToProcess\", "*.osm").ToList();

            foreach (string filename in filenames)
            {
                ways = null;
                nodes = null;
                SPOI = null;
                ways = new List<Way>();
                nodes = new List<Node>();
                SPOI = new List<SinglePointsOfInterest>();

                Log.WriteLog("Starting " + filename + " way read at " + DateTime.Now);
                XmlReaderSettings xrs = new XmlReaderSettings();
                xrs.IgnoreWhitespace = true;
                XmlReader osmFile = XmlReader.Create(filename, xrs);
                osmFile.MoveToContent();

                System.Diagnostics.Stopwatch timer = new System.Diagnostics.Stopwatch();
                timer.Start();

                //We do this in 2 steps, because this seems to minimize memory-related errors
                //Read the Ways first, identify the ones we want by tags, and track which nodes get referenced by those ways.
                //The second run will load those nodes into memory to be add their data to the ways.
                string destFolder = @"D:\Projects\OSM Server Info\Trimmed JSON Files\";
                bool firstWay = true;
                bool exitEarly = false;
                //first read - save Way info to process later.
                while (osmFile.Read() && !exitEarly)
                {
                    if (timer.ElapsedMilliseconds > 10000)
                    {
                        //Report progress on the console.
                        Log.WriteLog("Processed " + ways.Count() + " ways so far");
                        timer.Restart();
                    }

                    switch (osmFile.Name)
                    {
                        case "way":
                            if (firstWay) { Log.WriteLog("First Way entry found at " + DateTime.Now); firstWay = false; }

                            var w = new Way();
                            w.id = osmFile.GetAttribute("id").ToLong();

                            ParseWayDataV2(w, osmFile.ReadSubtree()); //Saves a list of nodeRefs, doesnt look for actual nodes.

                            if (!string.IsNullOrWhiteSpace(w.AreaType))
                                ways.Add(w);
                            break;
                        case "relation":
                            exitEarly = true;
                            break;
                    }
                }

                osmFile.Close(); osmFile.Dispose(); exitEarly = false;
                osmFile = XmlReader.Create(filename, xrs);
                osmFile.MoveToContent();

                var nodesToSave = ways.SelectMany(w => w.nodRefs).ToHashSet<long>();

                //second read - load Node data for ways and SPOIs.
                while (osmFile.Read() && !exitEarly)
                {
                    if (timer.ElapsedMilliseconds > 10000)
                    {
                        //Report progress on the console.
                        Log.WriteLog("Processed " + nodes.Count() + " nodes so far");
                        timer.Restart();
                    }

                    switch (osmFile.Name)
                    {
                        case "node":
                            if (osmFile.NodeType == XmlNodeType.Element) //sometimes is EndElement if we had tags we ignored.
                            {
                                var n = new Node();
                                n.id = osmFile.GetAttribute("id").ToLong();
                                n.lat = osmFile.GetAttribute("lat").ToDouble();
                                n.lon = osmFile.GetAttribute("lon").ToDouble();
                                if (nodesToSave.Contains(n.id))
                                    if (n.id != null && n.lat != null && n.lon != null)
                                        nodes.Add(n);

                                //This data below doesn't need saved in RAM, so we remove it after processing for SPOI and don't include it in the base Node object.
                                var tags = parseNodeTags(osmFile.ReadSubtree());
                                string name = tags.Where(t => t.k == "name").Select(t => t.v).FirstOrDefault();
                                string nodetype = GetType(tags);
                                //Now checking if this node is individually interesting.
                                if (nodetype != "")
                                    SPOI.Add(new SinglePointsOfInterest() { name = name, lat = n.lat, lon = n.lon, NodeID = n.id, NodeType = nodetype, PlusCode = GetPlusCode(n.lat, n.lon) });
                            }
                            break;
                        case "way":
                            exitEarly = true;
                            break;
                    }
                }

                Log.WriteLog("Attempting to convert " + nodes.Count() + " nodes to lookup at " + DateTime.Now);
                nodeLookup = (Lookup<long, Node>)nodes.ToLookup(k => k.id, v => v);

                List<long> waysToRemove = new List<long>();
                foreach (Way w in ways)
                {
                    foreach (long nr in w.nodRefs)
                    {
                        w.nds.Add(nodeLookup[nr].FirstOrDefault());
                    }
                    w.nodRefs = null; //free up a little memory we won't use again.

                    //now that we have nodes, lets do a little extra processing.
                    var latSpread = w.nds.Max(n => n.lat) - w.nds.Min(n => n.lat);
                    var lonSpread = w.nds.Max(n => n.lon) - w.nds.Min(n => n.lon);

                    if (latSpread <= PlusCode10Resolution && lonSpread <= PlusCode10Resolution)
                    {
                        //this is small enough to be an SPOI instead
                        var calcedCode = new OpenLocationCode(w.nds.Average(n => n.lat), w.nds.Average(n => n.lon));
                        var reverseDecode = calcedCode.Decode();
                        var spoiFromWay = new SinglePointsOfInterest()
                        {
                            lat = reverseDecode.CenterLatitude,
                            lon = reverseDecode.CenterLongitude,
                            NodeType = w.AreaType,
                            PlusCode = calcedCode.Code.Replace("+", ""),
                            PlusCode8 = calcedCode.Code.Substring(0, 8),
                            name = w.name,
                            NodeID = w.id //Will have to remember this could be a node or a way in the future.
                        };
                        SPOI.Add(spoiFromWay);
                        waysToRemove.Add(w.id);
                        w.nds = null; //free up a small amount of RAM now instead of later.
                    }
                }
                //now remove ways we converted to SPOIs.
                foreach (var wtr in waysToRemove)
                    ways.Remove(ways.Where(w => w.id == wtr).FirstOrDefault());

                Log.WriteLog("Ways populated with Nodes at " + DateTime.Now);
                nodes = null; //done with these now, can free up RAM again.

                //Moved here while working on reading Ways for items that are too small.
                Log.WriteLog("Done reading Node objects at " + DateTime.Now);
                WriteSPOIsToFile(destFolder + System.IO.Path.GetFileNameWithoutExtension(filename) + "-SPOIs.json");
                SPOI = null;

                WriteRawWaysToFile(destFolder + System.IO.Path.GetFileNameWithoutExtension(filename) + "-RawWays.json");

                //I don't currently use the processed way data set, since spatial indexes are efficient enough
                //I might return to using an abbreviated data set, but not for now, and I'll need a better way to approximate this when I do.
                //List<ProcessedWay> pws = new List<ProcessedWay>();
                //foreach (Way w in ways)
                //{
                //    var pw = ProcessWay(w);
                //    if (pw != null)
                //        pws.Add(pw);
                //}
                //WriteProcessedWaysToFile(destFolder + System.IO.Path.GetFileNameWithoutExtension(filename) + "-ProcessedWays.json", ref pws);
                //pws = null;
                //nodeLookup = null;

                osmFile.Close(); osmFile.Dispose();
                Log.WriteLog("Processed " + filename + " at " + DateTime.Now);
                File.Move(filename, filename + "Done"); //We made it all the way to the end, this file is done.
            }
        }

        public static void MakeAllSerializedFilesFromPBF()
        {
            string destFolder = @"D:\Projects\OSM Server Info\Trimmed JSON Files\";
            List<string> filenames = System.IO.Directory.EnumerateFiles(@"D:\Projects\OSM Server Info\XmlToProcess\", "*.pbf").ToList();

            foreach (string filename in filenames)
            {
                string destFilename = System.IO.Path.GetFileName(filename).Replace(".osm.pbf", "");
                ways = null;
                SPOI = null;
                ways = new List<Way>();
                SPOI = new List<SinglePointsOfInterest>();

                Log.WriteLog("Starting " + filename + " way read at " + DateTime.Now);
                var osmWays = GetWaysFromPbf(filename);
                Lookup<long, long> nodeLookup = (Lookup<long, long>)osmWays.SelectMany(w => w.Nodes).Distinct().ToLookup(k => k, v => v);

                Log.WriteLog("Starting " + filename + " node read at " + DateTime.Now);
                var osmNodes = GetNodesFromPbf(filename, nodeLookup);
                var osmNodeLookup = osmNodes.ToLookup(k => k.Id.Value, v => v);

                //Write SPOIs to file
                Log.WriteLog("Finding SPOIs at " + DateTime.Now);
                var SpoiEntries = osmNodes.Where(n => n.Tags.Count() > 0 && GetType(n.Tags.ToList()) != "").ToList();
                SPOI = SpoiEntries.Select(s => new SinglePointsOfInterest()
                {
                    lat = s.Latitude.Value,
                    lon = s.Longitude.Value,
                    name = s.Tags.Where(t => t.Key == "name").FirstOrDefault().Value,
                    NodeID = s.Id.Value,
                    NodeType = GetType(s.Tags.ToList()),
                    PlusCode = GetPlusCode(s.Latitude.Value, s.Longitude.Value),
                    PlusCode8 = GetPlusCode(s.Latitude.Value, s.Longitude.Value).Substring(0, 8),
                    PlusCode6 = GetPlusCode(s.Latitude.Value, s.Longitude.Value).Substring(0, 6)
                }).ToList();
                SpoiEntries = null;

                Log.WriteLog("Converting OsmWays to my Ways at " + DateTime.Now);
                ways = osmWays.Select(w => new OsmXmlParser.Classes.Way()
                {
                    id = w.Id.Value,
                    name = w.Tags.Where(t => t.Key == "name").FirstOrDefault().Value.RemoveDiacritics(),
                    AreaType = GetType(w.Tags.ToList()),
                    nodRefs = w.Nodes.ToList()
                })
                .ToList();

                List<long> waysToRemove = new List<long>();
                foreach (Way w in ways)
                {
                    foreach (long nr in w.nodRefs)
                    {
                        var osmNode = osmNodeLookup[nr].FirstOrDefault();
                        var myNode = new Node() { id = osmNode.Id.Value, lat = osmNode.Latitude.Value, lon = osmNode.Longitude.Value };
                        w.nds.Add(myNode);
                    }
                    w.nodRefs = null; //free up a little memory we won't use again.

                    //now that we have nodes, lets do a little extra processing.
                    var latSpread = w.nds.Max(n => n.lat) - w.nds.Min(n => n.lat);
                    var lonSpread = w.nds.Max(n => n.lon) - w.nds.Min(n => n.lon);

                    if (latSpread <= PlusCode10Resolution && lonSpread <= PlusCode10Resolution)
                    {
                        //this is small enough to be an SPOI instead
                        var calcedCode = new OpenLocationCode(w.nds.Average(n => n.lat), w.nds.Average(n => n.lon));
                        var reverseDecode = calcedCode.Decode();
                        var spoiFromWay = new SinglePointsOfInterest()
                        {
                            lat = reverseDecode.CenterLatitude,
                            lon = reverseDecode.CenterLongitude,
                            NodeType = w.AreaType,
                            PlusCode = calcedCode.Code.Replace("+", ""),
                            PlusCode8 = calcedCode.Code.Substring(0, 8),
                            PlusCode6 = calcedCode.Code.Substring(0, 6),
                            name = w.name,
                            NodeID = w.id //Will have to remember this could be a node or a way in the future.
                        };
                        SPOI.Add(spoiFromWay);
                        waysToRemove.Add(w.id);
                        w.nds = null; //free up a small amount of RAM now instead of later.
                    }
                }
                //now remove ways we converted to SPOIs.
                foreach (var wtr in waysToRemove)
                    ways.Remove(ways.Where(w => w.id == wtr).FirstOrDefault());

                Log.WriteLog("Ways populated with Nodes at " + DateTime.Now);
                osmNodes = null; //done with these now, can free up RAM again.

                WriteSPOIsToFile(destFolder + destFilename + "-SPOIs.json");
                SPOI = null;

                WriteRawWaysToFile(destFolder + destFilename + "-RawWays.json");
                Log.WriteLog("Processed " + filename + " at " + DateTime.Now);
                File.Move(filename, filename + "Done"); //We made it all the way to the end, this file is done.
                ways = null;
                GC.Collect(); //Ask to clean up memory

            }
        }

        public static List<OsmSharp.Way> GetWaysFromPbf(string filename)
        {
            List<OsmSharp.Way> filteredWays = new List<OsmSharp.Way>();
            using (var fs = File.OpenRead(filename))
            {
                var source = new PBFOsmStreamSource(fs);

                var progress = source.ShowProgress();

                //filter out data here
                //Now this is my default filter.
                filteredWays = progress.Where(p => p.Type == OsmSharp.OsmGeoType.Way && // || p.Type == OsmSharp.OsmGeoType.Node &&
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
                    .Select(w => (OsmSharp.Way)w)
                    .ToList();
            }
            return filteredWays;
        }

        public static List<OsmSharp.Node> GetNodesFromPbf(string filename, Lookup<long, long> nLookup)
        {
            //TODO:
            //Consider adding Abandoned buildings/areas, as a brave explorer sort of location?
            List<OsmSharp.Node> filteredNodes = new List<OsmSharp.Node>();
            using (var fs = File.OpenRead(filename))
            {
                var source = new PBFOsmStreamSource(fs);

                var progress = source.ShowProgress();

                //filter out data here
                //Now this is my default filter.
                filteredNodes = progress.Where(p => p.Type == OsmSharp.OsmGeoType.Node &&
                       (nLookup.Contains(p.Id.GetValueOrDefault()) ||
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
                        p.Tags.Any(t => t.Key == "tourism" && relevantTourismValues.Contains(t.Value))
                        )))
                    .Select(n => (OsmSharp.Node)n)
                    .ToList();
            }
            return filteredNodes;
        }

        public static void WriteRawWaysToFile(string filename)
        {
            System.IO.StreamWriter sw = new StreamWriter(filename);
            sw.Write("[" + Environment.NewLine);
            foreach (var w in ways)
            {
                if (w != null)
                {
                    var test = JsonSerializer.Serialize(w, typeof(Way));
                    sw.Write(test);
                    sw.Write("," + Environment.NewLine);
                }
            }
            sw.Write("]");
            sw.Close();
            sw.Dispose();
            Log.WriteLog("All ways were serialized individually and saved to file at " + DateTime.Now);
        }

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

        //Also not using this right now.
        //public static void WriteProcessedWaysToFile(string filename, ref List<ProcessedWay> pws)
        //{
        //    System.IO.StreamWriter sw = new StreamWriter(filename);
        //    sw.Write("[" + Environment.NewLine);
        //    foreach (var pw in pws)
        //    {
        //        var test = JsonSerializer.Serialize(pw, typeof(ProcessedWay));
        //        sw.Write(test);
        //        sw.Write("," + Environment.NewLine);
        //    }
        //    sw.Write("]");
        //    sw.Close();
        //    sw.Dispose();
        //    Log.WriteLog("All processed ways were serialized individually and saved to file at " + DateTime.Now);
        //}

        public static void WriteSPOIsToFile(string filename)
        {
            System.IO.StreamWriter sw = new StreamWriter(filename);
            sw.Write("[" + Environment.NewLine);
            foreach (var s in SPOI)
            {
                var test = JsonSerializer.Serialize(s, typeof(SinglePointsOfInterest));
                sw.Write(test);
                sw.Write("," + Environment.NewLine);
            }
            sw.Write("]");
            sw.Close();
            sw.Dispose();
            Log.WriteLog("All SPOIs were serialized individually and saved to file at " + DateTime.Now);
        }

        public static string GetPlusCode(double lat, double lon)
        {
            return OpenLocationCode.Encode(lat, lon).Replace("+", "");
        }

        public static void AddPlusCodesToSPOIs()
        {
            //Should only need to run this once, since I want to add these to the return stream. Took about a minute to do.
            var db = new GpsExploreContext();
            var spois = db.SinglePointsOfInterests.ToList();
            foreach (var spoi in spois)
            {
                spoi.PlusCode = GetPlusCode(spoi.lat, spoi.lon);
            }

            db.SaveChanges();
        }

        public static void AddPlusCode8sToSPOIs()
        {
            //Should only need to run this once, since I want to add these to the return stream. Took about a minute to do.
            var db = new GpsExploreContext();
            var spois = db.SinglePointsOfInterests.ToList();
            foreach (var spoi in spois)
            {
                spoi.PlusCode8 = GetPlusCode(spoi.lat, spoi.lon).Substring(0, 8);
            }

            db.SaveChanges();
        }

        public static void RemoveDuplicates()
        {
            var db = new GpsExploreContext();
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            var dupedWays = db.MapData.GroupBy(md => md.WayId)
                .Select(m => new { m.Key, Count = m.Count() })
                .ToDictionary(d => d.Key, v => v.Count)
                .Where(md => md.Value > 1);

            foreach (var dupe in dupedWays)
            {
                var entriesToDelete = db.MapData.Where(md => md.WayId == dupe.Key).ToList();
                db.MapData.RemoveRange(entriesToDelete.Skip(1));
            }
            db.SaveChanges();

            var dupedSpoi = db.SinglePointsOfInterests.GroupBy(md => md.NodeID)
                .Select(m => new { m.Key, Count = m.Count() })
                .ToDictionary(d => d.Key, v => v.Count)
                .Where(md => md.Value > 1);

            foreach (var dupe in dupedSpoi)
            {
                var entriesToDelete = db.SinglePointsOfInterests.Where(md => md.NodeID == dupe.Key).ToList();
                db.SinglePointsOfInterests.RemoveRange(entriesToDelete.Skip(1));
            }
            db.SaveChanges();


        }

        /* For reference: the tags Pokemon Go appears to be using. I don't need all of these. I have a few it doesn't, as well.
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
