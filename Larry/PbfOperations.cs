using CoreComponents;
using CoreComponents.Support;
using NetTopologySuite.Geometries;
using OsmSharp;
using OsmSharp.Complete;
using OsmSharp.Streams;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using static CoreComponents.DbTables;
using static CoreComponents.Place;
using static CoreComponents.Singletons;

namespace Larry
{
    //PbfOperations is for functions that do processing on a PBF file to create some kind of output.
    public static class PbfOperations
    {
        //TODO: clean up this huge mess of a function.
        public static List<MapData> ProcessData(ILookup<long, NodeReference> osmNodes, ref List<OsmSharp.Way> osmWays, ref List<OsmSharp.Relation> osmRelations, ILookup<long, short> referencedWays, bool writeDirectly = false, string directFile = "")
        {
            //This way really needs an option to write data directly to the output file. I wonder how much time is spent resizing processedEntries.
            List<MapData> processedEntries = new List<MapData>();
            List<NodeData> nodes = new List<NodeData>();
            List<WayData> ways = new List<WayData>();

            nodes.Capacity = 100000;
            ways.Capacity = 100000;
            if (!writeDirectly)
                processedEntries.Capacity = 1000000; // osmWays.Count() + osmRelations.Count(); //8 million ways might mean this fails on a 32-bit int.

            System.IO.StreamWriter sw = new StreamWriter(directFile);
            if (writeDirectly)
            {
                sw.Write("[" + Environment.NewLine);
            }

            //Write nodes as mapdata if they're tagged separately from other things.
            Log.WriteLog("Finding tagged nodes at " + DateTime.Now);
            var taggedNodes = osmNodes.AsParallel().Where(n => n.First().name != "" && n.First().type != "" && n.First().type != null).ToList();
            if (!writeDirectly)
                processedEntries.AddRange(taggedNodes.AsParallel().Select(s => Converters.ConvertNodeToMapData(s.First())));
            else
            {
                foreach (var n in taggedNodes) //this can't be parallel because we need to write to a single file.
                {
                    var md = Converters.ConvertNodeToMapData(n.First());

                    if (md != null) //null can be returned from the functions that convert OSM entries to MapData
                    {
                        var recordVersion = new MapDataForJson(md.name, md.place.AsText(), md.type, md.WayId, md.NodeId, md.RelationId, md.AreaTypeId);
                        var test = JsonSerializer.Serialize(recordVersion, typeof(MapDataForJson));
                        sw.Write(test);
                        sw.Write("," + Environment.NewLine);
                    }
                }
            }
            Log.WriteLog("Standalone tagged nodes converted to MapData at " + DateTime.Now);
            taggedNodes = null;

            Log.WriteLog("Converting " + osmWays.Count() + " OsmWays to my Ways at " + DateTime.Now);
            ways.Capacity = osmWays.Count();
            ways = osmWays.AsParallel().Select(w => new WayData()
            {
                id = w.Id.Value,
                name = Place.GetPlaceName(w.Tags),
                AreaType = Place.GetPlaceType(w.Tags),
                nodRefs = w.Nodes.ToList()
            })
            .ToList();
            osmWays = null; //free up RAM we won't use again.
            Log.WriteLog("List created at " + DateTime.Now);

            int wayCounter = 0;
            System.Threading.Tasks.Parallel.ForEach(ways, (w) =>
            {
                wayCounter++;
                if (wayCounter % 10000 == 0)
                    Log.WriteLog(wayCounter + " processed so far");

                LoadNodesIntoWay(ref w, ref osmNodes); //this cannot pass a ref parameter from ProcessData in here because its in a lambda, but we can ref it over.
            });

            Log.WriteLog("Ways populated with Nodes at " + DateTime.Now);
            osmNodes = null; //done with these now, can free up RAM again.

            //Process all the ways that aren't part of a relation first, then remove them.
            if (!writeDirectly)
            {
                processedEntries.AddRange(ways.Where(w => referencedWays[w.id].Count() == 0).AsParallel().Select(w => Converters.ConvertWayToMapData(ref w)));  //When we start hitting the swap file, this takes about 3-4 minutes to start a batch of entries on my dev machine.
                Log.WriteLog("Standalone tagged ways converted to MapData at " + DateTime.Now);
                ways = ways.Where(w => referencedWays[w.id].Count() > 0).ToList();
                processedEntries.AddRange(osmRelations.AsParallel().Select(r => ProcessRelation(r, ref ways))); //Approx. twice as fast as ProcessRelations() without parallel.
                Log.WriteLog("Relations converted to MapData at " + DateTime.Now);

                var outerWays = osmRelations.SelectMany(r => r.Members.Where(m => m.Role == "outer" && m.Type == OsmGeoType.Way).Select(m => m.Id)).ToLookup(k => k, v => v);
                ways = ways.Where(w => outerWays[w.id].Count() == 0).ToList();
                outerWays = null;
                osmRelations = null;

                processedEntries.AddRange(ways.AsParallel().Select(w => Converters.ConvertWayToMapData(ref w)));
                ways = null;
                return processedEntries;
            }
            else
            {
                foreach (var w1 in ways)
                {
                    if (referencedWays[w1.id].Count() != 0)
                        continue; //we're only loading ways that aren't tagged in another relation, so skip ones that are used elsewhere. Check this way to avoid converting ways into another IEnumerable

                    var w2 = w1;
                    var md2 = Converters.ConvertWayToMapData(ref w2);

                    if (md2 != null) //null can be returned from the functions that convert OSM entries to MapData
                    {
                        var recordVersion = new MapDataForJson(md2.name, md2.place.AsText(), md2.type, md2.WayId, md2.NodeId, md2.RelationId, md2.AreaTypeId);
                        var test = JsonSerializer.Serialize(recordVersion, typeof(MapDataForJson));
                        sw.Write(test);
                        sw.Write("," + Environment.NewLine);
                    }
                    md2 = null;
                    //Attempt to reduce memory usage faster so bigger files get processed faster
                    w1.nds = null;
                    w1.name = "";
                }
                Log.WriteLog("Standalone tagged ways converted to MapData at " + DateTime.Now);
                ways = ways.Where(w => referencedWays[w.id].Count() > 0).ToList();

                foreach (var r1 in osmRelations)
                {
                    var md3 = ProcessRelation(r1, ref ways);

                    if (md3 != null) //null can be returned from the functions that convert OSM entries to MapData
                    {
                        var recordVersion = new MapDataForJson(md3.name, md3.place.AsText(), md3.type, md3.WayId, md3.NodeId, md3.RelationId, md3.AreaTypeId);
                        var test = JsonSerializer.Serialize(recordVersion, typeof(MapDataForJson));
                        sw.Write(test);
                        sw.Write("," + Environment.NewLine);
                    }
                }
                Log.WriteLog("Relations converted to MapData at " + DateTime.Now);

                //this is a final check for entries that are an inner way in a relation that is also its own separate entity. (First pass would not have found it because it's referenced, 2nd pass missed because it's inner)
                var outerWays = osmRelations.SelectMany(r => r.Members.Where(m => m.Role == "outer" && m.Type == OsmGeoType.Way).Select(m => m.Id)).ToLookup(k => k, v => v); //v could be a short.
                ways = ways.Where(w => outerWays[w.id].Count() == 0).ToList(); //switch to .Contains
                outerWays = null;
                osmRelations = null;

                if (!writeDirectly)
                    processedEntries.AddRange(ways.AsParallel().Select(w => Converters.ConvertWayToMapData(ref w)));
                else
                {
                    foreach (var w3 in ways)
                    {
                        var w4 = w3;
                        var md4 = Converters.ConvertWayToMapData(ref w4);

                        if (md4 != null) //null can be returned from the functions that convert OSM entries to MapData
                        {
                            var recordVersion = new MapDataForJson(md4.name, md4.place.AsText(), md4.type, md4.WayId, md4.NodeId, md4.RelationId, md4.AreaTypeId);
                            var test = JsonSerializer.Serialize(recordVersion, typeof(MapDataForJson));
                            sw.Write(test);
                            sw.Write("," + Environment.NewLine);
                        }
                    }
                }
                ways = null;

                sw.Write("]");
                sw.Close();
                sw.Dispose();
                return null;
            }
        }

        public static List<MapData> ProcessData(ILookup<long, NodeReference> osmNodes, ref List<OsmSharp.Way> osmWays, ref List<OsmSharp.Relation> osmRelations, HashSet<long> referencedWays)
        {
            List<MapData> processedEntries = new List<MapData>();
            List<NodeData> nodes = new List<NodeData>();
            List<WayData> ways = new List<WayData>();

            nodes.Capacity = 100000;
            ways.Capacity = 100000;
            processedEntries.Capacity = 100000;

            //Write nodes as mapdata if they're tagged separately from other things.
            Log.WriteLog("Finding tagged nodes at " + DateTime.Now);
            var taggedNodes = osmNodes.AsParallel().Where(n => n.First().name != "" && n.First().type != "" && n.First().type != null).ToList();
            processedEntries.AddRange(taggedNodes.AsParallel().Select(s => Converters.ConvertNodeToMapData(s.First())));
            taggedNodes = null;

            Log.WriteLog("Converting " + osmWays.Count() + " OsmWays to my Ways at " + DateTime.Now);
            ways.Capacity = osmWays.Count();
            ways = osmWays.AsParallel().Select(w => new WayData()
            {
                id = w.Id.Value,
                name = Place.GetPlaceName(w.Tags),
                AreaType = Place.GetPlaceType(w.Tags),
                nodRefs = w.Nodes.ToList()
            })
            .ToList();
            osmWays = null; //free up RAM we won't use again.
            Log.WriteLog("List created at " + DateTime.Now);

            int wayCounter = 0;

            System.Threading.Tasks.Parallel.ForEach(ways, (w) =>
            //foreach(var w in ways)
            {
                wayCounter++;
                if (wayCounter % 10000 == 0)
                    Log.WriteLog(wayCounter + " processed so far");

                LoadNodesIntoWay(ref w, ref osmNodes);
            }
            );

            Log.WriteLog("Ways populated with Nodes at " + DateTime.Now);
            osmNodes = null; //done with these now, can free up RAM again.


            //Process all the ways that aren't used by a relation, then remove them.
            processedEntries.AddRange(ways.Where(w => !referencedWays.Contains(w.id)).AsParallel().Select(w => Converters.ConvertWayToMapData(ref w)));
            ways = ways.Where(w => referencedWays.Contains(w.id)).ToList();

            //processedEntries.AddRange(ProcessRelations(ref osmRelations, ref ways)); //75580ms on OH data
            processedEntries.AddRange(osmRelations.AsParallel().Select(r => ProcessRelation(r, ref ways))); //42223ms on OH.

            //Removed entries we've already looked at as part of a relation? I suspect this is one of those cases where
            //either way I do this, something's going to get messed up. Should track what.
            //Removing ways already in a reference screws up: Oak Grove Cemetery at BGSU
            //Re-processing ways already in a reference screws up:
            //ways = ways.Where(w => referencedWays[w.id].Count() == 0).ToList(); 
            //I might want to only remove outer ways, and let inner ways remain in case they're something else.
            var outerWays = osmRelations.SelectMany(r => r.Members.Where(m => m.Role == "outer" && m.Type == OsmGeoType.Way).Select(m => m.Id)).ToHashSet();
            ways = ways.Where(w => !outerWays.Contains(w.id)).ToList();
            outerWays = null;
            osmRelations = null;

            processedEntries.AddRange(ways.AsParallel().Select(w => Converters.ConvertWayToMapData(ref w)));
            ways = null;
            return processedEntries;
        }

        public static void LoadNodesIntoWay(ref WayData w, ref ILookup<long, NodeReference> osmNodes)
        {
            foreach (long nr in w.nodRefs)
            {
                var osmNode = osmNodes[nr].FirstOrDefault();
                //TODO: is osmNode is null, or its properties depending on OrDefault, make w null and return. check later to only process not-null ways. And log it. here too.
                var myNode = new NodeData(osmNode.Id, osmNode.lat, osmNode.lon);
                w.nds.Add(myNode);
            }
            w.nodRefs = null; //free up a little memory we won't use again?
        }

        public static List<MapData> ProcessRelations(ref List<OsmSharp.Relation> osmRelations, ref List<WayData> ways)
        {
            List<MapData> results = new List<MapData>();
            PraxisContext db = new PraxisContext();

            foreach (var r in osmRelations)
                results.Add(ProcessRelation(r, ref ways));

            return results;
        }

        public static MapData ProcessRelation(OsmSharp.Relation r, ref List<WayData> ways)
        {
            PraxisContext db = new PraxisContext();
            string relationName = GetPlaceName(r.Tags);
            Log.WriteLog("Processing Relation " + r.Id + " " + relationName + " to MapData at " + DateTime.Now, Log.VerbosityLevels.High);

            //I might need to check if these are usable ways before checking if they're already handled by the relation
            //Remove entries we won't use.

            var membersToRead = r.Members.Where(m => m.Type == OsmGeoType.Way).Select(m => m.Id).ToList();
            if (membersToRead.Count == 0)
            {
                Log.WriteLog("Relation " + r.Id + " " + relationName + " has no Ways, cannot process.");
                return null;
            }

            //Check members for closed shape
            var shapeList = new List<WayData>();
            foreach (var m in membersToRead)
            {
                var maybeWay = ways.Where(way => way.id == m).FirstOrDefault();
                if (maybeWay != null && maybeWay.nds.Count() >= 2) //2+ is a line, 1 is a point. I have relations with 2- and 3-point lines. THey're just not complete shapes.
                    shapeList.Add(maybeWay);
                else
                {
                    Log.WriteLog("Relation " + r.Id + " " + relationName + " references way " + m + " not found in the file. Attempting to process without it.", Log.VerbosityLevels.High);
                    //NOT TODO: add some way of saving this partial data to the DB to be fixed/enhanced later? This is the LargeAreas process/file. Thats where that should be handled.
                    //break;
                }
            }
            membersToRead = null;

            //Now we have our list of Ways. Check if there's lines that need made into a polygon.
            if (shapeList.Any(s => s.nds.Count == 0))
            {
                Log.WriteLog("Relation " + r.Id + " " + relationName + " has ways with 0 nodes.");
            }
            //convert to lines, polygon, or multipolygon as needed.
            Geometry Tpoly = GeometryHelper.GetGeometryFromWays(shapeList, r);
            if (Tpoly == null)
            {
                //error converting it
                Log.WriteLog("Relation " + r.Id + " " + relationName + " failed to get a polygon from ways. Error.");
                return null;
            }

            if (!Tpoly.IsValid)
            {
                //System.Diagnostics.Debugger.Break();
                Log.WriteLog("Relation " + r.Id + " " + relationName + " Is not valid geometry. Error.");
                return null;
            }

            MapData md = new MapData();
            md.name = Place.GetPlaceName(r.Tags);
            md.type = Place.GetPlaceType(r.Tags);
            md.AreaTypeId = areaTypeReference[md.type.StartsWith("admin") ? "admin" : md.type].First();
            md.RelationId = r.Id.Value;
            md.place = GeometrySupport.SimplifyArea(Tpoly);
            if (md.place == null)
                return null;

            return md;
        }

        public static void ValidateFile(string filename)
        {
            //Ohio.pbf results: 
            //Validate a PBF file
            //List entries that can or cannot be processed

            Log.WriteLog("Checking File " + filename + " at " + DateTime.Now);

            List<OsmSharp.Relation> rs = new List<OsmSharp.Relation>();
            List<OsmSharp.Way> ws = new List<OsmSharp.Way>();
            List<OsmSharp.Node> ns = new List<OsmSharp.Node>();

            rs.Capacity = 1000000;
            ws.Capacity = 1000000;
            ns.Capacity = 1000000;

            using (var fs = File.OpenRead(filename))
            {
                var source = new PBFOsmStreamSource(fs);
                var progress = source.ShowProgress();

                foreach (var entry in progress)
                {
                    if (entry.Type == OsmGeoType.Node)
                        ns.Add((OsmSharp.Node)entry);
                    else if (entry.Type == OsmGeoType.Way)
                        ws.Add((OsmSharp.Way)entry);
                    else if (entry.Type == OsmGeoType.Relation)
                        rs.Add((OsmSharp.Relation)entry);
                }
            }

            Log.WriteLog("Entries pulled into Memory at " + DateTime.Now);

            var rL = rs.ToLookup(k => k.Id, v => v);
            var wL = ws.ToLookup(k => k.Id, v => v);
            var nL = ns.ToLookup(k => k.Id, v => v);
            rs = null;
            ws = null;
            ns = null;

            Log.WriteLog("Lookups create at " + DateTime.Now);

            List<long> badRelations = new List<long>();
            List<long> badWays = new List<long>();

            bool gotoNext = false;
            foreach (var key in rL)
            {
                foreach (var r in key)
                {
                    gotoNext = false;
                    foreach (var m in r.Members)
                    {
                        if (gotoNext)
                            continue;
                        if (m.Type == OsmGeoType.Way && wL[m.Id].Count() > 0)
                        { } //OK
                        else
                        {
                            Log.WriteLog("Relation " + r.Id + "  " + GetPlaceName(r.Tags) + " is missing Way " + m.Id);
                            badRelations.Add(r.Id.Value);
                            gotoNext = true;
                            continue;
                        }
                    }
                }
            }

            Log.WriteLog("Total of " + badRelations.Count() + " unusable relations in a set of " + rL.Count());
        }

        public static void ExtractAreasFromLargeFile(string filename)
        {
            //This should refer to a list of relations that cross multiple extract files, to get a more accurate set of data in game.
            //Starting with North America, will test later on global data
            //Should start with big things
            //Great lakes, major rivers, some huge national parks. Oceans are important for global data.
            //Rough math suggests that this will take 103 minutes to skim planet-latest.osm.pbf per pass.
            //Takes ~17 minutes per pass the 'standard' way on north-america-latest.

            string outputFile = ParserSettings.JsonMapDataFolder + "LargeAreas" + (ParserSettings.UseHighAccuracy ? "-highAcc" : "-lowAcc") + ".json";

            var manualRelationId = new List<long>() {
                //Great Lakes:
                4039900, //Lake Erie
                1205151, //Lake Huron
                1206310, //lake ontario
                1205149, //lake michigan --not valid geometry?
                4039486, //lake superior
                //Admin boundaries:
                148838, //US Admin bounds
                9331155, //48 Contiguous US states
                1428125, //Canada
                //EU?
                //Which other countries do I have divided down into state/provinces?
                //UK
                //Germany
                //France
                //Russia
                //others
                //TODO: add oceans. these might not actually exist as a single entry in OSM. Will have to check why
                //TODO: multi-state or multi-nation rivers.
                2182501, //Ohio River
                1756854, //Mississippi river --failed to get a polygon?
                //other places:
                //yellowstone?
                //grand canyon?
            };

            //Might want to pass an option for MemoryStream on this, since I can store the 7GB continent file in RAM but not the 54GB Planet file.
            var stream = new FileStream(filename, FileMode.Open);
            var source = new PBFOsmStreamSource(stream);
            File.Delete(outputFile); //Clear out any existing entries.

            File.AppendAllLines(outputFile, new List<String>() { "[" });
            var rs = source.Where(s => s.Type == OsmGeoType.Relation && manualRelationId.Contains(s.Id.Value)).Select(s => (OsmSharp.Relation)s).ToList();
            Log.WriteLog("Relation data pass completed at " + DateTime.Now);
            List<WayData> ways = new List<WayData>();
            var referencedWays = rs.SelectMany(r => r.Members.Where(m => m.Type == OsmGeoType.Way).Select(m => m.Id)).Distinct().ToLookup(k => k, v => v);
            var ways2 = source.Where(s => s.Type == OsmGeoType.Way && referencedWays[s.Id.Value].Count() > 0).Select(s => (OsmSharp.Way)s).ToList();
            var referencedNodes = ways2.SelectMany(m => m.Nodes).Distinct().ToLookup(k => k, v => v);
            var nodes2 = source.Where(s => s.Type == OsmGeoType.Node && referencedNodes[s.Id.Value].Count() > 0).Select(n => new NodeReference(n.Id.Value, (float)((OsmSharp.Node)n).Latitude.Value, (float)((OsmSharp.Node)n).Longitude.Value, Place.GetPlaceName(n.Tags), Place.GetPlaceType(n.Tags))).ToList();
            Log.WriteLog("Relevant data pulled from file at" + DateTime.Now);

            var osmNodeLookup = nodes2.AsParallel().ToLookup(k => k.Id, v => v);
            Log.WriteLog("Found " + osmNodeLookup.Count() + " unique nodes");

            ways.Capacity = ways2.Count();
            ways = ways2.AsParallel().Select(w => new WayData()
            {
                id = w.Id.Value,
                name = Place.GetPlaceName(w.Tags),
                AreaType = Place.GetPlaceType(w.Tags),
                nodRefs = w.Nodes.ToList()
            })
            .ToList();
            ways2 = null; //free up RAM we won't use again.
            Log.WriteLog("List created at " + DateTime.Now);

            int wayCounter = 0;
            System.Threading.Tasks.Parallel.ForEach(ways, (w) =>
            {
                wayCounter++;
                if (wayCounter % 10000 == 0)
                    Log.WriteLog(wayCounter + " processed so far");

                LoadNodesIntoWay(ref w, ref osmNodeLookup);
            });

            Log.WriteLog("Ways populated with Nodes at " + DateTime.Now);
            nodes2 = null; //done with these now, can free up RAM again.
            var mapDataEntries = ProcessRelations(ref rs, ref ways);

            //convert to jsonmapdata type
            foreach (var mapDataEntry in mapDataEntries)
            {
                if (mapDataEntry != null)
                {
                    MapDataForJson output = new MapDataForJson(mapDataEntry.name, mapDataEntry.place.AsText(), mapDataEntry.type, mapDataEntry.WayId, mapDataEntry.NodeId, mapDataEntry.RelationId, mapDataEntry.AreaTypeId);
                    File.AppendAllLines(outputFile, new List<String>() { JsonSerializer.Serialize(output, typeof(MapDataForJson)) + "," });
                }
            }
            File.AppendAllLines(outputFile, new List<String>() { "]" });
        }

        //private static void GetAllEntriesFromPbf(Stream dataStream, string areaType, out List<OsmSharp.Relation> relList, out List<OsmSharp.Way> wayList, out Dictionary<long, NodeReference> nodes, out List<MapData> results)
        //{
        //    //Try and pull everything out of the file at once, instead of doing 3 passes on it.
        //    //This does assume, however, that everything is in order (All relations appear before a way they reference, and all ways appear before the nodes they reference.
        //    //This assumption may not be true, which would justify the 3-pass effort. This could cut it down to 2 passes (one for tagged stuff, one for referenced-and-not-tagged stuff)
        //    //Might also do processing here as a way to keep ram low-but-growing over time?
        //    //BAH THEYRE SORTED BACKWARDS.
        //    //Files have nodes first, then ways, then relations.
        //    //BUT
        //    //.ToComplete() gives me entries with all the members filled in, instead of doing the passes myself.
        //    List<OsmSharp.Relation> rs = new List<Relation>();
        //    List<OsmSharp.Way> ws = new List<Way>();
        //    Dictionary<long, NodeReference> ns = new Dictionary<long, NodeReference>();

        //    List<MapData> mds = new List<MapData>();

        //    //use these to track referenced entries internally, instead of externally. Can then also remove items from these.
        //    //THIS might be where I want to use a ConcurrentX collection instead of a baseline one, if i make this foreach parallel.
        //    HashSet<long> rels = new HashSet<long>();
        //    HashSet<long> ways = new HashSet<long>();
        //    HashSet<long> nods = new HashSet<long>();

        //    rs.Capacity = 100000;
        //    ws.Capacity = 100000;

        //    var source = new PBFOsmStreamSource(dataStream);
        //    var source2 = source.Where(s => Place.GetPlaceType(s.Tags) != "" && s.Type == OsmGeoType.Relation).ToComplete();


        //    foreach (var entry in source2)
        //    {
        //        //switch(entry.Type)
        //        //{
        //        //    case OsmGeoType.Relation:
        //        //        if (MapSupport.GetElementType(entry.Tags) != "")
        //        //        {
        //        CompleteRelation temp = (CompleteRelation)entry;
        //        var t = Complete.ProcessCompleteRelation(temp);
        //        //I should make a function that processes this.

        //        //            foreach (var m in temp.Members.Where(m => m.Type == OsmGeoType.Way).Select(m => m.Id))
        //        //                ways.Add(m);
        //        //        }
        //        //        break;
        //        //    case OsmGeoType.Way:
        //        //        if (MapSupport.GetElementType(entry.Tags) != "" || ways.Contains(entry.Id))
        //        //        {
        //        //            Way temp = (Way)entry;
        //        //            ws.Add(temp);
        //        //            foreach (var m in temp.Nodes)
        //        //                nods.Add(m);
        //        //        }
        //        //        break;
        //        //    case OsmGeoType.Node:
        //        //        if (MapSupport.GetElementType(entry.Tags) != "" || nods.Contains(entry.Id))
        //        //        {
        //        //            var n = (OsmSharp.Node)entry;
        //        //            ns.Add(n.Id.Value, new NodeReference(n.Id.Value, (float)n.Latitude, (float)n.Longitude, GetElementName(n.Tags), MapSupport.GetElementType(n.Tags)));
        //        //        }
        //        //        break;
        //        //}
        //    }

        //    relList = rs;
        //    wayList = ws;
        //    nodes = ns;
        //    results = mds;
        //}

        public static List<OsmSharp.Relation> GetRelationsFromPbf(string filename, string areaType, int limit = 0, int skip = 0)
        {
            //Read through a file for stuff that matches our parameters.
            List<OsmSharp.Relation> filteredRelations = new List<OsmSharp.Relation>();
            using (var fs = File.OpenRead(filename))
            {
                filteredRelations = InnerGetRelations(fs, areaType, limit, skip);
            }
            return filteredRelations;
        }

        public static List<OsmSharp.Relation> GetRelationsFromStream(Stream file, string areaType)
        {
            //Read through a file for stuff that matches our parameters.
            List<OsmSharp.Relation> filteredRelations = new List<OsmSharp.Relation>();
            file.Position = 0;
            return InnerGetRelations(file, areaType);
        }

        private static List<OsmSharp.Relation> InnerGetRelations(Stream stream, string areaType, int limit = 4000000, int skip = 0)
        {
            var source = new PBFOsmStreamSource(stream);
            var progress = source; //.ShowProgress();

            List<OsmSharp.Relation> filteredEntries;
            ParallelQuery<Relation> filtering;
            if (areaType == null)
                filteredEntries = progress.AsParallel().Where(p => p.Type == OsmGeoType.Relation &&
                    Place.GetPlaceType(p.Tags) != "")
                .Select(p => (OsmSharp.Relation)p)
            .ToList();
            else if (areaType == "admin")
                filteredEntries = progress.AsParallel().Where(p => p.Type == OsmGeoType.Relation &&
                        Place.GetPlaceType(p.Tags).StartsWith(areaType))
                    .Select(p => (OsmSharp.Relation)p)
                    .ToList();
            else
                filteredEntries = progress.Where(p => p.Type == OsmGeoType.Relation && //Might need to remove the AsParallel part here to get Skip and Take to work as intented.
                Place.GetPlaceType(p.Tags) == areaType)
                //.Skip(skip)
                //.TakeWhile(t => limit-- > 0)
                .Select(p => (OsmSharp.Relation)p)
            .ToList();

            return filteredEntries;
        }

        public static List<OsmSharp.Way> GetWaysFromPbf(string filename, string areaType, ILookup<long, short> referencedWays, bool onlyReferenced = false, int skip = 0, int take = 4000000)
        {
            //Read through a file for stuff that matches our parameters.
            List<OsmSharp.Way> filteredWays = new List<OsmSharp.Way>();
            using (var fs = File.OpenRead(filename))
            {
                filteredWays = InnerGetWays(fs, areaType, referencedWays, onlyReferenced, skip, take);
            }
            return filteredWays;
        }

        //public static List<OsmSharp.Way> GetWaysFromStream(Stream file, string areaType, ILookup<long, short> referencedWays)
        //{
        //    //Read through a memorystream for stuff that matches our parameters.   
        //    file.Position = 0;
        //    return InnerGetWays(file, areaType, referencedWays);
        //}

        public static List<OsmSharp.Way> GetWaysFromStream(Stream file, string areaType, HashSet<long> referencedWays)
        {
            //Read through a memorystream for stuff that matches our parameters.   
            file.Position = 0;
            return InnerGetWays(file, areaType, referencedWays);
        }

        public static List<OsmSharp.Way> InnerGetWays(Stream file, string areaType, ILookup<long, short> referencedWays, bool onlyReferenced = false, int skip = 0, int limit = 4000000)
        {
            List<OsmSharp.Way> filteredWays = new List<OsmSharp.Way>();
            var source = new PBFOsmStreamSource(file);
            var progress = source; //.ShowProgress();

            if (areaType == null)
            {
                filteredWays = progress.AsParallel().Where(p => p.Type == OsmGeoType.Way &&
                (Place.GetPlaceType(p.Tags) != ""
                || referencedWays[p.Id.Value].Count() > 0)
            )
                .Select(p => (OsmSharp.Way)p)
                .ToList();
            }
            else if (areaType == "admin")
            {
                filteredWays = progress.AsParallel().Where(p => p.Type == OsmGeoType.Way &&
                    (Place.GetPlaceType(p.Tags).StartsWith(areaType)
                    || referencedWays[p.Id.Value].Count() > 0)
                )
                    .Select(p => (OsmSharp.Way)p)
                    .ToList();
            }
            else
            {
                filteredWays = progress.AsParallel().Where(p => p.Type == OsmGeoType.Way &&
                    ((Place.GetPlaceType(p.Tags).StartsWith(areaType) && !onlyReferenced)
                    || referencedWays[p.Id.Value].Count() > 0)
                )
                    .Select(p => (OsmSharp.Way)p)
                    .Skip(skip)
                    .TakeWhile(t => limit-- > 0)
                    .ToList();
            }

            return filteredWays;
        }

        private static List<OsmSharp.Way> InnerGetWays(Stream file, string areaType, HashSet<long> referencedWays)
        {
            List<OsmSharp.Way> filteredWays = new List<OsmSharp.Way>();
            var source = new PBFOsmStreamSource(file);
            var progress = source; //.ShowProgress();

            if (areaType == null)
            {
                filteredWays = progress.AsParallel().Where(p => p.Type == OsmGeoType.Way &&
                (Place.GetPlaceType(p.Tags) != ""
                || referencedWays.Contains(p.Id.Value))
            )
                .Select(p => (OsmSharp.Way)p)
                .ToList();
            }
            else if (areaType == "admin")
            {
                filteredWays = progress.AsParallel().Where(p => p.Type == OsmGeoType.Way &&
                    (Place.GetPlaceType(p.Tags).StartsWith(areaType)
                    || referencedWays.Contains(p.Id.Value))
                )
                    .Select(p => (OsmSharp.Way)p)
                    .ToList();
            }
            else
            {
                filteredWays = progress.AsParallel().Where(p => p.Type == OsmGeoType.Way &&
                    (Place.GetPlaceType(p.Tags) == areaType
                    || referencedWays.Contains(p.Id.Value))
                )
                    .Select(p => (OsmSharp.Way)p)
                    .ToList();
            }

            return filteredWays;
        }

        public static ILookup<long, NodeReference> GetNodesFromPbf(string filename, string areaType, ILookup<long, short> nodes, bool onlyReferenced = false)
        {
            ILookup<long, NodeReference> filteredEntries;
            using (var fs = File.OpenRead(filename))
            {
                filteredEntries = InnerGetNodes(fs, areaType, nodes, onlyReferenced);
            }
            return filteredEntries;
        }

        //public static ILookup<long, NodeReference> GetNodesFromStream(Stream file, string areaType, ILookup<long, short> nodes)
        //{
        //    file.Position = 0;
        //    return InnerGetNodes(file, areaType, nodes);
        //}

        public static ILookup<long, NodeReference> GetNodesFromStream(Stream file, string areaType, HashSet<long> nodes)
        {
            file.Position = 0;
            return InnerGetNodes(file, areaType, nodes);
        }

        public static ILookup<long, NodeReference> InnerGetNodes(Stream file, string areaType, ILookup<long, short> nodes, bool onlyReferenced = false)
        {
            var source = new PBFOsmStreamSource(file);
            var progress = source; //.ShowProgress();
            ILookup<long, NodeReference> filteredEntries;

            if (areaType == null)
            {
                filteredEntries = progress.AsParallel().Where(p => p.Type == OsmGeoType.Node &&
               (GetPlaceType(p.Tags) != "" || nodes[p.Id.Value].Count() > 0)
           )
               .Select(n => new NodeReference(n.Id.Value, (float)((OsmSharp.Node)n).Latitude.Value, (float)((OsmSharp.Node)n).Longitude.Value, GetPlaceName(n.Tags), GetPlaceType(n.Tags)))
               .ToLookup(k => k.Id, v => v);
            }
            else if (areaType == "admin")
            {
                filteredEntries = progress.AsParallel().Where(p => p.Type == OsmGeoType.Node &&
               (Place.GetPlaceType(p.Tags).StartsWith(areaType) || nodes[p.Id.Value].Count() > 0)
            )
               .Select(n => new NodeReference(n.Id.Value, (float)((OsmSharp.Node)n).Latitude.Value, (float)((OsmSharp.Node)n).Longitude.Value, GetPlaceName(n.Tags), areaType))
               .ToLookup(k => k.Id, v => v);
            }
            else
            {
                filteredEntries = progress.AsParallel().Where(p => p.Type == OsmGeoType.Node &&
                   ((Place.GetPlaceType(p.Tags) == areaType && !onlyReferenced) || nodes.Contains(p.Id.Value)) //might use less CPU than [].count() TODO test/determine if true.
               )
                   .Select(n => new NodeReference(n.Id.Value, (float)((OsmSharp.Node)n).Latitude.Value, (float)((OsmSharp.Node)n).Longitude.Value, GetPlaceName(n.Tags), areaType))
                   .ToLookup(k => k.Id, v => v);
            }

            return filteredEntries;
        }

        public static ILookup<long, NodeReference> InnerGetNodes(Stream file, string areaType, HashSet<long> nodes)
        {
            var source = new PBFOsmStreamSource(file);
            var progress = source; //.ShowProgress();
            ILookup<long, NodeReference> filteredEntries;

            if (areaType == null)
            {
                filteredEntries = progress.AsParallel().Where(p => p.Type == OsmGeoType.Node &&
               (GetPlaceType(p.Tags) != "" || nodes.Contains(p.Id.Value))
           )
               .Select(n => new NodeReference(n.Id.Value, (float)((OsmSharp.Node)n).Latitude.Value, (float)((OsmSharp.Node)n).Longitude.Value, GetPlaceName(n.Tags), GetPlaceType(n.Tags)))
               .ToLookup(k => k.Id, v => v);
            }
            else if (areaType == "admin")
            {
                filteredEntries = progress.AsParallel().Where(p => p.Type == OsmGeoType.Node &&
               (GetPlaceType(p.Tags).StartsWith(areaType) || nodes.Contains(p.Id.Value))
            )
               .Select(n => new NodeReference(n.Id.Value, (float)((OsmSharp.Node)n).Latitude.Value, (float)((OsmSharp.Node)n).Longitude.Value, GetPlaceName(n.Tags), areaType))
               .ToLookup(k => k.Id, v => v);
            }
            else
            {
                filteredEntries = progress.AsParallel().Where(p => p.Type == OsmGeoType.Node &&
                   (GetPlaceType(p.Tags) == areaType || nodes.Contains(p.Id.Value))
               )
                   .Select(n => new NodeReference(n.Id.Value, (float)((OsmSharp.Node)n).Latitude.Value, (float)((OsmSharp.Node)n).Longitude.Value, GetPlaceName(n.Tags), areaType))
                   .ToLookup(k => k.Id, v => v);
            }

            return filteredEntries;
        }
    }
}
