using CoreComponents;
using CoreComponents.Support;
using NetTopologySuite.Geometries;
using OsmSharp;
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
        public static void ProcessData(ILookup<long, NodeReference> osmNodes, ref List<OsmSharp.Way> osmWays, ref List<OsmSharp.Relation> osmRelations, ILookup<long, short> referencedWays, string directFile = "")
        {
            List<MapData> processedEntries = new List<MapData>();
            List<NodeData> nodes = new List<NodeData>();
            List<WayData> ways = new List<WayData>();

            nodes.Capacity = 100000;
            ways.Capacity = 100000;

            System.IO.StreamWriter sw = new StreamWriter(directFile);
            sw.Write("[" + Environment.NewLine);

            //Write nodes as mapdata if they're tagged separately from other things.
            Log.WriteLog("Finding tagged nodes at " + DateTime.Now);
            var taggedNodes = osmNodes.AsParallel().Where(n => n.First().name != "" && n.First().type != "" && n.First().type != null).ToList();

            foreach (var n in taggedNodes) //this can't be parallel because we need to write to a single file.
            {
                var md = Converters.ConvertNodeToMapData(n.First());

                if (md != null) //null can be returned from the functions that convert OSM entries to MapData
                {
                    var recordVersion = new MapDataForJson(md.name, md.place.AsText(), md.type, md.WayId, md.NodeId, md.RelationId, md.AreaTypeId, md.AreaSize);
                    var test = JsonSerializer.Serialize(recordVersion, typeof(MapDataForJson));
                    sw.Write(test);
                    sw.Write("," + Environment.NewLine);
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


            foreach (var w1 in ways)
            {
                if (referencedWays[w1.id].Count() != 0)
                    continue; //we're only loading ways that aren't tagged in another relation, so skip ones that are used elsewhere. Check this way to avoid converting ways into another IEnumerable

                var w2 = w1;
                var md2 = Converters.ConvertWayToMapData(ref w2);

                if (md2 != null) //null can be returned from the functions that convert OSM entries to MapData
                {
                    var recordVersion = new MapDataForJson(md2.name, md2.place.AsText(), md2.type, md2.WayId, md2.NodeId, md2.RelationId, md2.AreaTypeId, md2.AreaSize);
                    var test = JsonSerializer.Serialize(recordVersion, typeof(MapDataForJson));
                    sw.Write(test);
                    sw.Write("," + Environment.NewLine);
                }
                md2 = null;
                //Attempt to reduce memory usage sooner
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
                    var recordVersion = new MapDataForJson(md3.name, md3.place.AsText(), md3.type, md3.WayId, md3.NodeId, md3.RelationId, md3.AreaTypeId, md3.AreaSize);
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


            foreach (var w3 in ways)
            {
                var w4 = w3;
                var md4 = Converters.ConvertWayToMapData(ref w4);

                if (md4 != null) //null can be returned from the functions that convert OSM entries to MapData
                {
                    var recordVersion = new MapDataForJson(md4.name, md4.place.AsText(), md4.type, md4.WayId, md4.NodeId, md4.RelationId, md4.AreaTypeId, md4.AreaSize);
                    var test = JsonSerializer.Serialize(recordVersion, typeof(MapDataForJson));
                    sw.Write(test);
                    sw.Write("," + Environment.NewLine);
                }
            }

            ways = null;

            sw.Write("]");
            sw.Close();
            sw.Dispose();
            return;
        }

        public static void LoadNodesIntoWay(ref WayData w, ref ILookup<long, NodeReference> osmNodes)
        {
            foreach (long nr in w.nodRefs)
            {
                var osmNode = osmNodes[nr].FirstOrDefault();
                if (osmNode != null)
                {
                    var myNode = new NodeData(osmNode.Id, osmNode.lat, osmNode.lon);
                    w.nds.Add(myNode);
                }
            }
            w.nodRefs = null; //free up a little memory we won't use again
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

            //Sanity check 1
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
                //Oceans don't exist in OSM because their specific boundaries are poorly defined.
                //TODO: multi-state or multi-nation rivers.
                2182501, //Ohio River
                1756854, //Mississippi river --failed to get a polygon?
                //other places:
                //yellowstone?
                //grand canyon?
            };

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
                    MapDataForJson output = new MapDataForJson(mapDataEntry.name, mapDataEntry.place.AsText(), mapDataEntry.type, mapDataEntry.WayId, mapDataEntry.NodeId, mapDataEntry.RelationId, mapDataEntry.AreaTypeId, mapDataEntry.AreaSize);
                    File.AppendAllLines(outputFile, new List<String>() { JsonSerializer.Serialize(output, typeof(MapDataForJson)) + "," });
                }
            }
            File.AppendAllLines(outputFile, new List<String>() { "]" });
        }

        public static List<OsmSharp.Relation> GetRelationsFromPbf(string filename, string areaType, int skip = 0, int limit = 0)
        {
            List<OsmSharp.Relation> filteredRelations = new List<OsmSharp.Relation>();
            using (var fs = File.OpenRead(filename))
            {
                filteredRelations = InnerGetRelations(fs, areaType, skip, limit);
            }
            return filteredRelations;
        }

        public static List<OsmSharp.Relation> GetRelationsFromStream(Stream file, string areaType)
        {
            List<OsmSharp.Relation> filteredRelations = new List<OsmSharp.Relation>();
            file.Position = 0;
            return InnerGetRelations(file, areaType);
        }

        private static List<OsmSharp.Relation> InnerGetRelations(Stream stream, string areaType, int skip = 0, int limit = 4000000)
        {
            var source = new PBFOsmStreamSource(stream);

            List<OsmSharp.Relation> filteredEntries;
            if (areaType == null)
                filteredEntries = source.AsParallel().Where(p => p.Type == OsmGeoType.Relation &&
                    Place.GetPlaceType(p.Tags) != "")
                .Select(p => (OsmSharp.Relation)p)
            .ToList();
            else if (areaType == "admin")
                filteredEntries = source.AsParallel().Where(p => p.Type == OsmGeoType.Relation &&
                        Place.GetPlaceType(p.Tags).StartsWith(areaType))
                    .Select(p => (OsmSharp.Relation)p)
                    .ToList();
            else
                filteredEntries = source.Where(p => p.Type == OsmGeoType.Relation &&
                Place.GetPlaceType(p.Tags) == areaType)
                .Skip(skip) //I never hit anything close to my intended limit sorting through my extract files.
                .TakeWhile(t => limit-- > 0) //But these may matter if someone wants to try and parse through a single global pbf file.
                .Select(p => (OsmSharp.Relation)p)
            .ToList();

            return filteredEntries;
        }

        public static List<OsmSharp.Way> GetWaysFromPbf(string filename, string areaType, ILookup<long, short> referencedWays, bool onlyReferenced = false, int skip = 0, int limit = 4000000)
        {
            List<OsmSharp.Way> filteredWays = new List<OsmSharp.Way>();
            using (var fs = File.OpenRead(filename))
            {
                filteredWays = InnerGetWays(fs, areaType, referencedWays, onlyReferenced, skip, limit);
            }
            return filteredWays;
        }

        public static List<OsmSharp.Way> GetWaysFromStream(Stream file, string areaType, ILookup<long, short> referencedWays)
        {
            file.Position = 0;
            return InnerGetWays(file, areaType, referencedWays);
        }

        public static List<OsmSharp.Way> InnerGetWays(Stream file, string areaType, ILookup<long, short> referencedWays, bool onlyReferenced = false, int skip = 0, int limit = 4000000)
        {
            List<OsmSharp.Way> filteredWays = new List<OsmSharp.Way>();
            var source = new PBFOsmStreamSource(file);

            if (areaType == null)
            {
                filteredWays = source.AsParallel().Where(p => p.Type == OsmGeoType.Way &&
                (Place.GetPlaceType(p.Tags) != ""
                || referencedWays[p.Id.Value].Count() > 0)
            )
                .Select(p => (OsmSharp.Way)p)
                .ToList();
            }
            else if (areaType == "admin")
            {
                filteredWays = source.AsParallel().Where(p => p.Type == OsmGeoType.Way &&
                    (Place.GetPlaceType(p.Tags).StartsWith(areaType)
                    || referencedWays[p.Id.Value].Count() > 0)
                )
                    .Select(p => (OsmSharp.Way)p)
                    .ToList();
            }
            else
            {
                filteredWays = source.AsParallel().Where(p => p.Type == OsmGeoType.Way &&
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

            if (areaType == null)
            {
                filteredWays = source.AsParallel().Where(p => p.Type == OsmGeoType.Way &&
                (Place.GetPlaceType(p.Tags) != ""
                || referencedWays.Contains(p.Id.Value))
            )
                .Select(p => (OsmSharp.Way)p)
                .ToList();
            }
            else if (areaType == "admin")
            {
                filteredWays = source.AsParallel().Where(p => p.Type == OsmGeoType.Way &&
                    (Place.GetPlaceType(p.Tags).StartsWith(areaType)
                    || referencedWays.Contains(p.Id.Value))
                )
                    .Select(p => (OsmSharp.Way)p)
                    .ToList();
            }
            else
            {
                filteredWays = source.AsParallel().Where(p => p.Type == OsmGeoType.Way &&
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

        public static ILookup<long, NodeReference> GetNodesFromStream(Stream file, string areaType, HashSet<long> nodes)
        {
            file.Position = 0;
            return InnerGetNodes(file, areaType, nodes);
        }

        public static ILookup<long, NodeReference> InnerGetNodes(Stream file, string areaType, ILookup<long, short> nodes, bool onlyReferenced = false)
        {
            var source = new PBFOsmStreamSource(file);
            ILookup<long, NodeReference> filteredEntries;

            if (areaType == null)
            {
                filteredEntries = source.AsParallel().Where(p => p.Type == OsmGeoType.Node &&
               (GetPlaceType(p.Tags) != "" || nodes[p.Id.Value].Count() > 0)
           )
               .Select(n => new NodeReference(n.Id.Value, (float)((OsmSharp.Node)n).Latitude.Value, (float)((OsmSharp.Node)n).Longitude.Value, GetPlaceName(n.Tags), GetPlaceType(n.Tags)))
               .ToLookup(k => k.Id, v => v);
            }
            else if (areaType == "admin")
            {
                filteredEntries = source.AsParallel().Where(p => p.Type == OsmGeoType.Node &&
               (Place.GetPlaceType(p.Tags).StartsWith(areaType) || nodes[p.Id.Value].Count() > 0)
            )
               .Select(n => new NodeReference(n.Id.Value, (float)((OsmSharp.Node)n).Latitude.Value, (float)((OsmSharp.Node)n).Longitude.Value, GetPlaceName(n.Tags), areaType))
               .ToLookup(k => k.Id, v => v);
            }
            else
            {
                filteredEntries = source.AsParallel().Where(p => p.Type == OsmGeoType.Node &&
                   ((Place.GetPlaceType(p.Tags) == areaType && !onlyReferenced) || nodes.Contains(p.Id.Value))
               )
                   .Select(n => new NodeReference(n.Id.Value, (float)((OsmSharp.Node)n).Latitude.Value, (float)((OsmSharp.Node)n).Longitude.Value, GetPlaceName(n.Tags), areaType))
                   .ToLookup(k => k.Id, v => v);
            }

            return filteredEntries;
        }

        public static ILookup<long, NodeReference> InnerGetNodes(Stream file, string areaType, HashSet<long> nodes)
        {
            var source = new PBFOsmStreamSource(file);
            ILookup<long, NodeReference> filteredEntries;

            if (areaType == null)
            {
                filteredEntries = source.AsParallel().Where(p => p.Type == OsmGeoType.Node &&
               (GetPlaceType(p.Tags) != "" || nodes.Contains(p.Id.Value))
           )
               .Select(n => new NodeReference(n.Id.Value, (float)((OsmSharp.Node)n).Latitude.Value, (float)((OsmSharp.Node)n).Longitude.Value, GetPlaceName(n.Tags), GetPlaceType(n.Tags)))
               .ToLookup(k => k.Id, v => v);
            }
            else if (areaType == "admin")
            {
                filteredEntries = source.AsParallel().Where(p => p.Type == OsmGeoType.Node &&
               (GetPlaceType(p.Tags).StartsWith(areaType) || nodes.Contains(p.Id.Value))
            )
               .Select(n => new NodeReference(n.Id.Value, (float)((OsmSharp.Node)n).Latitude.Value, (float)((OsmSharp.Node)n).Longitude.Value, GetPlaceName(n.Tags), areaType))
               .ToLookup(k => k.Id, v => v);
            }
            else
            {
                filteredEntries = source.AsParallel().Where(p => p.Type == OsmGeoType.Node &&
                   (GetPlaceType(p.Tags) == areaType || nodes.Contains(p.Id.Value))
               )
                   .Select(n => new NodeReference(n.Id.Value, (float)((OsmSharp.Node)n).Latitude.Value, (float)((OsmSharp.Node)n).Longitude.Value, GetPlaceName(n.Tags), areaType))
                   .ToLookup(k => k.Id, v => v);
            }

            return filteredEntries;
        }

        public static void SerializeFilesFromPBF(string filename)
        {
            System.IO.FileInfo fi = new FileInfo(filename);
            if (ParserSettings.ForceSeparateFiles || fi.Length > ParserSettings.FilesizeSplit) //This is mostly pre-emptive, size is not a particularly great predictor of the need to split files in practice.
            {
                //Parse this file into area type sub-files from disk, so that I can do more work in less RAM.
                SerializeSeparateFilesFromPBF(filename);
                return;
            }

            Log.WriteLog("Checking for members in  " + filename + " at " + DateTime.Now);
            string destFilename = System.IO.Path.GetFileName(filename).Replace(".osm.pbf", "");
            ProcessRelationData(null, filename, ParserSettings.JsonMapDataFolder + destFilename + "-MapData" + (ParserSettings.UseHighAccuracy ? "-highAcc" : "-lowAcc") + ".json");

            Log.WriteLog("Processed " + filename + " at " + DateTime.Now);
            File.Move(filename, filename + "Done");
        }

        public static void SerializeSeparateFilesFromPBF(string filename)
        {
            foreach (var areatype in areaTypes.Where(a => a.AreaTypeId < 100)) //each pass takes roughly the same amount of time to read, but uses less ram than holding the stream in memory or all content at once.
            {
                try
                {

                    string areatypename = areatype.AreaName;
                    Log.WriteLog("Checking for " + areatypename + " members in  " + filename + " at " + DateTime.Now);
                    string destFilename = System.IO.Path.GetFileName(filename).Replace(".osm.pbf", "");

                    ProcessRelationData(areatypename, filename, ParserSettings.JsonMapDataFolder + destFilename + "-MapData-" + areatypename + (ParserSettings.UseHighAccuracy ? "-highAcc" : "-lowAcc") + ".json");                    
                }
                catch (Exception ex)
                {
                    //do nothing, just recover and move on.
                    Log.WriteLog("Attempting last chance processing");
                    LastChanceSerializer(filename, areatype.AreaName);
                }
            }

            Log.WriteLog("Processed " + filename + " at " + DateTime.Now);
            File.Move(filename, filename + "Done");
        }

        public static void LastChanceSerializer(string filename, string areaType)
        {
            //We got here by request, or because we hit OutOfMemory errors processing a file.
            //So we're gonna go real slow now, 100k entries at a time (which is usually a 50-100MB file).
            //this should work on any file with any computer with 8GB of RAM or more. Most passes will work on 4-6GB, the top 1% will go over that.
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

                //TODO: this loop is a duplicate of ProcessRelationData with the limits in place. Consider moving parameters into that to remove duplication.
                bool loadRelations = true;
                while (loadRelations)
                {
                    var osmRelations = GetRelationsFromPbf(filename, areatypename, loopCount * loadCount, loadCount);

                    if (osmRelations.Count() < loadCount)
                        loadRelations = false;

                    usedWays = osmRelations.SelectMany(r => r.Members.Select(m => m.Id)).ToLookup(k => k, v => 0);

                    Log.WriteLog(osmRelations.Count() + " relations found", Log.VerbosityLevels.High);
                    var referencedWays = osmRelations.AsParallel().SelectMany(r => r.Members.Where(m => m.Type == OsmGeoType.Way).Select(m => m.Id)).Distinct().ToLookup(k => k, v => (short)0);
                    Log.WriteLog(referencedWays.Count() + " ways used within relations", Log.VerbosityLevels.High);
                    Log.WriteLog("Relations loaded at " + DateTime.Now);
                    var osmWays = PbfOperations.GetWaysFromPbf(filename, areatypename, referencedWays, true);
                    Log.WriteLog(osmWays.Count() + " ways found", Log.VerbosityLevels.High);
                    var referencedNodes = osmWays.AsParallel().SelectMany(m => m.Nodes).Distinct().ToLookup(k => k, v => (short)0);
                    Log.WriteLog(referencedNodes.Count() + " nodes used by ways", Log.VerbosityLevels.High);
                    Log.WriteLog("Ways loaded at " + DateTime.Now);
                    var osmNodes2 = GetNodesFromPbf(filename, areatypename, referencedNodes, true); //making this by-ref able would probably be the best memory optimization i could still do.
                    referencedNodes = null;
                    Log.WriteLog("Relevant data pulled from file at " + DateTime.Now);

                    ProcessData(osmNodes2, ref osmWays, ref osmRelations, referencedWays, ParserSettings.JsonMapDataFolder + destFilename + "-MapData-" + areatypename + (ParserSettings.UseHighAccuracy ? "-highAcc" : "-lowAcc") + loopCount.ToString() + ".json");
                    loopCount++;
                }
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

                    ProcessData(osmNodes3, ref osmWays2, ref osmRelations2, referencedWays, ParserSettings.JsonMapDataFolder + destFilename + "-MapData-" + areatypename + (ParserSettings.UseHighAccuracy ? "-highAcc" : "-lowAcc") + loopCount.ToString() + ".json");
                    wayLoopCount++;
                    loopCount++;
                }
                Log.WriteLog("Ways processed, moving on to standalone nodes");

                ILookup<long, short> referencedNodes2 = new List<long>().ToLookup(k => k, v => (short)0);
                ILookup<long, short> referencedWays2 = new List<long>().ToLookup(k => k, v => (short)0);
                var osmNodes4 = GetNodesFromPbf(filename, areatypename, referencedNodes2, true);
                var osmWays3 = new List<Way>();
                var osmRelations3 = new List<Relation>();
                ProcessData(osmNodes4, ref osmWays3, ref osmRelations3, referencedWays2, ParserSettings.JsonMapDataFolder + destFilename + "-MapData-" + areatypename + (ParserSettings.UseHighAccuracy ? "-highAcc" : "-lowAcc") + loopCount.ToString() + ".json");
            }
            catch (Exception ex)
            {
                //do nothing, just recover and move on.
                Log.WriteLog("Exception occurred: " + ex.Message + " at " + DateTime.Now + ", moving on");
            }

            Log.WriteLog("Processed " + filename + " at " + DateTime.Now);
            File.Move(filename, filename + "Done");
        }

        //This will mean there's nothing that tries to keep files in memory manually (turns out, windows already does that automatically)
        public static void ProcessRelationData(string areatypename, string pbfFilename, string destFilename)
        {
            Log.WriteLog("Starting " + pbfFilename + " " + areatypename + " data read at " + DateTime.Now);
            List<Relation> osmRelations = GetRelationsFromPbf(pbfFilename, areatypename);
            Log.WriteLog(osmRelations.Count() + " relations found", Log.VerbosityLevels.High);
            var referencedWays = osmRelations.AsParallel().SelectMany(r => r.Members.Where(m => m.Type == OsmGeoType.Way).Select(m => m.Id)).Distinct().ToLookup(k => k, v => (short)0);
            Log.WriteLog(referencedWays.Count() + " ways used within relations", Log.VerbosityLevels.High);
            Log.WriteLog("Relations loaded at " + DateTime.Now);
            var osmWays = GetWaysFromPbf(pbfFilename, areatypename, referencedWays);
            Log.WriteLog(osmWays.Count() + " ways found", Log.VerbosityLevels.High);
            var referencedNodes = osmWays.AsParallel().SelectMany(m => m.Nodes).Distinct().ToLookup(k => k, v => (short)0);
            Log.WriteLog(referencedNodes.Count() + " nodes used by ways", Log.VerbosityLevels.High);
            Log.WriteLog("Ways loaded at " + DateTime.Now);
            var osmNodes = GetNodesFromPbf(pbfFilename, areatypename, referencedNodes); //making this by-ref able would probably be the best memory optimization i could still do.
            referencedNodes = null;
            Log.WriteLog("Relevant data pulled from file at " + DateTime.Now);

            //Write content directly to file.
            ProcessData(osmNodes, ref osmWays, ref osmRelations, referencedWays, destFilename);
        }
    }
}
