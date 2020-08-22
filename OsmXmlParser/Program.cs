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

namespace OsmXmlParser
{
    class Program
    {
        //
        public static List<Bounds> bounds = new List<Bounds>();
        public static List<Node> nodes = new List<Node>();
        public static List<Way> ways = new List<Way>();
        public static List<Relation> relations = new List<Relation>();

        public static Lookup<long, Node> nodeLookup;
        public static Lookup<long, Way> wayLookup;

        public static List<string> relevantTags = new List<string>() { "name", "natural", "leisure", "landuse", "amenity", "tourism", "historic"}; //The keys in tags we process to see if we want it included.
        public static List<InterestingPoint> IPs = new List<InterestingPoint>();

        static void Main(string[] args)
        {
            //TODO: parallelize parsing where possible. reading a single XML file isn't parallelizable.
            //CleanDb(); //testing, to start on an empty DB each time.
            //ParseXmlV2(); //temporarily switching to making smaller files.
            MakeAllSerializedFiles();


            //Serialze data, so I can process smaller files in the future?
            //Way should have all the stuff it needs in place after being parsed.
            //XmlSerializer xs = new XmlSerializer(typeof(List<Way>));

            //TODO: decide what elements I want to look up, then save only those results this way.
            //Ways should have all the relevant data if I want to change ProcessedWays to save time in the future.
            //TODO: also loop over a list of xml files?
            //System.IO.StreamWriter sw = new System.IO.StreamWriter("ProcessedWayData.xml");
            //xs.Serialize(sw, ways);
            return;
        }

        public static void ParseXmlV1() 
        { 
            //Hardcoding test logic here.
            //TODO: read xml from zip file. Pretty sure I can stream zipfile data to save HD space, since global XML data needs 1TB of space unzipped.
            string filename = @"..\..\..\jamaica-latest.osm"; //this one takes about 10 seconds to run though, 500MB
            //string filename = @"C:\Users\Drake\Downloads\us-midwest-latest.osm\us-midwest-latest.osm"; //This one takes much longer to process, 28 GB
            //string filename = @"C:\Users\Drake\Downloads\LocalCity.osm"; //stuff I can actually walk to. 4MB
            XmlReaderSettings xrs = new XmlReaderSettings();
            xrs.IgnoreWhitespace = true;
            XmlReader osmFile = XmlReader.Create(filename, xrs);
            osmFile.MoveToContent();

            while (osmFile.Read())
            {
                //This takes about 11 seconds right now to parse Jamaica's full file with XmlReader
                //Its fast enough where I may not bother to remove unused data from the source xml.
                switch (osmFile.Name)
                {
                    case "bounds": //the logical limits of the data in this file.
                        var b = new Bounds();
                        b.minlon = osmFile.GetAttribute("minlon").ToDouble();
                        b.minlat = osmFile.GetAttribute("minlat").ToDouble();
                        b.maxlon = osmFile.GetAttribute("maxlon").ToDouble();
                        b.maxlat = osmFile.GetAttribute("maxlat").ToDouble();
                        bounds.Add(b);
                        break;
                    case "node":
                        //It's a shame we can't dig farther into the XML file to know if this node is needed or not until we've loaded them all.
                        if (osmFile.NodeType == XmlNodeType.Element) //sometimes is EndElement if we had tags we ignored.
                        {
                            var n = new Node();
                            n.id = osmFile.GetAttribute("id").ToLong();
                            n.lat = osmFile.GetAttribute("lat").ToDouble();
                            n.lon = osmFile.GetAttribute("lon").ToDouble();
                            //nodes might have tags with useful info
                            //if (osmFile.NodeType == XmlNodeType.Element)

                            //Actually, I don't care about tags on an individual node right now. Removing this.
                            //There COULD be tagged nodes that matter, and I could make a cell for them, but for now I dont know what those options are
                            //n.tags = parseTags(osmFile.ReadSubtree());
                            //TODO: delete note tags that aren't interesting //EX: created-by, 
                            //TODO: delete nodes that only belonged to boring ways/relations.
                            nodes.Add(n);
                        }
                        break;
                    case "way":
                        //One-shot update this for speed
                        if (nodeLookup == null)
                            nodeLookup = (Lookup<long, Node>)nodes.ToLookup(k => k.id, v => v);
                        //way will contain 'nd' elements with 'ref' to a node by ID.
                        //may contain tag, k is the type of thing it is.
                        var w = new Way();
                        w.id = osmFile.GetAttribute("id").ToLong();

                        ParseWayData(w, osmFile.ReadSubtree());
                        //w.nds = parseWayNodes(osmFile.ReadSubtree());
                        //w.tags = parseTags(osmFile.ReadSubtree());

                        //TODO: delete ways that are just buildings with no additional markers.
                        //if (!w.tags.Any(t => t.k == "building"  //don't add buildings
                        //        || t.k == "runway" //we dont need runways.
                        //        || t.k == "highway" //ignoring roads.
                        //        )) 
                        
                        //Trying an inclusive approach instead:
                        //Other options: landuse for various resources
                        //Option: shop:mall for indoor stuff. (also landuse:retail?)
                        if (w.tags.Any(t => t.k == "leisure" && (t.v == "park") || (t.k == "landuse" && (t.v == "cemetery")) || t.k =="amenity" && (t.v == "grave_yard")))
                            ways.Add(w);
                        break;
                    case "relation":
                        //Upon second thought, I might not actually need relations for what I'm doing. Blocking this out for now.
                        ////again, one shot update
                        //if (wayLookup == null)
                        //    wayLookup = (Lookup<long, Way>)ways.ToLookup(k => k.id, v => v);
                        ////relation has 'member' with a type (usually to a 'way' entry) and a ref to their id
                        ////Can also have tags with various keys (k). Which ones are intersting?
                        ////see https://wiki.openstreetmap.org/wiki/Map_Features for what's what here.
                        ////k = "natural", "tourism", "power", "sport", "leisure", "landuse"[residential, cemetary], "Waterway", type=route, route=foot ?
                        ////Some of these will be complicated because people dont agree on which to use.
                        ////EX: leisure/park is technically a park, but could also be boundary/national_park or sometimes landuse/forest etc.
                        ////My local park is leisure/park
                        ////type=restriction is one we can ignore. I dont care about no U turn signs or whatnot.
                        ////Turning these boxes into PlusCodes is a later task.

                        //Relation r = new Relation();
                        //r.id = osmFile.GetAttribute("id").ToLong();
                        //ParseRelationData(r, osmFile.ReadSubtree());

                        ////on local city data, I only care if the type is interesting, the rest of the tags are irrelevant right now.
                        ////This is an inclusive approach, only include stuff I care about.
                        ////if (r.tags.Any(t => t.k == "type" && t.v != "restriction"))
                        ////{
                        //    relations.Add(r);
                        ////}

                        ////exclusive approach was for discovery.
                        ////if (r.tags.Any(t => t.k == "type" && t.v == "restriction"  //This relation is a restriction on a road, not interesting
                        ////    || t.k == "building")  //this is a building and we're not looking for buildings right now.
                        ////    || r.members.Count == 0 //we didn't add any of the ways in this relation because they weren't useful.
                        ////    ) 

                        ////else //do nothing with this entry.

                        break;
                }
            }

            int counter = 0;

            //Do some checking now
            //var wayTypes = ways.SelectMany(w => w.tags.Select(t => t.k + "|" + t.v)).Distinct().ToList();
            //var relationTypes = relations.SelectMany(w => w.tags.Select(t => t.k + "|" + t.v)).Distinct().ToList();

            //Figure out how to map up these Ways to plus codes.
        }

        public static List<Tag> parseTags(XmlReader xr)
        {
            List<Tag> retVal = new List<Tag>();

            while (xr.Read())
            {
                if (xr.Name == "tag")
                {
                    Tag t = new Tag() { k = xr.GetAttribute("k"), v = xr.GetAttribute("v") };
                    //This is an exclusive approach. I'd prefer an inclusive approach, but I don't yet know what tags an individual node might have I want.
                    if (t.k.StartsWith("name") && t.k != "name" //purge non-English names.
                        || t.k == "created_by" //Don't need to monitor OSM users.
                        || t.k == "highway" //not interested in storing road data.
                        || t.k == "surface" //dont care, this is road data
                        || t.k == "traffic_calming" //dont care, this is road data
                        || t.k == "direction" //dont care, this is road data
                        )
                    { 
                        //Ignore this tag.
                    }
                    else
                        retVal.Add(t);
                }
            }
            return retVal;
        }

        public static void ParseWayData(Way w, XmlReader xr)
        {
            List<Node> retVal = new List<Node>();

            while (xr.Read())
            {
                if (xr.Name == "nd")
                {
                    long nodeID = xr.GetAttribute("ref").ToLong();
                    Node n = nodeLookup[nodeID].First(); // nodes.Where(n => n.id == nodeID).FirstOrDefault();
                    if (n != null && n.id != null)
                        w.nds.Add(n);
                }
                else if (xr.Name == "tag")
                {
                    Tag t = new Tag() { k = xr.GetAttribute("k"), v = xr.GetAttribute("v") };
                    //Also exclusive, want to make this inclusive? Or for Way tags do I read them all now and filter the way out later?
                    if (t.k.StartsWith("name") && t.k != "name" //purge non-English names.
                        || t.k == "created_by" //Don't need to monitor OSM users.
                        )
                    {
                        //Ignore this tag.
                    }
                    else
                        w.tags.Add(t);
                }
            }
            return;
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

                w.AreaType = GetWayType(w.tags);
            }
            return;
        }

        public static void ParseRelationData(Relation r, XmlReader xr)
        {
            //I dont think I'm actually parsing this now, since these are rarely things I'm interested in.
            List<Node> retVal = new List<Node>();
            
            while (xr.Read())
            {
                if (xr.Name == "member")
                {
                    var memberType = xr.GetAttribute("type");
                    if (memberType == "way") //get ways, ignore nodes for now.
                    {
                        long wayID = xr.GetAttribute("ref").ToLong();
                        if (wayLookup[wayID].Count() > 0) //we might not have added this way if it was't interesting.
                        {
                            Way w = wayLookup[wayID].First();
                            if (w != null && w.id != null)
                                r.members.Add(w);
                        }
                    }
                }
                else if (xr.Name == "tag")
                {
                    Tag t = new Tag() { k = xr.GetAttribute("k"), v = xr.GetAttribute("v") };
                    if (t.k.StartsWith("name") && t.k != "name" //purge non-English names.
                        || t.k == "created_by" //Don't need to monitor OSM users.
                        )
                    {
                        //Ignore this tag.
                    }
                    else
                        r.tags.Add(t);
                }
            }
            return;
        }


        public static void ParseXmlV2()
        {
            //For faster processing (or minimizing RAM use), we do 2 reads of the file.
            //Run 1: Skip to Ways, save tags and a list of ref values. Filter on the existing rules for including a Way.
            //Run 2: make a list of nodes we care about from our selected Way objects, then parse those into a list
            //Run 2a: once we have all the nodes we care about, update the Way objects to fill in the List<Node> instead of the List<long> of refs.

            //Results:
            //This takes 25 seconds to process Jamaica's 500MB data, including DB writes.
            //My local city takes a second with DB writes.
            //US Midwest takes 5:45 to read and write.

            Console.WriteLine("Starting way read at " + DateTime.Now);
            //TODO: read xml from zip file. Pretty sure I can stream zipfile data to save HD space, since global XML data needs 1TB of space unzipped.
            //string filename = @"..\..\..\jamaica-latest.osm"; //500MB, a reasonable test scenario for speed/load purposes.
            string filename = @"C:\Users\Drake\Downloads\us-midwest-latest.osm\us-midwest-latest.osm"; //This one takes much longer to process, 28 GB
            //string filename = @"C:\Users\Drake\Downloads\LocalCity.osm"; //stuff I can actually walk to. 4MB
            XmlReaderSettings xrs = new XmlReaderSettings();
            xrs.IgnoreWhitespace = true;
            XmlReader osmFile = XmlReader.Create(filename, xrs);
            osmFile.MoveToContent();

            bool firstWay = true;
            bool exitEarly = false;
            while (osmFile.Read() && !exitEarly)
            {
                //First read-through, only load Way entries

                //This takes about 11 seconds right now to parse Jamaica's full file with XmlReader
                //Its fast enough where I may not bother to remove unused data from the source xml.
                switch (osmFile.Name)
                {
                    case "way":
                        if (firstWay) { Console.WriteLine("First Way entry found at " + DateTime.Now); firstWay = false; }

                        //way will contain 'nd' elements with 'ref' to a node by ID.
                        //may contain tag, k is the type of thing it is.
                        var w = new Way();
                        w.id = osmFile.GetAttribute("id").ToLong();

                        ParseWayDataV2(w, osmFile.ReadSubtree()); //Saves a list of nodeRefs, doesnt look for actual nodes.
                        
                        //Trying an inclusive approach instead:
                        //Other options: landuse for various resources
                        //Option: landuse:retail?
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

            //We now have all Way info. Reset the XML Reader
            osmFile.Close(); osmFile.Dispose();
            osmFile = XmlReader.Create(filename, xrs);
            osmFile.MoveToContent();
            exitEarly = false;

            var nodesToGet = ways.SelectMany(w => w.nodRefs).ToLookup(k => k, v => v);
            //var nodesByWay = ways. //Need a quicker way to find Ways that need a node?
            
            Console.WriteLine("Nodes to process parsed at " + DateTime.Now);
            while (osmFile.Read() && !exitEarly)
            {
                //Now process Nodes we need to.
                switch (osmFile.Name) 
                {
                    case "node":
                        //It's a shame we can't dig farther into the XML file to know if this node is needed or not until we've loaded them all.
                        if (osmFile.NodeType == XmlNodeType.Element) //sometimes is EndElement if we had tags we ignored.
                        {
                            var n = new Node();
                            n.id = osmFile.GetAttribute("id").ToLong();

                            //see if we need this node. If not, don't save it.
                            //we need this node IF it's part of a way we saved OR (TODO) has a tag of interest to point out. These tags are TBD.
                            if (nodesToGet[n.id].Count() == 0) //TODO: || relevantTags.Contains(n.tags)
                                break;
                            
                            n.lat = osmFile.GetAttribute("lat").ToDouble();
                            n.lon = osmFile.GetAttribute("lon").ToDouble();
                            //nodes might have tags with useful info
                            //if (osmFile.NodeType == XmlNodeType.Element)

                            //Actually, I don't care about tags on an individual node right now. Removing this.
                            //There COULD be tagged nodes that matter, and I could make a cell for them, but for now I dont know what those options are
                            //n.tags = parseTags(osmFile.ReadSubtree());
                            //TODO: delete note tags that aren't interesting //EX: created-by, 
                            //TODO: delete nodes that only belonged to boring ways/relations.
                            nodes.Add(n);
                        }
                        break;
                    case "way":
                        exitEarly = true;
                        break;
                }
            }
            Console.WriteLine("Nodes processed at " + DateTime.Now);

            //might need to loop over Ways now, add in Nodes as needed.
            nodeLookup = (Lookup<long, Node>)nodes.ToLookup(k => k.id, v => v);

            foreach (Way w in ways)
            {
                foreach (long nr in w.nodRefs)
                {
                    w.nds.Add(nodeLookup[nr].First());
                }
                w.nodRefs = null; //free up a little memory we won't use again.
            }

            Console.WriteLine("Ways populated with Nodes at " + DateTime.Now);

            XmlSerializer xs = new XmlSerializer(typeof(List<Way>));
            System.IO.StreamWriter sw = new System.IO.StreamWriter(System.IO.Path.GetFileNameWithoutExtension(filename) + "-RawWays.xml");
            xs.Serialize(sw, ways);
            sw.Close(); sw.Dispose();

            //Take a second, create area types
            var areaTypes = ways.Select(w => w.AreaType).Distinct().Select(w => new AreaType() { });


            //Time to actually create the game objects.
            List<ProcessedWay> pws = new List<ProcessedWay>();
            foreach (Way w in ways)
            {
                var pw = ProcessWay(w);
                if (pw != null)
                    pws.Add(pw);
            }
            Console.WriteLine("Ways processed at " + DateTime.Now);
            xs = new XmlSerializer(typeof(List<ProcessedWay>));
            sw = new System.IO.StreamWriter(System.IO.Path.GetFileNameWithoutExtension(filename) + "-ProcessedWays.xml");
            xs.Serialize(sw, ways);
            sw.Close(); sw.Dispose();

            //Make InterestingPoint entries out of Processed Ways
            MakePoints(pws);
            Console.WriteLine("Interesting Points genereated at " + DateTime.Now);

            //database work now.
            GpsExploreContext db = new GpsExploreContext();
            db.BulkInsert<ProcessedWay>(pws);
            //db.SaveChanges();
            Console.WriteLine("Processed Ways saved to DB at " + DateTime.Now);

            //db.InterestingPoints.AddRange(IPs);
            db.BulkInsert<InterestingPoint>(IPs);
            db.SaveChanges();
            Console.WriteLine("Interesting Points saved to DB at " + DateTime.Now);
        }

        public static void LoadPreviouslyParsedData(string filename)
        {
            //Should match up to ParseXMLV2, but instead of the OSM XML file, 
            //use a serialized value. Just un-serialize them and continue on.

            XmlSerializer xs = new XmlSerializer(typeof(List<Way>));
            System.IO.StreamReader sw = new System.IO.StreamReader(filename);
            ways = (List<Way>)xs.Deserialize(sw);
        }

        public static ProcessedWay ProcessWay(Way w)
        {
            if (w == null)
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

            pw.latitudeS = w.nds.Min(w => w.lat); //smaller numbers are south
            pw.longitudeW = w.nds.Min(w => w.lon); //smaller numbers are west.
            pw.distanceN = w.nds.Max(w => w.lat) - pw.latitudeS; //should be a positive number now.
            pw.distanceE = w.nds.Max(w => w.lon) - pw.longitudeW; //same          

            return pw; //This can be used to make InterestingPoints for the actual app to read.

            //Theory for Version 2:
            //I could probably use the actual geolocation stuff in SQL Server 
            //and see if any Way (before this approximate processing) .Contains() the center of a Plus code.
            //Might need to start at the 8cell level, and if anything hits there, THEN check for 10cells contained by it?
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
                int cellsTall  = (int)(pw.distanceN / resolution); //Plus code resolution in degrees.

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

        public static string GetWayType(List<Tag> tags)
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
            if (tags.Any(t => (t.k == "tourism")))
                return "tourism";

            //I have historical as a tag to save, but not necessarily sub-sets yet of whats interesting there.
            if (tags.Any(t => (t.k == "historical")))
                return "historical";

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

            Console.WriteLine("DB cleaned at " + DateTime.Now);
        }

        public static void MakeAllSerializedFiles()
        {
            List<string> filenames = System.IO.Directory.EnumerateFiles(@"D:\Projects\OSM Server Info\XmlToProcess\", "*.osm").ToList();

            //This loop takes about 10 minutes to handle one of the USA sub-region files, reading from my USB3 external HD. (Internal M2 SSD would probably be almost twice as fast)
            foreach (string filename in filenames)
            {
                ways = null;
                nodes = null;
                ways = new List<Way>();
                nodes = new List<Node>();

                Console.WriteLine("Starting " + filename + " way read at " + DateTime.Now);
                XmlReaderSettings xrs = new XmlReaderSettings();
                xrs.IgnoreWhitespace = true;
                XmlReader osmFile = XmlReader.Create(filename, xrs);
                osmFile.MoveToContent();

                bool firstWay = true;
                bool exitEarly = false;
                while (osmFile.Read() && !exitEarly)
                {
                    switch (osmFile.Name)
                    {
                        case "way":
                            if (firstWay) { Console.WriteLine("First Way entry found at " + DateTime.Now); firstWay = false; }
                            var w = new Way();
                            w.id = osmFile.GetAttribute("id").ToLong();
                            ParseWayDataV2(w, osmFile.ReadSubtree()); //Saves a list of nodeRefs, doesnt look for actual nodes.

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

                //We now have all Way info. Reset the XML Reader
                osmFile.Close(); osmFile.Dispose();
                osmFile = XmlReader.Create(filename, xrs);
                osmFile.MoveToContent();
                exitEarly = false;

                var nodesToGet = ways.SelectMany(w => w.nodRefs).ToLookup(k => k, v => v);
                Console.WriteLine("Nodes to process parsed at " + DateTime.Now);
                while (osmFile.Read() && !exitEarly)
                {
                    //Now process Nodes we need to.
                    switch (osmFile.Name)
                    {
                        case "node":
                            if (osmFile.NodeType == XmlNodeType.Element) //sometimes is EndElement if we had tags we ignored.
                            {
                                var n = new Node();
                                n.id = osmFile.GetAttribute("id").ToLong();

                                //see if we need this node. If not, don't save it.
                                //we need this node IF it's part of a way we saved OR (TODO) has a tag of interest to point out. These tags are TBD.
                                if (nodesToGet[n.id].Count() == 0) //TODO: || relevantTags.Contains(n.tags)
                                    break;

                                n.lat = osmFile.GetAttribute("lat").ToDouble();
                                n.lon = osmFile.GetAttribute("lon").ToDouble();
                                nodes.Add(n);
                            }
                            break;
                        case "way":
                            exitEarly = true;
                            break;
                    }
                }
                Console.WriteLine("Nodes processed at " + DateTime.Now);

                //might need to loop over Ways now, add in Nodes as needed.
                nodeLookup = (Lookup<long, Node>)nodes.ToLookup(k => k.id, v => v); //this is where the inner engine errors occur?
                //Hungray - 7813636589 node isn't in my list?
                //Great Britain - 7813623051 isnt in the list
                //Germany - 7813639268 isnt in my list? 8,356,155 nodes in the lookup
                foreach (Way w in ways)
                {
                    foreach (long nr in w.nodRefs)
                    {
                        w.nds.Add(nodeLookup[nr].FirstOrDefault()); //means a null nd will show up eventually if this fixes the single-missing-node issue
                    }
                    w.nodRefs = null; //free up a little memory we won't use again.
                }

                Console.WriteLine("Ways populated with Nodes at " + DateTime.Now);

                XmlSerializer xs = new XmlSerializer(typeof(List<Way>));
                System.IO.StreamWriter sw = new System.IO.StreamWriter(System.IO.Path.GetFileNameWithoutExtension(filename) + "-RawWays.xml");
                xs.Serialize(sw, ways);
                sw.Close(); sw.Dispose();

                List<ProcessedWay> pws = new List<ProcessedWay>();
                foreach (Way w in ways)
                {
                    pws.Add(ProcessWay(w));
                }
                Console.WriteLine("Ways processed at " + DateTime.Now);

                xs = new XmlSerializer(typeof(List<ProcessedWay>));
                sw = new System.IO.StreamWriter(System.IO.Path.GetFileNameWithoutExtension(filename) + "-ProcessedWays.xml");
                xs.Serialize(sw, pws);
                sw.Close(); sw.Dispose();

                osmFile.Close(); osmFile.Dispose();
                Console.WriteLine("Processed " + filename + " at " + DateTime.Now);
            }
        }
        

        //This works with XmlDocument, but XmlReader is so much faster.
        //public static List<Tag> parseTags(XmlNode xmlNode)
        //{
        //    List<Tag> retVal = new List<Tag>();
        //    List<XmlNode> stuffToRemove = new List<XmlNode>();
        //    foreach (XmlNode t in xmlNode.ChildNodes)
        //    {
        //        if (t.Name == "tag")
        //        {
        //            //TODO: delete boring/space consuming tags from the node here
        //            if (t.Attributes["k"].Value.StartsWith("name") && t.Attributes["k"].Value != "name" //purge non-English names.
        //                || t.Attributes["k"].Value == "created_by"
        //                )
        //                stuffToRemove.Add(t);
        //            else
        //            {
        //                retVal.Add(new Tag() { k = t.Attributes["k"].Value, v = t.Attributes["v"].Value });
        //            }
        //        }
        //    }
        //    foreach (XmlNode x in stuffToRemove)
        //        xmlNode.RemoveChild(x);

        //    return retVal;
        //}


        /* For reference: the tags Pokemon Go appears to be using. I don't need all of these.
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
