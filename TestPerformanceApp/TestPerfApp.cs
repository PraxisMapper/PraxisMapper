using CoreComponents;
using CoreComponents.Support;
using Google.Common.Geometry;
using Google.OpenLocationCode;
using NetTopologySuite;
using NetTopologySuite.Geometries;
using OsmSharp;
using OsmSharp.Streams;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using static CoreComponents.DbTables;
using static CoreComponents.MapSupport;

namespace PerformanceTestApp
{
    class TestPerfApp
    {
        //fixed values here for testing stuff later. Adjust to your own preferences or to fit your data set.
        static string cell8 = "8FW4V722";
        static string cell6 = "8FW4V7"; //Eiffel Tower and surrounding area. Use for global data
        static string cell4 = "8FW4";
        //static string cell2 = "8F";

        //a test structure, is slower than not using it.
        public record MapDataAbbreviated(string name, string type, Geometry place);

        static void Main(string[] args)
        {
            PraxisContext.connectionString = "Data Source=localhost\\SQLDEV;UID=GpsExploreService;PWD=lamepassword;Initial Catalog=Praxis;";

            if (Debugger.IsAttached)
                Console.WriteLine("Run this in Release mode for accurate numbers!");
            //This is for running and archiving performance tests on different code approaches.
            //PerformanceInfoEFCoreVsSproc();
            //S2VsPlusCode();
            //SplitAreaValues();
            //TestPlaceLookupPlans();
            //TestSpeedChangeByArea();
            //TestGetPlacesPerf();
            //TestMapDataAbbrev();
            //TestFileVsMemoryStream();
            //TestMultiPassVsSinglePass();
            //TestFlexEndpoint();
            //MicroBenchmark();
            //ConcurrentTest();
            CalculateScoreTest();



            //TODO: consider pulling 4-cell worth of places into memory, querying against that instead of a DB lookup every time?
        }

        private static void TestMultiPassVsSinglePass()
        {
            System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
            string filename = @"D:\Projects\PraxisMapper Files\XmlToProcess\ohio-latest.osm.pbf"; //160MB PBF
            FileStream fs2 = new FileStream(filename, FileMode.Open);
            byte[] fileInRam = new byte[fs2.Length];
            fs2.Read(fileInRam, 0, (int)fs2.Length);
            MemoryStream ms = new MemoryStream(fileInRam);
            
            sw.Start();
            GetRelationsFromStream(ms, null);
            sw.Stop();
            Log.WriteLog("Reading all types took " + sw.ElapsedMilliseconds + "ms.");
            sw.Restart();
            ms.Position = 0;
            GetRelationsFromStream(ms, "water");
            sw.Stop();
            Log.WriteLog("Reading water type took " + sw.ElapsedMilliseconds + "ms.");
            sw.Restart();
            ms.Position = 0;
            GetRelationsFromStream(ms, "cemetery");
            sw.Stop();
            Log.WriteLog("Reading cemetery type took " + sw.ElapsedMilliseconds + "ms.");


        }

        public static List<CoordPair> GetRandomCoords(int count)
        {
            List<CoordPair> results = new List<CoordPair>();
            results.Capacity = count;

            for (int i = 0; i < count; i++)
                results.Add(MapSupport.GetRandomCoordPair());

            return results;
        }

        public static List<CoordPair> GetRandomBoundedCoords(int count)
        {
            List<CoordPair> results = new List<CoordPair>();
            results.Capacity = count;

            for (int i = 0; i < count; i++)
                results.Add(GetRandomBoundedCoordPair());

            return results;
        }


        public static void PerformanceInfoEFCoreVsSproc()
        {
            int count = 100;
            System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
            sw.Start();
            for (int i = 0; i < count; i++)
            {
                PerformanceTracker pt = new PerformanceTracker("test");
                pt.Stop();
            }
            sw.Stop();
            long EfCoreInsertTime = sw.ElapsedMilliseconds;

            sw.Restart();
            for (int i = 0; i < count; i++)
            {
                PerformanceTracker pt = new PerformanceTracker("test");
                pt.StopNoChangeTracking();
            }
            sw.Stop();
            long NoCTInsertTime = sw.ElapsedMilliseconds;
            
            sw.Restart();
            for (int i = 0; i < count; i++)
            {
                PerformanceTracker pt = new PerformanceTracker("test");
                pt.StopSproc();
            }
            sw.Stop();
            long SprocInsertTime = sw.ElapsedMilliseconds;

            Log.WriteLog("PerformanceTracker EntityFrameworkCore total  /average speed: " + EfCoreInsertTime + " / " + (EfCoreInsertTime / count) + "ms.");
            Log.WriteLog("PerformanceTracker EntityFrameworkCore NoChangeTracking total /average speed: " + NoCTInsertTime + " / "  + (NoCTInsertTime / count) + "ms.");
            Log.WriteLog("PerformanceTracker Sproc total / average speed: " + SprocInsertTime + " / " + (SprocInsertTime / count) + "ms.");
        }

        public static void S2VsPlusCode()
        {
            //Testing how fast the conversion between coords and areas is here.
            int count = 10000;
            var testPointList = GetRandomCoords(count);
            System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();

            sw.Start();
            foreach(var coords in testPointList)
            {
                OpenLocationCode olc = new OpenLocationCode(coords.lat, coords.lon); //creates data from coords
                var area = olc.Decode(); //an area i can use for intersects() calls in the DB
            }
            sw.Stop();
            var PlusCodeConversion = sw.ElapsedMilliseconds;
            
            sw.Restart();
            foreach (var coords in testPointList)
            {
                S2LatLng s2 = S2LatLng.FromDegrees(coords.lat, coords.lon); //creates data from coords
                S2CellId c = S2CellId.FromLatLng(s2); //this gives a usable area, I think.
            }
            sw.Stop();
            var S2Conversion = sw.ElapsedMilliseconds;

            Log.WriteLog("PlusCode conversion total / average time: " + PlusCodeConversion +  " / " + (PlusCodeConversion / count) + " ms");
            Log.WriteLog("S2 conversion total / average time: " + S2Conversion + " / " + (S2Conversion / count) + " ms");

        }

        public static void SplitAreaValues()
        {
            //Load an area, see what value of splits is the fastest.
            //I currently think its 40.
            System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
            sw.Start();
            //Pick a specific area for testing, since we want to compare the math.
            string plusCode6 = cell6;
            var db = new CoreComponents.PraxisContext();
            var places = MapSupport.GetPlaces(OpenLocationCode.DecodeValid(plusCode6));  //All the places in this 6-code
            var box = OpenLocationCode.DecodeValid(plusCode6);
            sw.Stop();
            Log.WriteLog("Pulling " + places.Count + " places in 6-cell took " + sw.ElapsedMilliseconds + "ms");

            int[] splitChecks = new int[] { 1, 2, 4, 8, 10, 20, 25, 32, 40, 80, 100 };
            foreach (int splitcount in splitChecks)
            {
                sw.Restart();
                List<MapData>[] placeArray;
                GeoArea[] areaArray;
                StringBuilder[] sbArray = new StringBuilder[splitcount * splitcount];
                MapSupport.SplitArea(box, splitcount, places, out placeArray, out areaArray);
                System.Threading.Tasks.Parallel.For(0, placeArray.Length, (i) =>
                {
                    sbArray[i] = MapSupport.SearchArea(ref areaArray[i], ref placeArray[i]);
                });
                sw.Stop();
                Log.WriteLog("dividing map by " + splitcount + " took " + sw.ElapsedMilliseconds + " ms");
            }
        }

        public static void TestPlaceLookupPlans()
        {
            //For determining which way of finding areas is faster.
            //Unfortunately, only intersects finds ways/points unless youre exactly standing on them.
            System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();

            List<long> intersectsPolygonRuntimes = new List<long>(50);
            List<long> containsPointRuntimes = new List<long>(50);
            List<long> AlgorithmRuntimes = new List<long>(50);
            List<long> precachedAlgorithmRuntimes = new List<long>(50);

            //tryint to determine the fastest way to search areas. Pull a 6-cell's worth of data from the DB, then parse it into 10cells.
            //Option 1: make a box, check Intersects.
            //Option 2: make a point, check Contains. (NOTE: a polygon does not Contain() its boundaries, so a point directly on a boundary line will not be identified)
            //Option 3: try NetTopologySuite.Algorithm.Locate.IndexedPointInAreaLocator ?
            //Option 4: consider using Contains against something like NetTopologySuite.Geometries.Prepared.PreparedGeometryFactory().Prepare(geom) instead of just Place? This might be outdated

            var factory = NtsGeometryServices.Instance.CreateGeometryFactory(srid: 4326); //SRID matches Plus code values. //share this here, so i compare the actual algorithms instead of this boilerplate, mandatory entry.
            var db = new PraxisContext();

            for (int i = 0; i < 50; i++)
            {

                var point = MapSupport.GetRandomBoundedCoordPair();
                var olc = OpenLocationCode.Encode(point.lat, point.lon);
                var codeString = olc.Substring(0, 6);
                sw.Restart();
                var box = OpenLocationCode.DecodeValid(codeString);
                var cord1 = new NetTopologySuite.Geometries.Coordinate(box.Min.Longitude, box.Min.Latitude);
                var cord2 = new NetTopologySuite.Geometries.Coordinate(box.Min.Longitude, box.Max.Latitude);
                var cord3 = new NetTopologySuite.Geometries.Coordinate(box.Max.Longitude, box.Max.Latitude);
                var cord4 = new NetTopologySuite.Geometries.Coordinate(box.Max.Longitude, box.Min.Latitude);
                var cordSeq = new NetTopologySuite.Geometries.Coordinate[5] { cord4, cord3, cord2, cord1, cord4 };
                var location = factory.CreatePolygon(cordSeq); //the 6 cell.

                var places = db.MapData.Where(md => md.place.Intersects(location)).ToList();
                double resolution10 = .000125; //as defined
                for (double x = box.Min.Longitude; x <= box.Max.Longitude; x += resolution10)
                {
                    for (double y = box.Min.Latitude; y <= box.Max.Latitude; y += resolution10)
                    {
                        //also remember these coords start at the lower-left, so i can add the resolution to get the max bounds
                        var olcInner = new OpenLocationCode(y, x); //This takes lat, long, Coordinate takes X, Y. This line is correct.
                        var cordSeq2 = new NetTopologySuite.Geometries.Coordinate[5] { new NetTopologySuite.Geometries.Coordinate(x, y), new NetTopologySuite.Geometries.Coordinate(x + resolution10, y), new NetTopologySuite.Geometries.Coordinate(x + resolution10, y + resolution10), new NetTopologySuite.Geometries.Coordinate(x, y + resolution10), new NetTopologySuite.Geometries.Coordinate(x, y) };
                        var poly2 = factory.CreatePolygon(cordSeq2);
                        var entriesHere = places.Where(md => md.place.Intersects(poly2)).ToList();

                    }
                }
                sw.Stop(); //measuring time it takes to parse a 6-cell down to 10-cells.and wou
                intersectsPolygonRuntimes.Add(sw.ElapsedMilliseconds);
            }

            for (int i = 0; i < 50; i++)
            {
                var point = MapSupport.GetRandomBoundedCoordPair();
                var olc = OpenLocationCode.Encode(point.lat, point.lon);
                var codeString = olc.Substring(0, 6);
                sw.Restart();
                var box = OpenLocationCode.DecodeValid(codeString);
                var cord1 = new NetTopologySuite.Geometries.Coordinate(box.Min.Longitude, box.Min.Latitude);
                var cord2 = new NetTopologySuite.Geometries.Coordinate(box.Min.Longitude, box.Max.Latitude);
                var cord3 = new NetTopologySuite.Geometries.Coordinate(box.Max.Longitude, box.Max.Latitude);
                var cord4 = new NetTopologySuite.Geometries.Coordinate(box.Max.Longitude, box.Min.Latitude);
                var cordSeq = new NetTopologySuite.Geometries.Coordinate[5] { cord4, cord3, cord2, cord1, cord4 };
                var location = factory.CreatePolygon(cordSeq); //the 6 cell.

                var places = db.MapData.Where(md => md.place.Intersects(location)).ToList();
                double resolution10 = .000125; //as defined
                for (double x = box.Min.Longitude; x <= box.Max.Longitude; x += resolution10)
                {
                    for (double y = box.Min.Latitude; y <= box.Max.Latitude; y += resolution10)
                    {
                        //Option 2, is Contains on a point faster?
                        var location2 = factory.CreatePoint(new NetTopologySuite.Geometries.Coordinate(x, y));
                        var places2 = places.Where(md => md.place.Contains(location)).ToList();

                    }
                }
                sw.Stop(); //measuring time it takes to parse a 6-cell down to 10-cells.and wou
                containsPointRuntimes.Add(sw.ElapsedMilliseconds);
            }

            for (int i = 0; i < 50; i++)
            {
                var point = MapSupport.GetRandomBoundedCoordPair();
                var olc = OpenLocationCode.Encode(point.lat, point.lon);
                var codeString = olc.Substring(0, 6);
                sw.Restart();
                var box = OpenLocationCode.DecodeValid(codeString);
                var cord1 = new NetTopologySuite.Geometries.Coordinate(box.Min.Longitude, box.Min.Latitude);
                var cord2 = new NetTopologySuite.Geometries.Coordinate(box.Min.Longitude, box.Max.Latitude);
                var cord3 = new NetTopologySuite.Geometries.Coordinate(box.Max.Longitude, box.Max.Latitude);
                var cord4 = new NetTopologySuite.Geometries.Coordinate(box.Max.Longitude, box.Min.Latitude);
                var cordSeq = new NetTopologySuite.Geometries.Coordinate[5] { cord4, cord3, cord2, cord1, cord4 };
                var location = factory.CreatePolygon(cordSeq); //the 6 cell.

                //var places = db.MapData.Where(md => md.place.Intersects(location)).ToList();
                var indexedIn = db.MapData.Where(md => md.place.Contains(location)).Select(md => new NetTopologySuite.Algorithm.Locate.IndexedPointInAreaLocator(md.place)).ToList();
                var fakeCoord = new NetTopologySuite.Geometries.Coordinate(point.lon, point.lat);
                foreach (var ii in indexedIn)
                    ii.Locate(fakeCoord); //force index creation on all items now instead of later.

                double resolution10 = .000125; //as defined
                for (double x = box.Min.Longitude; x <= box.Max.Longitude; x += resolution10)
                {
                    for (double y = box.Min.Latitude; y <= box.Max.Latitude; y += resolution10)
                    {
                        //Option 2, is Contains on a point faster?
                        var location2 = new NetTopologySuite.Geometries.Coordinate(x, y);
                        var places3 = indexedIn.Where(i => i.Locate(location2) == NetTopologySuite.Geometries.Location.Interior);
                    }
                }
                sw.Stop(); //measuring time it takes to parse a 6-cell down to 10-cells.and wou
                precachedAlgorithmRuntimes.Add(sw.ElapsedMilliseconds);
            }

            //these commented numbers are out of date.
            //var a = AlgorithmRuntimes.Average();
            var b = intersectsPolygonRuntimes.Average();
            var c = containsPointRuntimes.Average(); 
            var d = precachedAlgorithmRuntimes.Average();

            Log.WriteLog("Intersect test average result is " + b + "ms");
            Log.WriteLog("Contains Point test average result is " + c + "ms");
            Log.WriteLog("Precached point test average result is " + d + "ms");


            return;
        }

        public static void TestSpeedChangeByArea()
        {
            //See how fast it is to look up a bigger area vs smaller ones.
            System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
            long avg8 = 0, avg6 = 0, avg4 = 0; //, avg2 = 0;

            int loopCount = 5;
            for (int i = 0; i < loopCount; i++)
            {
                if (i == 0)
                    Log.WriteLog("First loop has some warmup time.");

                sw.Restart();
                GeoArea area8 = OpenLocationCode.DecodeValid(cell8);
                var eightCodePlaces = GetPlacesNoTrack(area8);
                sw.Stop();
                var eightCodeTime = sw.ElapsedMilliseconds;
                avg8 += eightCodeTime;

                sw.Restart();
                GeoArea area6 = OpenLocationCode.DecodeValid(cell6);
                var sixCodePlaces = MapSupport.GetPlaces(area6);
                sw.Stop();
                var sixCodeTime = sw.ElapsedMilliseconds;
                avg6 += sixCodeTime;

                sw.Restart();
                GeoArea area4 = OpenLocationCode.DecodeValid(cell4);
                var fourCodePlaces = MapSupport.GetPlaces(area4);
                sw.Stop();
                var fourCodeTime = sw.ElapsedMilliseconds;
                avg4 += fourCodeTime;

                //2 codes on global data is silly.
                //sw.Restart();
                //GeoArea area2 = OpenLocationCode.DecodeValid(cell2);
                //var twoCodePlaces = MapSupport.GetPlaces(area2);
                //sw.Stop();
                //var twoCodeTime = sw.ElapsedMilliseconds;
                //avg2 += twoCodeTime;

                Log.WriteLog("8-code search time is " + eightCodeTime + "ms");
                Log.WriteLog("6-code search time is " + sixCodeTime + "ms");
                Log.WriteLog("4-code search time is " + fourCodeTime + "ms");
                //Log.WriteLog("2-code search time is " + twoCodeTime + "ms");
            }
            //If this was linear, each one should take 400x as long as the previous one. (20x20 grid = 400 calls to the smaller level)
            Log.WriteLog("Average 8-code search time is " + (avg8 / loopCount) + "ms");
            Log.WriteLog("6-code search time would be " +   (avg8 * 400 / loopCount) + " linearly, is actually " + avg6 + " (" + ((avg8 * 400 / loopCount) / avg6) + "x faster)");
            Log.WriteLog("Average 6-code search time is " + (avg6 / loopCount) + "ms");
            Log.WriteLog("4-code search time would be " + (avg6 * 400 / loopCount) + " linearly, is actually " + avg4 + " (" + ((avg6 * 400 / loopCount) / avg4) + "x faster)");
            Log.WriteLog("Average 4-code search time is " + (avg4 / loopCount) + "ms");
            //Log.WriteLog("2-code search time would be " + (avg4 * 400 / loopCount) + " linearly, is actually " + avg2 + " (" + ((avg4 * 400 / loopCount) / avg2) + "x faster)");
            //Log.WriteLog("Average 2-code search time is " + (avg2 / loopCount) + "ms");


        }

        public static void TestGetPlacesPerf()
        {
            for (int i = 0; i < 5; i++)
            {
                System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
                sw.Restart();
                GeoArea area6 = OpenLocationCode.DecodeValid(cell6);
                var sixCodePlaces = GetPlacesBase(area6);
                sw.Stop();
                var sixCodeTime = sw.ElapsedMilliseconds;
                sw.Restart();
                var sixCodePlacesNT = GetPlacesNoTrack(area6);
                sw.Stop();
                var sixCodeNTTime = sw.ElapsedMilliseconds;
                sw.Restart();
                var sixCodePlacesPrecomp = GetPlacesPrecompiled(area6);
                sw.Stop();
                var sixCodePrecompTime = sw.ElapsedMilliseconds;
                Log.WriteLog("6code- Tracking: " + sixCodeTime + "ms VS NoTracking: " + sixCodeNTTime + "ms VS Precompiled: " + sixCodePrecompTime + "ms");


                sw.Restart();
                GeoArea area4 = OpenLocationCode.DecodeValid(cell4);
                var fourCodePlaces = GetPlacesBase(area4);
                sw.Stop();
                var fourCodeTime = sw.ElapsedMilliseconds;
                sw.Restart();
                var fourCodePlacesNT = GetPlacesNoTrack(area4);
                sw.Stop();
                var fourCodeNTTime = sw.ElapsedMilliseconds;
                sw.Restart();
                var fourCodePlacesPrecomp = GetPlacesPrecompiled(area4);
                sw.Stop();
                var fourCodePrecompTime = sw.ElapsedMilliseconds;
                Log.WriteLog("4code- Tracking: " + fourCodeTime + "ms VS NoTracking: " + fourCodeNTTime + "ms VS Precompiled: " + fourCodePrecompTime + "ms");
            }
        }

        public static List<MapData> GetPlacesBase(GeoArea area, List<MapData> source = null)
        {
            var coordSeq = GeoAreaToCoordArray(area);
            var location = factory.CreatePolygon(coordSeq);
            List<MapData> places;
            if (source == null)
            {
                var db = new CoreComponents.PraxisContext();
                places = db.MapData.Where(md => md.place.Intersects(location)).ToList();
            }
            else
                places = source.Where(md => md.place.Intersects(location)).ToList();
            return places;
        }

        public static List<MapData> GetPlacesPrecompiled(GeoArea area, List<MapData> source = null)
        {
            var coordSeq = GeoAreaToCoordArray(area);
            var location = factory.CreatePolygon(coordSeq);
            List<MapData> places;
            if (source == null)
            {
                var db = new CoreComponents.PraxisContext();
                places = db.getPlaces((location)).ToList();
            }
            else
                places = source.Where(md => md.place.Intersects(location)).ToList();
            return places;
        }

        public static List<MapData> GetPlacesNoTrack(GeoArea area, List<MapData> source = null)
        {
            //TODO: this seems to have a lot of warmup time that I would like to get rid of. Would be a huge performance improvement.
            //The flexible core of the lookup functions. Takes an area, returns results that intersect from Source. If source is null, looks into the DB.
            //Intersects is the only indexable function on a geography column I would want here. Distance and Equals can also use the index, but I don't need those in this app.
            var coordSeq = GeoAreaToCoordArray(area);
            var location = factory.CreatePolygon(coordSeq);
            List<MapData> places;
            if (source == null)
            {
                var db = new CoreComponents.PraxisContext();
                db.ChangeTracker.AutoDetectChangesEnabled = false;
                places = db.MapData.Where(md => md.place.Intersects(location)).ToList();
            }
            else
                places = source.Where(md => md.place.Intersects(location)).ToList();
            return places;
        }

        public static void TestMapDataAbbrev()
        {
            System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
            var db = new CoreComponents.PraxisContext();

            for (int i = 0; i < 5; i++)
            {
                sw.Restart();
                var places2 = db.MapData.Take(10000).ToList();
                sw.Stop();
                var placesTime = sw.ElapsedMilliseconds;
                sw.Restart();
                var places3 = db.MapData.Take(10000).Select(m => new MapDataAbbreviated(m.name, m.type, m.place)).ToList();
                sw.Stop();
                var abbrevTime = sw.ElapsedMilliseconds;

                Log.WriteLog("Full data time took " + placesTime + "ms");
                Log.WriteLog("short data time took " + abbrevTime + "ms");
            }
        }

        public static void TestPrecompiledQuery()
        {
            System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
            var db = new CoreComponents.PraxisContext();
            

            for (int i = 0; i < 5; i++)
            {
                sw.Restart();
                var places1 = GetPlacesBase(OpenLocationCode.DecodeValid(cell6));
                sw.Stop();
                var placesTime = sw.ElapsedMilliseconds;
                sw.Restart();
                var places2 = GetPlacesPrecompiled(OpenLocationCode.DecodeValid(cell6));
                sw.Stop();
                var abbrevTime = sw.ElapsedMilliseconds;

                Log.WriteLog("Full data time took " + placesTime + "ms");
                Log.WriteLog("short data time took " + abbrevTime + "ms");
            }
        }

        public static void TestFileVsMemoryStream()
        {
            //reading everything from disk took ~55 seconds.
            //the memorystream alternative took ~33 seconds. But this difference goes away largely by using the filter commands
            //eX: on relations it's 22 seconds vs 21 seconds.
            //So there's some baseline performance floor that's probably disk dependent.
            Log.WriteLog("Starting memorystream perf test at " + DateTime.Now);
            System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
            string filename = @"D:\Projects\PraxisMapper Files\XmlToProcess\ohio-latest.osm.pbf"; //160MB PBF

            sw.Start();
            //using (var fs = System.IO.File.OpenRead(filename))
            //{
            //    List<OsmSharp.Relation> filteredRelations = new List<OsmSharp.Relation>();
            //    List<MapData> contents = new List<MapData>();
            //    contents.Capacity = 100000;

            //    var source = new PBFOsmStreamSource(fs);
            //    var progress = source.ShowProgress();

            //    //List<OsmSharp.Relation> filteredEntries;
            //        var filteredEntries = progress //.Where(p => p.Type == OsmGeoType.Relation)
            //        //.Select(p => (OsmSharp.Relation)p)
            //        .ToList();

            //}
            sw.Stop();
            Log.WriteLog("Reading from file took " + sw.ElapsedMilliseconds + "ms");
            
            sw.Restart();
            FileStream fs2 = new FileStream(filename, FileMode.Open);
            byte[] fileInRam = new byte[fs2.Length];
            fs2.Read(fileInRam, 0, (int)fs2.Length);
            MemoryStream ms = new MemoryStream(fileInRam);
            List<OsmSharp.Relation> filteredRelations2 = new List<OsmSharp.Relation>();
            List<MapData> contents2 = new List<MapData>();
            contents2.Capacity = 100000;

            var source2 = new PBFOsmStreamSource(ms);
            var progress2 = source2.ShowProgress();

            //List<OsmSharp.Relation> filteredEntries2;
            var filteredEntries2 = progress2 //.Where(p => p.Type == OsmGeoType.Relation)
            //.Select(p => (OsmSharp.Relation)p)
            .ToList();
            sw.Stop();

            Log.WriteLog("Reading to MemoryStream and processing took " + sw.ElapsedMilliseconds + "ms");

        }

        private static List<OsmSharp.Relation> GetRelationsFromPbf(string filename, string areaType)
        {
            //Read through a file for stuff that matches our parameters.
            List<OsmSharp.Relation> filteredRelations = new List<OsmSharp.Relation>();
            using (var fs = File.OpenRead(filename))
            {
                filteredRelations = InnerGetRelations(fs, areaType);
            }
            return filteredRelations;
        }

        private static List<OsmSharp.Relation> GetRelationsFromStream(Stream file, string areaType)
        {
            //Read through a file for stuff that matches our parameters.
            List<OsmSharp.Relation> filteredRelations = new List<OsmSharp.Relation>();
            file.Position = 0;
            return InnerGetRelations(file, areaType);
        }

        private static List<OsmSharp.Relation> InnerGetRelations(Stream stream, string areaType)
        {
            var source = new PBFOsmStreamSource(stream);
            var progress = source.ShowProgress();

            List<OsmSharp.Relation> filteredEntries;
            if (areaType == null)
                filteredEntries = progress.Where(p => p.Type == OsmGeoType.Relation &&
                    MapSupport.GetElementType(p.Tags) != "")
                .Select(p => (OsmSharp.Relation)p)
                .ToList();
            else if (areaType == "admin")
                filteredEntries = progress.Where(p => p.Type == OsmGeoType.Relation &&
                    MapSupport.GetElementType(p.Tags).StartsWith(areaType))
                .Select(p => (OsmSharp.Relation)p)
                .ToList();
            else
                filteredEntries = progress.Where(p => p.Type == OsmGeoType.Relation &&
                MapSupport.GetElementType(p.Tags) == areaType
            )
                .Select(p => (OsmSharp.Relation)p)
                .ToList();

            return filteredEntries;
        }

        private static void MemoryTest()
        {
            //floats and doubles don't seem to make an actual difference in my app's memory usage unless it's huge, like Norway. Weird. Check that out here.
            
        }

        private static void TestFlexEndpoint()
        {
            string website = "http://localhost/GPSExploreServerAPI/MapData/flexarea/41.565188/-81.435063/";

            WebClient wc = new WebClient();
            for (double i = .0001; i <= 1; i += .001) //roughly a 10cell in size, expand radius by 1 each loop.
                wc.DownloadString(website + i);
        }

        private static void MicroBenchmark()
        {
            //Measure performance of various things in timer ticks instead of milliseconds.
            //Might be useful to measure CPU performance across machines.
            Stopwatch sw = new Stopwatch();

            NetTopologySuite.IO.WKTReader reader = new NetTopologySuite.IO.WKTReader();
            reader.DefaultSRID = 4326;
            string testPlaceWKT = "POLYGON ((-83.737174987792969 40.103412628173828, -83.734664916992188 40.101036071777344, -83.732452392578125 40.100399017333984, -83.7278823852539 40.100162506103516, -83.7275390625 40.102806091308594, -83.737174987792969 40.103412628173828))";
            //check on performance for reading and writing a MapData entry to Json file.
            //Fixed MapData Entry
            MapDataForJson test1 = new MapDataForJson("TestPlace", testPlaceWKT, "Way", 12345, null, null, 1);
            string tempFile = System.IO.Path.GetTempFileName();
            sw.Start();
            //WriteMapDataToFile(tempFile, ref l);
            var test2 = JsonSerializer.Serialize(test1, typeof(MapDataForJson));
            sw.Stop();
            Log.WriteLog("Single MapData to Json took " + sw.ElapsedTicks + " ticks (" + sw.ElapsedMilliseconds + "ms)");
            sw.Restart();
            MapDataForJson j = (MapDataForJson)JsonSerializer.Deserialize(test2, typeof(MapDataForJson));
            sw.Stop();
            Log.WriteLog("Single Json to MapData took " + sw.ElapsedTicks + " ticks (" + sw.ElapsedMilliseconds + "ms)");
            File.Delete(tempFile); //Clean up after ourselves.
            sw.Restart();
            var test3 = reader.Read(testPlaceWKT);
            sw.Stop();
            Log.WriteLog("Converting 1 polygon from Text to Geometry took " + sw.ElapsedTicks + " ticks (" + sw.ElapsedMilliseconds + "ms)");
            sw.Restart();
            var result3 = MapSupport.CCWCheck((Polygon)test3);
            sw.Stop();
            Log.WriteLog("Single CCWCheck on 5-point polygon took " + sw.ElapsedTicks + " ticks (" + sw.ElapsedMilliseconds + "ms)");
        }

        private static void ConcurrentTest()
        {
            string filename = @"D:\Projects\PraxisMapper Files\XmlToProcess\ohio-latest.osm.pbf"; //160MB PBF
            FileStream fs2 = new FileStream(filename, FileMode.Open);
            byte[] fileInRam = new byte[fs2.Length];
            fs2.Read(fileInRam, 0, (int)fs2.Length);
            MemoryStream ms = new MemoryStream(fileInRam);
            List<OsmSharp.Relation> filteredRelations2 = new List<OsmSharp.Relation>();
            List<MapData> contents2 = new List<MapData>();
            contents2.Capacity = 100000;

            var source2 = new PBFOsmStreamSource(ms);
            var progress2 = source2.ShowProgress();

            //List<OsmSharp.Relation> filteredEntries2;
            var normalListTest = progress2
                .Where(p => p.Type == OsmGeoType.Relation)
                .Select(p => (Relation)p)
            .ToList();

            var concurrentTest = new ConcurrentBag<OsmSharp.Relation>(normalListTest);
            Log.WriteLog("Both data sources populated. Starting test.");

            Stopwatch sw = new Stopwatch();
            sw.Start();
            var data1 = normalListTest.AsParallel().Select(r => r.Members.Count()).ToList();
            sw.Stop();
            Log.WriteLog("Standard list took " + sw.ElapsedTicks + " ticks (" + sw.ElapsedMilliseconds + "ms)");
            sw.Restart();
            var data2 = concurrentTest.AsParallel().Select(r => r.Members.Count()).ToList();
            sw.Stop();
            Log.WriteLog("ConcurrentBag took " + sw.ElapsedTicks + " ticks (" + sw.ElapsedMilliseconds + "ms)");

            //lets do lookup VS dictionary vs concurrentdictionary. This eats a lot more RAM with nodes.
            var list = progress2.Where(p => p.Type == OsmGeoType.Way).Select(p => (OsmSharp.Way)p).ToList();
            //TODO: time populating these entreis.
            var lookup = list.ToLookup(k => k.Id, v => v);
            var dictionary = list.ToDictionary(k => k.Id, v => v);
            var conDict = new ConcurrentDictionary<long, OsmSharp.Way>();
            foreach (var entry in list)
                conDict.Append(new KeyValuePair<long, OsmSharp.Way>(entry.Id.Value, entry));

            sw.Restart();
            var data3 = lookup.AsParallel().Select(l => l.First()).ToList();
            sw.Stop();
            Log.WriteLog("Standard lookup took " + sw.ElapsedTicks + " ticks (" + sw.ElapsedMilliseconds + "ms)");
            sw.Restart();
            data3 = dictionary.AsParallel().Select(l => l.Value).ToList(); sw.Stop();
            Log.WriteLog("Standard dictionary took " + sw.ElapsedTicks + " ticks (" + sw.ElapsedMilliseconds + "ms)");
            sw.Restart();
            data3 = conDict.AsParallel().Select(l => l.Value).ToList(); sw.Stop();
            Log.WriteLog("Concurrent Dictionary took " + sw.ElapsedTicks + " ticks (" + sw.ElapsedMilliseconds + "ms)");
            sw.Restart();
        }

        public static void CalculateScoreTest()
        {
            //on testing, the slowest random result was 13ms.  Most are 0-1ms.
            var db = new PraxisContext();
            var randomCap = db.MapData.Count();
            Random r = new Random();
            string website = "http://localhost/GPSExploreServerAPI/MapData/CalculateMapDataScore/";
            for (int i = 0; i <100; i++)
            {
                WebClient wc = new WebClient();
                wc.DownloadString(website + r.Next(1, randomCap));
            }
        }

        public static void TestRecordVsStringBuilders()
        {
            List<MapData> mapData = new List<MapData>();
            StringBuilder sb = new StringBuilder();
            GeoArea area = new GeoArea(1, 2, 3, 4);


            var xCells = area.LongitudeWidth / resolutionCell10;
            var yCells = area.LatitudeHeight / resolutionCell10;

            Stopwatch sw = new Stopwatch();
            sw.Start();
            for (double xx = 0; xx < xCells; xx += 1)
            {
                for (double yy = 0; yy < yCells; yy += 1)
                {
                    double x = area.Min.Longitude + (resolutionCell10 * xx);
                    double y = area.Min.Latitude + (resolutionCell10 * yy);

                    var placesFound = MapSupport.FindPlacesIn10Cell(x, y, ref mapData, true);
                    if (!string.IsNullOrWhiteSpace(placesFound))
                        sb.AppendLine(placesFound);
                }
            }
            sw.Stop();
            Log.WriteLog("Searched and built String response in " + sw.ElapsedMilliseconds);

            //now test again with new function
            List<Cell10Info> info = new List<Cell10Info>();
            sw.Restart();
            for (double xx = 0; xx < xCells; xx += 1)
            {
                for (double yy = 0; yy < yCells; yy += 1)
                {
                    double x = area.Min.Longitude + (resolutionCell10 * xx);
                    double y = area.Min.Latitude + (resolutionCell10 * yy);

                    var placesFound = MapSupport.CellInfoFindPlacesIn10Cell(x, y, ref mapData);
                    if (placesFound != null)
                        info.Add(placesFound);
                }
            }
            sw.Stop();
            Log.WriteLog("Searched and built Cell10Info Record response in " + sw.ElapsedMilliseconds);

            //todo: also check parsing record results to string for api endpoint
            StringBuilder sb2 = new StringBuilder();
            sw.Restart();
            foreach (var place in info.Select(i => i.placeName).Distinct())
            {
                var codes = string.Join(",", info.Where(i => i.placeName == place).Select(i => i.Cell10));
                sb.Append(place + "|" + codes + Environment.NewLine);
            }
            sw.Stop();
            Log.WriteLog("converted record list to string output in " + sw.ElapsedMilliseconds);
        }
    }
}
