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

//TODO: this is saving quite a few SPOIs without a name or a type. I should check my logic for saving that.
//TODO: dont include raw ways with null nodes? I can't process those.
//TODO: some node names are displaying in the debug console as "?????? ????". See Siberia. This should all be unicode and that should work fine.

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

        public static string parsedJsonPath = @"D:\Projects\OSM Server Info\Trimmed JSON Files\";

        static void Main(string[] args)
        {

            if (args.Any(a => a == "-cleanDB"))
            {
                CleanDb();
            }

            if (args.Any(a => a == "-resetXml"))
            {
                List<string> filenames = System.IO.Directory.EnumerateFiles(@"D:\Projects\OSM Server Info\XmlToProcess\", "*.osmDone").ToList();
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

            if (args.Any(a => a == "-readSPOIs"))
            {
                AddSPOItoDB(); //takes 13 seconds using bulkInserts. Takes far, far longer without.
            }

            if (args.Any(a => a == "-readRawWays"))
            {
                AddRawWaystoDB();
            }

            if (args.Any(a => a == "-readProcessedWays"))
            {
                AddProcessedWaysToDB();
            }

            //LoadPreviouslyParsedWayData(parsedJsonPath + "LocalCity-RawWays.json");
            //LoadPreviouslyParsedSPOIData(parsedJsonPath + "quebec-latest-SPOIs.json");

            //trying to reduce the amount of resizing done by setting this to a better starting amount.


            //Now loading data into DB.
            //AddSPOItoDB();

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

            Log.WriteLog("Starting Node read at " + DateTime.Now);
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
                    Log.WriteLog("Processed " + nodes.Count() + " nodes and " + ways.Count() + " ways so far");
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
                        if (firstWay) { Log.WriteLog("First Way entry found at " + DateTime.Now); firstWay = false; }

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
            Log.WriteLog("Done reading Way objects at " + DateTime.Now);

            //We got at least partway through processing, save these now.
            File.WriteAllText(System.IO.Path.GetFileNameWithoutExtension(filename) + "-Nodes.json", JsonSerializer.Serialize(nodes));
            File.WriteAllText(System.IO.Path.GetFileNameWithoutExtension(filename) + "-RawWays.json", JsonSerializer.Serialize(ways));
            File.WriteAllText(System.IO.Path.GetFileNameWithoutExtension(filename) + "-SPOI.json", JsonSerializer.Serialize(SPOI));
            Log.WriteLog("Stuff dumped to JSON at " + DateTime.Now);

            //Time to actually create the game objects.
            List<ProcessedWay> pws = new List<ProcessedWay>();
            foreach (Way w in ways)
            {
                var pw = ProcessWay(w);
                if (pw != null)
                    pws.Add(pw);
            }
            Log.WriteLog("Ways processed at " + DateTime.Now);

            //Converting this to Json to minimize disk space. Json's about half the size.
            File.WriteAllText(System.IO.Path.GetFileNameWithoutExtension(filename) + "-ProcessedWays.json", JsonSerializer.Serialize(pws));
            Log.WriteLog("ProcessedWays dumped to JSON at " + DateTime.Now);

            //database work now.
            GpsExploreContext db = new GpsExploreContext();
            db.BulkInsert<ProcessedWay>(pws);
            Log.WriteLog("Processed Ways saved to DB at " + DateTime.Now);

            db.BulkInsert<InterestingPoint>(IPs);
            Log.WriteLog("Interesting Points saved to DB at " + DateTime.Now);

            db.BulkInsert<SinglePointsOfInterest>(SPOI);
            Log.WriteLog("SPOI saved to DB at " + DateTime.Now);

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
                    Log.WriteLog("Error making MapData entry: " + ex.Message);
                }
            }
            //db.BulkInsert<MapData>(mapDatas); //Throws errors on data type.
            db.MapData.AddRange(mapDatas);
            db.SaveChanges(); //bulkInsert might not work on this type
            Log.WriteLog("SQL Geometry values converted and saved to DB at " + DateTime.Now);

        }

        public static void AddSPOItoDB()
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

        public static void AddProcessedWaysToDB()
        {
            GpsExploreContext db = new GpsExploreContext();

            foreach (var file in System.IO.Directory.EnumerateFiles(parsedJsonPath, "*-ProcessedWays.json"))
            {
                List<ProcessedWay> entries = (List<ProcessedWay>)JsonSerializer.Deserialize(File.ReadAllText(file), typeof(List<ProcessedWay>));
                db.BulkInsert<ProcessedWay>(entries);
                Log.WriteLog("Added " + file + " to dB at " + DateTime.Now);
            }
        }

        public static void AddRawWaystoDB()
        {
            //These are MapData items in the DB, unlike the other types that match names in code and DB tables.
            //This function is pretty slow. I should figure out how to speed it up.
            //TODO: don't insert duplicate objects. ~400,000 ways get inserted more than once for some reason. Doesn't seem to impact performance but could be improved.
            var factory = NtsGeometryServices.Instance.CreateGeometryFactory(srid: 4326); //SRID matches Plus code values.
            foreach (var file in System.IO.Directory.EnumerateFiles(parsedJsonPath, "*-RawWays.json"))
            {
                GpsExploreContext db = new GpsExploreContext();
                db.ChangeTracker.AutoDetectChangesEnabled = false; //Allows single inserts to operate at a reasonable speed. Nothing else edits this table.
                List<Way> entries = ReadRawWaysToMemory(file);

                Log.WriteLog("Processing " + entries.Count() + " ways from " + file);
                //List<MapData> mdList = new List<MapData>();
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
                        //TODO consider adding a copy of the first node as the last node if a way isn't closed.
                        MapData md = new MapData();
                        md.name = w.name;
                        md.WayId = w.id;
                        md.type = w.AreaType;
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

                        //TODO: may need to do some kind of MakeValid() call on these to use them directly in SQL Server.
                        md.place = temp;


                        //mdList.Add(md);
                        //Trying to add each entry indiviudally to detect additional errors for now.
                        //But this way is slow, adding ~1 per second. with ChangeTracking on. ~2000 per second with Change Tracking off.
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
                        //at least 1 way has nodes with null coordinates, cant use those ieth
                    }
                }
                //GpsExploreContext db = new GpsExploreContext();
                //db.MapData.AddRange(mdList);
                //db.SaveChanges(); //The bulkInsert library does not like this type, must add the slow way.
                //Takes 4 minutes on Netherlands to get to an error here, adding as a list.
                //at 256,878 entries
                Log.WriteLog("Added " + file + " to dB at " + DateTime.Now);
                Log.WriteLog(errorCount + " ways excluded due to errors (" + ((errorCount / entries.Count()) * 100) + "%)");

                File.Move(file, file + "Done");
            }
        }

        public static void MakeSqlEntries()
        {
            //temp copy of this logic from ParseXMLV3 before removing it
            //database work now.
            GpsExploreContext db = new GpsExploreContext();
            //db.BulkInsert<ProcessedWay>(pws); this isnt a global.
            Log.WriteLog("Processed Ways saved to DB at " + DateTime.Now);

            db.BulkInsert<InterestingPoint>(IPs);
            Log.WriteLog("Interesting Points saved to DB at " + DateTime.Now);

            db.BulkInsert<SinglePointsOfInterest>(SPOI);
            Log.WriteLog("SPOI saved to DB at " + DateTime.Now);



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
                    Log.WriteLog("Error making MapData entry: " + ex.Message);
                    //Errors to consider handling/fixing: if the points aren't a closed shape, make a copy of the first point and make that the last point to close it.
                }
            }
            //db.BulkInsert<MapData>(mapDatas); //Throws errors on data type. It might not support this feature.
            db.MapData.AddRange(mapDatas);
            db.SaveChanges();
            Log.WriteLog("SQL Geometry values converted and saved to DB at " + DateTime.Now);
        }

        //public static void LoadPreviouslyParsedWayData(string filename)
        //{
        //    ways = (List<Way>)JsonSerializer.Deserialize(File.ReadAllText(filename), typeof(List<Way>));
        //}

        //public static void LoadPreviouslyParsedNodeData(string filename)
        //{
        //    nodes = (List<Node>)JsonSerializer.Deserialize(File.ReadAllText(filename), typeof(List<Node>));
        //}

        //public static void LoadPreviouslyParsedSPOIData(string filename)
        //{
        //    SPOI = (List<SinglePointsOfInterest>)JsonSerializer.Deserialize(File.ReadAllText(filename), typeof(List<SinglePointsOfInterest>));
        //}

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
            //Version 2 might just be an attempt at reducing the size of a way.
            //like, check if any of the points are within .000125 degress of another (1 10cell), and if so just use one point
            //or shift all points to the nearest .000125 increment and eliminate duplicates?
            //Since the raw ways data is fast enough to read once its indexed, i dont really need a super small set otherwise.
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
            //TODO: make these bulk deletes? 
            //Test function to put the DB back to empty.
            GpsExploreContext osm = new GpsExploreContext();
            osm.AreaTypes.RemoveRange(osm.AreaTypes);
            osm.SaveChanges();
            Log.WriteLog("AreaTypes cleaned at " + DateTime.Now);

            osm.InterestingPoints.RemoveRange(osm.InterestingPoints);
            Log.WriteLog("InterestingPoints cleaned at " + DateTime.Now);
            osm.SaveChanges();

            osm.ProcessedWays.RemoveRange(osm.ProcessedWays);
            osm.SaveChanges();
            Log.WriteLog("ProcessedWays cleaned at " + DateTime.Now);

            osm.MapData.RemoveRange(osm.MapData);
            osm.SaveChanges();
            Log.WriteLog("MapData cleaned at " + DateTime.Now);

            osm.SinglePointsOfInterests.RemoveRange(osm.SinglePointsOfInterests);
            osm.SaveChanges();
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
                                    SPOI.Add(new SinglePointsOfInterest() { name = name, lat = n.lat, lon = n.lon, NodeID = n.id, NodeType = nodetype });
                            }
                            break;
                        case "way":
                            exitEarly = true;
                            break;
                    }
                }
                Log.WriteLog("Done reading Node objects at " + DateTime.Now);
                WriteSPOIsToFile(destFolder + System.IO.Path.GetFileNameWithoutExtension(filename) + "-SPOIs.json");
                SPOI = null;

                Log.WriteLog("Attempting to convert " + nodes.Count() + " nodes to lookup at " + DateTime.Now);
                nodeLookup = (Lookup<long, Node>)nodes.ToLookup(k => k.id, v => v);

                foreach (Way w in ways)
                {
                    foreach (long nr in w.nodRefs)
                    {
                        w.nds.Add(nodeLookup[nr].FirstOrDefault());
                    }
                    w.nodRefs = null; //free up a little memory we won't use again.
                }
                Log.WriteLog("Ways populated with Nodes at " + DateTime.Now);
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
                Log.WriteLog("Processed " + filename + " at " + DateTime.Now);
                File.Move(filename, filename + "Done"); //We made it all the way to the end, this file is done.
            }
        }

        public static void WriteRawWaysToFile(string filename)
        {
            System.IO.StreamWriter sw = new StreamWriter(filename);
            sw.Write("[" + Environment.NewLine);
            foreach (var w in ways)
            {
                var test = JsonSerializer.Serialize(w, typeof(Way));
                sw.Write(test);
                sw.Write("," + Environment.NewLine);
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
            //string firstLine = sr.ReadLine(); //2 different formats exist, either its all a single line or it's one entry per line plus square brackets on first and last lines.
            //if (firstLine != "[")
            //{
            //    if (firstLine.StartsWith("["))
            //        firstLine = firstLine.Substring(1);
            //    //lw = (List<Way>)JsonSerializer.Deserialize(firstLine, typeof(List<Way>));
            //    lw.Add((Way)JsonSerializer.Deserialize(firstLine, typeof(Way)));
            //}
            //else
            //{
            while (!sr.EndOfStream)
            {
                //It looks like there's a few different formats of JSON output I need to dig through.
                string line = sr.ReadLine();
                if (line == "[")
                {
                    //start of a file that spaced out every entry on a newline correctly. Skip.
                }    
                else if (line.StartsWith("[") && line.EndsWith("]"))
                    lw.AddRange((List<Way>)JsonSerializer.Deserialize(line, typeof(List<Way>), jso)); //whole file is a list on one line. These shouldn't happen anymore.
                else if (line.StartsWith("[") && line.EndsWith(","))
                    lw.Add((Way)JsonSerializer.Deserialize(line.Substring(1, line.Count() -2), typeof(Way), jso)); //first entry on a file before I forced the brackets onto newlines. Comma at end causes errors, is also trimmed.
                else if (line.StartsWith("]"))
                {
                    //dont do anything, this is EOF
                    Log.WriteLog("EOF Reached for " + filename + "at " + DateTime.Now);
                }    
                else
                { 
                    lw.Add((Way)JsonSerializer.Deserialize(line.Substring(0, line.Count() -1), typeof(Way), jso)); //not starting line, trailing comma causes errors
                }    
            }
            //}

            if (lw.Count() == 0)
                Log.WriteLog("No entries for " + filename + "? why?");

            sr.Close(); sr.Dispose();
            return lw;
        }

        public static void WriteProcessedWaysToFile(string filename, ref List<ProcessedWay> pws)
        {
            System.IO.StreamWriter sw = new StreamWriter(filename);
            sw.Write("[" + Environment.NewLine);
            foreach (var pw in pws)
            {
                var test = JsonSerializer.Serialize(pw, typeof(ProcessedWay));
                sw.Write(test);
                sw.Write("," + Environment.NewLine);
            }
            sw.Write("]");
            sw.Close();
            sw.Dispose();
            Log.WriteLog("All processed ways were serialized individually and saved to file at " + DateTime.Now);
        }

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
