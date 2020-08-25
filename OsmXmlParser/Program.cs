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

//Next TODO: clean up comments and scattered test code.

namespace OsmXmlParser
{
    class Program
    {
        //NOTE: OSM data license allows me to use the data but requires acknowleging OSM as the data source
        public static List<Bounds> bounds = new List<Bounds>();
        public static List<Node> nodes = new List<Node>();
        public static List<Way> ways = new List<Way>();
        public static List<Relation> relations = new List<Relation>();

        public static Lookup<long, Node> nodeLookup;

        public static List<string> relevantTags = new List<string>() { "name", "natural", "leisure", "landuse", "amenity", "tourism", "historic" }; //The keys in tags we process to see if we want it included.
        public static List<string> relevantTourismValues = new List<string>() { "artwork", "attraction", "gallery", "museum", "viewpoint", "zoo" }; //The stuff we care about in the tourism category. Zoo and attraction are debatable.
        public static List<InterestingPoint> IPs = new List<InterestingPoint>();
        public static List<SinglePointsOfInterest> SPOI = new List<SinglePointsOfInterest>();

        static void Main(string[] args)
        {
            //TODO: parallelize parsing where possible. reading a single XML file isn't parallelizable.
            //CleanDb(); //testing, to start on an empty DB each time.

            string parsedJsonPath = @"D:\Projects\OSM Server Info\Trimmed JSON Files\";

            //LoadPreviouslyParsedWayData(parsedJsonPath + "LocalCity-RawWays.json");
            //LoadPreviouslyParsedSPOIData(parsedJsonPath + "quebec-latest-SPOIs.json");

            //trying to reduce the amount of resizing done by setting this to a better starting amount.
            nodes.Capacity = 10000000;
            ways.Capacity =  10000000;
            MakeAllSerializedFiles();
            return;
        }

        public static void TryRawWay(Way w)
        {
            //Translate a Way into a Sql Server datatype. This was confirming the minumum logic for this.
            //Something fails if the polygon isn't a closed shape, so I should check if firstpoint == lastpoint, and if not add a copy of firstpoint as the last point?
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
                    //Skip this step now, i have a full list of nodes in memory.
                    long nodeID = xr.GetAttribute("ref").ToLong();
                    w.nodRefs.Add(nodeID);

                    //w.nds.Add((Node)nodeLookup[xr.GetAttribute("ref").ToLong()]);
                    //long id = xr.GetAttribute("ref").ToLong();
                    //w.nds.Add(nodeLookup[id].FirstOrDefault());
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

        public static void ParseXmlV3()
        {
            //PArseXML needs an additional rethink: I'm using MakeAllSerializedFiles to reduce the amount of processing.
            //V4 might need to check for JSON files, and if so work on those instead. 
            //If the JSON is missing, then read and parse the XML data instead to fill the List<>s?


            //Third re-think. Read the file once.
            //Nodes: if a node has interesting tags, make it a SinglePointOfInterest. Otherwise, throw it in the list.
            //Ways: works roughtly the same, except I don't need the temp-list. i have the nodes already.
            //Relations: skip, as usual.

            //Results:
            //This takes 12 seconds to process Jamaica's 500MB data, including DB writes (without InterestingPoints)
            //My local city takes 2 seconds with DB writes and JSON serialization
            //US Midwest takes ? to read and write.

            Console.WriteLine("Starting Node read at " + DateTime.Now);
            //TODO: read xml from zip file. Pretty sure I can stream zipfile data to save HD space, since global XML data needs 1TB of space unzipped.
            //string filename = @"..\..\..\jamaica-latest.osm"; //500MB, a reasonable test scenario for speed/load purposes.
            string filename = @"C:\Users\Drake\Downloads\us-midwest-latest.osm\us-midwest-latest.osm"; //This one takes much longer to process, 28 GB
            //string filename = @"C:\Users\Drake\Downloads\LocalCity.osm"; //stuff I can actually walk to. 4MB
            XmlReaderSettings xrs = new XmlReaderSettings();
            xrs.IgnoreWhitespace = true;
            XmlReader osmFile = XmlReader.Create(filename, xrs);
            osmFile.MoveToContent();

            System.Diagnostics.Stopwatch timer = new System.Diagnostics.Stopwatch();
            timer.Start();

            bool firstWay = true;
            bool exitEarly = false;
            while (osmFile.Read() && !exitEarly)
            {
                if (timer.ElapsedMilliseconds > 10000)
                {
                    //Report progress on the console.
                    Console.WriteLine("Processed " + nodes.Count() + " nodes and " + ways.Count() + " ways so far");
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
                            nodes.Add(n);
                            //This data below doesn't need saved in RAM, so we remove it after processing for SPOI and don't include it in the base Node object.
                            var tags = parseNodeTags(osmFile.ReadSubtree());
                            string name = tags.Where(t => t.k == "name").Select(t => t.v).FirstOrDefault();
                            string nodetype = GetType(tags);
                            //Now checking if this node is individually interesting.
                            if (nodetype != "") //a name along is not interesting
                                SPOI.Add(new SinglePointsOfInterest() { name = name, lat = n.lat, lon = n.lon, NodeID = n.id, NodeType = nodetype });
                        }
                        break;
                    case "way":
                        if (firstWay) { Console.WriteLine("First Way entry found at " + DateTime.Now); firstWay = false; }

                        //way will contain 'nd' elements with 'ref' to a node by ID.
                        //may contain tag, k is the type of thing it is.
                        var w = new Way();
                        w.id = osmFile.GetAttribute("id").ToLong();

                        ParseWayDataV2(w, osmFile.ReadSubtree()); //Saves a list of nodeRefs, doesnt look for actual nodes yet.

                        if (w.AreaType != "")
                            ways.Add(w);
                        break;
                    case "relation":
                        //we're done with way entries.
                        exitEarly = true;
                        break;
                }
            }
            Console.WriteLine("Done reading Way objects at " + DateTime.Now);

            //We got at least partway through processing, save these now.
            File.WriteAllText(System.IO.Path.GetFileNameWithoutExtension(filename) + "-Nodes.json", JsonSerializer.Serialize(nodes));
            File.WriteAllText(System.IO.Path.GetFileNameWithoutExtension(filename) + "-RawWays.json", JsonSerializer.Serialize(ways));
            File.WriteAllText(System.IO.Path.GetFileNameWithoutExtension(filename) + "-SPOI.json", JsonSerializer.Serialize(SPOI));
            Console.WriteLine("Stuff dumped to JSON at " + DateTime.Now);

            //Time to actually create the game objects.
            List<ProcessedWay> pws = new List<ProcessedWay>();
            foreach (Way w in ways)
            {
                var pw = ProcessWay(w);
                if (pw != null)
                    pws.Add(pw);
            }
            Console.WriteLine("Ways processed at " + DateTime.Now);

            //Converting this to Json to minimize disk space. Json's about half the size.
            File.WriteAllText(System.IO.Path.GetFileNameWithoutExtension(filename) + "-ProcessedWays.json", JsonSerializer.Serialize(pws));
            Console.WriteLine("ProcessedWays dumped to JSON at " + DateTime.Now);

            //database work now.
            GpsExploreContext db = new GpsExploreContext();
            db.BulkInsert<ProcessedWay>(pws);
            Console.WriteLine("Processed Ways saved to DB at " + DateTime.Now);

            db.BulkInsert<InterestingPoint>(IPs);
            Console.WriteLine("Interesting Points saved to DB at " + DateTime.Now);

            db.BulkInsert<SinglePointsOfInterest>(SPOI);
            Console.WriteLine("SPOI saved to DB at " + DateTime.Now);

            //Make SQL geometry entries.
            var factory = NtsGeometryServices.Instance.CreateGeometryFactory(srid: 4326); //SRID matches Plus code values.
            List<MapData> mapDatas = new List<MapData>();
            foreach (Way w in ways)
            {
                //TODO: try/catch this, if the way's points don't make a closed polygon this errors out.
                try
                {
                    MapData md = new MapData();
                    md.name = w.name;
                    md.type = w.AreaType;
                    md.WayId = w.id;
                    Polygon temp = factory.CreatePolygon(w.nds.Select(n => new Coordinate(n.lon, n.lat)).ToArray());
                    if (!temp.Shell.IsCCW)
                        temp = (Polygon)temp.Reverse();

                    md.place = temp;

                    mapDatas.Add(md);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Error making MapData entry: " + ex.Message);
                }
            }
            //db.BulkInsert<MapData>(mapDatas); //Throws errors on data type.
            db.MapData.AddRange(mapDatas);
            db.SaveChanges(); //bulkInsert might not work on this type
            Console.WriteLine("SQL Geometry values converted and saved to DB at " + DateTime.Now);

        }

        public static void MakeSqlEntries()
        {
            //temp copy of this logic from ParseXMLV3 before removing it
            //database work now.
            GpsExploreContext db = new GpsExploreContext();
            //db.BulkInsert<ProcessedWay>(pws); this isnt a global.
            Console.WriteLine("Processed Ways saved to DB at " + DateTime.Now);

            db.BulkInsert<InterestingPoint>(IPs);
            Console.WriteLine("Interesting Points saved to DB at " + DateTime.Now);

            db.BulkInsert<SinglePointsOfInterest>(SPOI);
            Console.WriteLine("SPOI saved to DB at " + DateTime.Now);



            var factory = NtsGeometryServices.Instance.CreateGeometryFactory(srid: 4326); //SRID matches Plus code value for this..
            List<MapData> mapDatas = new List<MapData>();
            foreach (Way w in ways)
            {
                try
                {
                    MapData md = new MapData();
                    md.name = w.name;
                    md.type = w.AreaType;
                    md.WayId = w.id;
                    Polygon temp = factory.CreatePolygon(w.nds.Select(n => new Coordinate(n.lon, n.lat)).ToArray());
                    if (!temp.Shell.IsCCW)
                        temp = (Polygon)temp.Reverse();

                    md.place = temp;

                    mapDatas.Add(md);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Error making MapData entry: " + ex.Message);
                    //Errors to consider handling/fixing: if the points aren't a closed shape, make a copy of the first point and make that the last point to close it.
                }
            }
            //db.BulkInsert<MapData>(mapDatas); //Throws errors on data type. It might not support this feature.
            db.MapData.AddRange(mapDatas);
            db.SaveChanges(); 
            Console.WriteLine("SQL Geometry values converted and saved to DB at " + DateTime.Now);
        }

        public static void LoadPreviouslyParsedWayData(string filename)
        {
            ways = (List<Way>)JsonSerializer.Deserialize(File.ReadAllText(filename), typeof(List<Way>));
        }

        public static void LoadPreviouslyParsedNodeData(string filename)
        {
            nodes = (List<Node>)JsonSerializer.Deserialize(File.ReadAllText(filename), typeof(List<Node>));
        }

        public static void LoadPreviouslyParsedSPOIData(string filename)
        {
            SPOI = (List<SinglePointsOfInterest>)JsonSerializer.Deserialize(File.ReadAllText(filename), typeof(List<SinglePointsOfInterest>));
        }

        public static ProcessedWay ProcessWay(Way w)
        {
            //Neither of these should happen, but they have.
            if (w == null)
                return null;

            if (w.nds.Count() == 0)
                return null;

            //A few files are missing a node(s), we can't use those ways.
            if (w.nds.Any(n => n == null))
                return null; 

            //Version 1.
            //Convert the list of nodes into a rectangle that covers the full bounds.
            //Is 'close enough' for now, should be fairly quick compared to more accurate calculations.
            //I may not want to use this for all elements, or find a better option for some extremely large 
            //but important elements (EX: Lake Erie will cover Cleveland entirely using this approximation.
            ProcessedWay pw = new ProcessedWay();
            pw.OsmWayId = w.id;
            pw.lastUpdated = DateTime.Now;
            pw.name = w.tags.Where(t => t.k == "name").Select(t => t.v).FirstOrDefault();
            pw.AreaType = w.AreaType; //Should become a FK int to save space later. Is a string for now.

            pw.latitudeS = w.nds.Min(n => n.lat); //smaller numbers are south
            pw.longitudeW = w.nds.Min(n => n.lon); //smaller numbers are west.
            pw.distanceN = w.nds.Max(n => n.lat) - pw.latitudeS; //should be a positive number now.
            pw.distanceE = w.nds.Max(n => n.lon) - pw.longitudeW; //same          

            return pw; //This can be used to make InterestingPoints for the actual app to read.

            //Theory for Version 2:
            //I could probably use the actual geolocation stuff in SQL Server 
            //and see if any Way (before this approximate processing) .Contains() the center of a Plus code.
            //Might need to start at the 8cell level, and if anything hits there, THEN check for 10cells contained by it?
            //This is the MapData class that stores the whole vector list of nodes in SQL Server
        }

        public static void MakePoints(List<ProcessedWay> pws)
        {
            //Version 1:
            //Take the southwest area 

            double resolution = .000125; //the size in degress of a PlusCode cell at the 10-digit level.
            foreach (ProcessedWay pw in pws)
            {
                //make first point. NO, don't i'll do that on the 0/0 loop.
                //OpenLocationCode plusCodeMain = new OpenLocationCode(pw.latitudeS, pw.longitudeW);
                int cellsWide = (int)(pw.distanceE / resolution); //Plus code resolution in degrees.
                int cellsTall = (int)(pw.distanceN / resolution); //Plus code resolution in degrees.

                for (int i = 0; i <= cellsWide; i++)
                {
                    for (int j = 0; j <= cellsTall; j++)
                    {
                        //new specific plus code
                        //TODO: is it faster to shift the Plus code string manually like I do client-side, or to calculate a new plus code from lat/long?
                        OpenLocationCode current = new OpenLocationCode(pw.latitudeS + (resolution * j), pw.longitudeW + (resolution * i));
                        //make a DB entry for this 10cell
                        InterestingPoint IP = new InterestingPoint();
                        IP.PlusCode8 = current.CodeDigits.Substring(0, 8);
                        IP.PlusCode2 = current.CodeDigits.Substring(8, 2);
                        IP.OsmWayId = pw.OsmWayId;
                        IP.areaType = pw.AreaType;

                        IPs.Add(IP);
                    }
                }
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
            if (tags.Any(t => t.k == "natural" && t.v == "water"))
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
            if (tags.Any(t => (t.k == "historical")))
                return "historical";

            //Possibly of interest:
            //landuse:forest / landuse:orchard  / natural:wood
            //natural:sand may be of interest for desert area?
            //natural:spring / natural:hot_spring
            //Anything else seems un-interesting or irrelevant.

            return ""; //not a way we need to save right now.
        }

        public static void CleanDb()
        {
            //Test function to put the DB back to empty.
            GpsExploreContext osm = new GpsExploreContext();
            osm.AreaTypes.RemoveRange(osm.AreaTypes);
            osm.SaveChanges();

            osm.InterestingPoints.RemoveRange(osm.InterestingPoints);
            osm.SaveChanges();

            osm.ProcessedWays.RemoveRange(osm.ProcessedWays);
            osm.SaveChanges();

            osm.MapData.RemoveRange(osm.MapData);
            osm.SaveChanges();

            osm.SinglePointsOfInterests.RemoveRange(osm.SinglePointsOfInterests);
            osm.SaveChanges();

            Console.WriteLine("DB cleaned at " + DateTime.Now);
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

                Console.WriteLine("Starting " + filename + " way read at " + DateTime.Now);
                XmlReaderSettings xrs = new XmlReaderSettings();
                xrs.IgnoreWhitespace = true;
                XmlReader osmFile = XmlReader.Create(filename, xrs);
                osmFile.MoveToContent();

                System.Diagnostics.Stopwatch timer = new System.Diagnostics.Stopwatch();
                timer.Start();

                string destFolder = @"D:\Projects\OSM Server Info\Trimmed JSON Files\";
                bool firstWay = true;
                bool exitEarly = false;
                //first read - save Way info to process later.
                while (osmFile.Read() && !exitEarly)
                {
                    if (timer.ElapsedMilliseconds > 10000)
                    {
                        //Report progress on the console.
                        Console.WriteLine("Processed " +  ways.Count() + " ways so far");
                        timer.Restart();
                    }

                    switch (osmFile.Name)
                    {
                        case "way":
                            if (firstWay) { Console.WriteLine("First Way entry found at " + DateTime.Now); firstWay = false; }

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
                        Console.WriteLine("Processed " + nodes.Count() + " nodes so far");
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
                                    SPOI.Add(new SinglePointsOfInterest() { name = name, lat = n.lat, lon = n.lon, NodeID = n.id, NodeType = nodetype });
                            }
                            break;
                        case "way":
                            exitEarly = true;
                            break;
                    }
                }
                Console.WriteLine("Done reading Node objects at " + DateTime.Now);
                WriteSPOIsToFile(destFolder + System.IO.Path.GetFileNameWithoutExtension(filename) + "-SPOIs.json");
                SPOI = null;
                
                Console.WriteLine("Attempting to convert " + nodes.Count() +  " nodes to lookup at " + DateTime.Now);
                nodeLookup = (Lookup<long, Node>)nodes.ToLookup(k => k.id, v => v);

                foreach (Way w in ways)
                {
                    foreach (long nr in w.nodRefs)
                    {
                        w.nds.Add(nodeLookup[nr].FirstOrDefault());
                    }
                    w.nodRefs = null; //free up a little memory we won't use again.
                }
                Console.WriteLine("Ways populated with Nodes at " + DateTime.Now);
                nodes = null; //done with these now, can free up RAM again.
                
                WriteRawWaysToFile(destFolder + System.IO.Path.GetFileNameWithoutExtension(filename) + "-RawWays.json");

                List<ProcessedWay> pws = new List<ProcessedWay>();
                foreach (Way w in ways)
                {
                    var pw = ProcessWay(w);
                    if (pw != null)
                        pws.Add(pw);
                }
                WriteProcessedWaysToFile(destFolder + System.IO.Path.GetFileNameWithoutExtension(filename) + "-ProcessedWays.json", ref pws);
                pws = null;
                nodeLookup = null;

                osmFile.Close(); osmFile.Dispose();
                Console.WriteLine("Processed " + filename + " at " + DateTime.Now);
                File.Move(filename, filename + "Done"); //We made it all the way to the end, this file is done.
            }
        }

        public static void WriteRawWaysToFile(string filename)
        {
            System.IO.StreamWriter sw = new StreamWriter(filename);
            sw.Write("[");
            foreach (var w in ways)
            {
                var test = JsonSerializer.Serialize(w, typeof(Way));
                sw.Write(test);
                sw.Write(",");
            }
            sw.Write("]");
            sw.Close();
            sw.Dispose();
            Console.WriteLine("All ways were serialized individually and saved to file at " + DateTime.Now);
        }

        public static void WriteProcessedWaysToFile(string filename, ref List<ProcessedWay> pws)
        {
            System.IO.StreamWriter sw = new StreamWriter(filename);
            sw.Write("[");
            foreach (var pw in pws)
            {
                var test = JsonSerializer.Serialize(pw, typeof(ProcessedWay));
                sw.Write(test);
                sw.Write(",");
            }
            sw.Write("]");
            sw.Close();
            sw.Dispose();
            Console.WriteLine("All processed ways were serialized individually and saved to file at " + DateTime.Now);
        }

        public static void WriteSPOIsToFile(string filename)
        {
            System.IO.StreamWriter sw = new StreamWriter(filename);
            sw.Write("[");
            foreach (var s in SPOI)
            {
                var test = JsonSerializer.Serialize(s, typeof(SinglePointsOfInterest));
                sw.Write(test);
                sw.Write(",");
            }
            sw.Write("]");
            sw.Close();
            sw.Dispose();
            Console.WriteLine("All SPOIs were serialized individually and saved to file at " + DateTime.Now);
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
