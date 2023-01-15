using CryptSharp;
using Google.Common.Geometry;
using Google.OpenLocationCode;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Prepared;
using OsmSharp;
using OsmSharp.API;
using OsmSharp.Streams;
using PraxisCore;
using PraxisCore.PbfReader;
using PraxisCore.Support;
using SkiaSharp;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Numerics;
using System.Reflection;
using System.Text;
using static PraxisCore.DbTables;
using static PraxisCore.GeometrySupport;
using static PraxisCore.Place;
using static PraxisCore.Singletons;
using static PraxisCore.StandaloneDbTables;

namespace PerformanceTestApp
{
    class TestPerfApp
    {
        //fixed values here for testing stuff later. Adjust to your own preferences or to fit your data set.
        static string cell8 = "8FW4V722";
        static string cell6 = "8FW4V7"; //Eiffel Tower and surrounding area. Use for global data
        static string cell4 = "8FW4";
        //static string cell2 = "8F";

        static IMapTiles MapTiles;

        //a test structure, is slower than not using it.
        public record MapDataAbbreviated(string name, string type, Geometry place);

        static void Main(string[] args)
        {
            var asm = Assembly.LoadFrom(@"PraxisMapTilesSkiaSharp.dll");
            var MapTiles = Activator.CreateInstance(asm.GetType("PraxisCore.MapTiles"));

            //PraxisContext.serverMode = "SQLServer";
            //PraxisContext.connectionString = "Data Source=localhost\\SQLDEV;UID=GpsExploreService;PWD=lamepassword;Initial Catalog=Praxis;";

            PraxisContext.serverMode = "MariaDB";
            PraxisContext.connectionString = "server=localhost;database=praxis;user=root;password=asdf;";

            //PraxisContext.serverMode = "PostgreSQL";
            //PraxisContext.connectionString = "server=localhost;database=praxis;user=root;password=asdf;";

            //TagParser.Initialize();
            TagParser.Initialize(false, (IMapTiles)MapTiles);

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
            //CalculateScoreTest();
            //TestIntersectsPreparedVsNot();
            //TestRasterVsVectorCell8();
            //TestRasterVsVectorCell10();
            //TestImageSharpVsSkiaSharp(); //imagesharp was removed for being vastly slower.
            //TestTagParser();
            //TestCropVsNoCropDraw("86HWPM");
            //TestCropVsNoCropDraw("86HW");
            //TestCustomPbfReader();
            //TestDrawingHoles();
            //TestPbfParsing();
            //TestMaptileDrawing();
            //TestTagParsers();
            //TestSpanOnEntry("754866354	2	LINESTRING (-82.110422 40.975346, -82.1113778 40.9753544)	0.0009558369107748833	2028a47f-4119-4426-b40f-a8715d67f962");
            //TestSpanOnEntry("945909899	1	POINT (-84.1416403 39.7111214)	0.000125	5b9f9899-09dc-4b53-ba1a-5799fe6f992b");
            //TestConvertFromTsv();
            //TestSearchArea();
            //TupleVsRecords(); //looks like recordstructs are way faster
            BcryptSpeedCheck();


            //NOTE: EntityFramework cannot change provider after the first configuration/new() call. 
            //These cannot all be enabled in one run. You must comment/uncomment each one separately.
            //ALSO, these need updated somehow to be self-contained and consistent. Like, maybe load Delaware data or something as a baseline.
            //TestDBPerformance("SQLServer");
            //TestDBPerformance("MariaDB");
            //TestDBPerformance("PostgreSQL");

            //Sample code for later, I will want to make sure these indexes work as expected.
            //PraxisCore.MapTiles.ExpireSlippyMapTiles(Converters.GeoAreaToPolygon(OpenLocationCode.DecodeValid("86HWHHFF")));
            //TestMapTileIndexSpeed();
        }

        private static void TestCustomPbfReader()
        {
            //string filename = @"C:\praxis\delaware-latest.osm.pbf";
            //string filename = @"C:\praxis\ohio-latest.osm.pbf";
            string filename = @"D:\Projects\PraxisMapper Files\XmlToProcess\ohio-latest.osm.pbf";
            //string filename = @"D:\Projects\PraxisMapper Files\alternate source files\north-america-latest.osm.pbf"; //11GB files takes 1GB RAM and 6 minutes 6 seconds time
            //~4GB in 90 seconds would be ~5 minutes. world is 50GB, and I'd extrapolate that to ~90 minutes and 
            //PmPbfReader.PbfReader reader = new PmPbfReader.PbfReader();
            //reader.Open(filename);
            Stopwatch sw = new Stopwatch();
            //sw.Start();
            //reader.IndexFileParallel();
            //sw.Stop();
            Log.WriteLog(filename + " indexed parallel in " + sw.Elapsed);
            sw.Restart();
            //reader.IndexFile();
            //sw.Stop();
            //Log.WriteLog(filename + " indexed in " + sw.Elapsed);
            //sw.Restart();
            //reader.IndexFileBlocks();
            //sw.Stop();
            //Log.WriteLog(filename + " blocks-only indexed in " + sw.Elapsed);
            //sw.Restart();
            //reader.LoadWholeFile();
            //sw.Stop();
            //Log.WriteLog(filename + " loaded to RAM one-pass in " + sw.Elapsed);
            //sw.Restart();
            //reader.LoadWholeFileParallel();
            //sw.Stop();
            //var tempBlock = reader.GetBlock(1);
            //var smallnodes = reader.InflateNodes(tempBlock);
            //sw.Stop();
            //Log.WriteLog("Block 1 inflated to SmallNodes in " + sw.Elapsed);
            //sw.Restart();
            //Log.WriteLog(filename + " loaded to RAM one-pass parallel in " + sw.Elapsed);
            //reader.GetGeometryFromNextBlockSelfContained(); //This doesn't work, because blocks only hold 1 element type. I knew that but was hoping it wasnt true.
            //var data1 = reader.GetGeometryFromBlock(3239); //3241 for ohio, 253 for delaware
            //sw.Stop();
            //Log.WriteLog(filename + " loaded first (relation) block to RAM in " + sw.Elapsed);
            //sw.Restart();
            //PraxisCore.PbfFileParser.ProcessPMPBFResults(data1, "testFileName-block3239.json");
            ////458 of 7799 convert correctly. 
            //sw.Stop();
            //Log.WriteLog("wrote all results for relation block to JSON in " + sw.Elapsed);
            //sw.Restart();
            //var data2 = reader.GetGeometryFromBlock(3000); //3000 for ohio, 252 for delaware
            //sw.Stop();
            //Log.WriteLog(filename + " loaded next (way) block to RAM in " + sw.Elapsed);
            //sw.Restart();

            // var data3 = reader.GetGeometryFromBlock(2000); //3000 for ohio, 252 for delaware
            //sw.Stop();
            //Log.WriteLog(filename + " loaded node block to RAM in " + sw.Elapsed);
            // sw.Restart();

            //reference: run Larry -loadPbfsToJson and record time
            //then run this loop
            //Fun reminder: my tablet can't process Ohio via the normal Larry pbfToJson call. 
            //This takes ~4GB RAM and 3-6 minutes per relation block on north-america-latest. (it has 150 relation blocks, 15,132 Way blocks, 189k total)
            //~900 minutes = 15 hours
            var skipCount = 0;
            Log.WriteLog("starting data Load at " + DateTime.Now);
            //    for (var block = reader.BlockCount() - 1 - skipCount; block > 0; block--)
            //    {
            //        var x = reader.GetGeometryFromBlock(block);
            //        if (x != null) //Task this, so I can process the next block while writing these results.
            //            System.Threading.Tasks.Task.Run(() => 
            //            //PraxisCore.PbfFileParser.ProcessPMPBFResults(x, "testFile-ohio.json")
            //            //ProcessPMPBFResults(x, @"D:\Projects\PraxisMapper Files\Trimmed JSON Files\testFile-ohio.json")
            //            );
            //}
            sw.Stop();
            Log.WriteLog("All Ohio blocks processed in " + sw.Elapsed);
            Log.WriteLog("process test completed at " + DateTime.Now);

        }

        //ONly used for testing.
        public static CoordPair GetRandomCoordPair()
        {
            //Global scale testing.
            Random r = new Random();
            float lat = 90 * (float)r.NextDouble() * (r.Next() % 2 == 0 ? 1 : -1);
            float lon = 180 * (float)r.NextDouble() * (r.Next() % 2 == 0 ? 1 : -1);
            return new CoordPair(lat, lon);
        }

        //Only used for testing.
        public static CoordPair GetRandomBoundedCoordPair()
        {
            //randomize lat and long to roughly somewhere in Ohio. For testing a limited geographic area.
            //42, -80 NE
            //38, -84 SW
            //so 38 + (0-4), -84 = (0-4) coords.
            Random r = new Random();
            float lat = 38 + ((float)r.NextDouble() * 4);
            float lon = -84 + ((float)r.NextDouble() * 4);
            return new CoordPair(lat, lon);
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
                results.Add(GetRandomCoordPair());

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
            Log.WriteLog("PerformanceTracker EntityFrameworkCore NoChangeTracking total /average speed: " + NoCTInsertTime + " / " + (NoCTInsertTime / count) + "ms.");
            Log.WriteLog("PerformanceTracker Sproc total / average speed: " + SprocInsertTime + " / " + (SprocInsertTime / count) + "ms.");
        }

        public static void S2VsPlusCode()
        {
            //Testing how fast the conversion between coords and areas is here.
            int count = 10000;
            var testPointList = GetRandomCoords(count);
            System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();

            sw.Start();
            foreach (var coords in testPointList)
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

            Log.WriteLog("PlusCode conversion total / average time: " + PlusCodeConversion + " / " + (PlusCodeConversion / count) + " ms");
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
            var db = new PraxisCore.PraxisContext();
            var places = GetPlaces(OpenLocationCode.DecodeValid(plusCode6));  //All the places in this 6-code
            var box = OpenLocationCode.DecodeValid(plusCode6);
            sw.Stop();
            Log.WriteLog("Pulling " + places.Count + " places in 6-cell took " + sw.ElapsedMilliseconds + "ms");

            int[] splitChecks = new int[] { 1, 2, 4, 8, 10, 20, 25, 32, 40, 80, 100 };
            foreach (int splitcount in splitChecks)
            {
                sw.Restart();
                List<DbTables.Place>[] placeArray;
                GeoArea[] areaArray;
                StringBuilder[] sbArray = new StringBuilder[splitcount * splitcount];
                //Converters.SplitArea(box, splitcount, places, out placeArray, out areaArray);
                //System.Threading.Tasks.Parallel.For(0, placeArray.Length, (i) =>
                //{
                //    sbArray[i] = AreaTypeInfo.SearchArea(ref areaArray[i], ref placeArray[i]);
                //});
                sw.Stop();
                Log.WriteLog("dividing map by " + splitcount + " took " + sw.ElapsedMilliseconds + " ms");
            }
        }

        //public static void TestPlaceLookupPlans()
        //{
        //    //For determining which way of finding areas is faster.
        //    //Unfortunately, only intersects finds ways/points unless youre exactly standing on them.
        //    System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();

        //    List<long> intersectsPolygonRuntimes = new List<long>(50);
        //    List<long> containsPointRuntimes = new List<long>(50);
        //    List<long> AlgorithmRuntimes = new List<long>(50);
        //    List<long> precachedAlgorithmRuntimes = new List<long>(50);

        //    //tryint to determine the fastest way to search areas. Pull a 6-cell's worth of data from the DB, then parse it into 10cells.
        //    //Option 1: make a box, check Intersects.
        //    //Option 2: make a point, check Contains. (NOTE: a polygon does not Contain() its boundaries, so a point directly on a boundary line will not be identified)
        //    //Option 3: try NetTopologySuite.Algorithm.Locate.IndexedPointInAreaLocator ?
        //    //Option 4: consider using Contains against something like NetTopologySuite.Geometries.Prepared.PreparedGeometryFactory().Prepare(geom) instead of just Place? This might be outdated

        //    var factory = NtsGeometryServices.Instance.CreateGeometryFactory(srid: 4326); //SRID matches Plus code values. //share this here, so i compare the actual algorithms instead of this boilerplate, mandatory entry.
        //    var db = new PraxisContext();

        //    for (int i = 0; i < 50; i++)
        //    {

        //        var point = GetRandomBoundedCoordPair();
        //        var olc = OpenLocationCode.Encode(point.lat, point.lon);
        //        var codeString = olc.Substring(0, 6);
        //        sw.Restart();
        //        var box = OpenLocationCode.DecodeValid(codeString);
        //        var cord1 = new NetTopologySuite.Geometries.Coordinate(box.Min.Longitude, box.Min.Latitude);
        //        var cord2 = new NetTopologySuite.Geometries.Coordinate(box.Min.Longitude, box.Max.Latitude);
        //        var cord3 = new NetTopologySuite.Geometries.Coordinate(box.Max.Longitude, box.Max.Latitude);
        //        var cord4 = new NetTopologySuite.Geometries.Coordinate(box.Max.Longitude, box.Min.Latitude);
        //        var cordSeq = new NetTopologySuite.Geometries.Coordinate[5] { cord4, cord3, cord2, cord1, cord4 };
        //        var location = factory.CreatePolygon(cordSeq); //the 6 cell.

        //        var places = db.MapData.Where(md => md.place.Intersects(location)).ToList();
        //        double resolution10 = .000125; //as defined
        //        for (double x = box.Min.Longitude; x <= box.Max.Longitude; x += resolution10)
        //        {
        //            for (double y = box.Min.Latitude; y <= box.Max.Latitude; y += resolution10)
        //            {
        //                //also remember these coords start at the lower-left, so i can add the resolution to get the max bounds
        //                var olcInner = new OpenLocationCode(y, x); //This takes lat, long, Coordinate takes X, Y. This line is correct.
        //                var cordSeq2 = new NetTopologySuite.Geometries.Coordinate[5] { new NetTopologySuite.Geometries.Coordinate(x, y), new NetTopologySuite.Geometries.Coordinate(x + resolution10, y), new NetTopologySuite.Geometries.Coordinate(x + resolution10, y + resolution10), new NetTopologySuite.Geometries.Coordinate(x, y + resolution10), new NetTopologySuite.Geometries.Coordinate(x, y) };
        //                var poly2 = factory.CreatePolygon(cordSeq2);
        //                var entriesHere = places.Where(md => md.place.Intersects(poly2)).ToList();

        //            }
        //        }
        //        sw.Stop(); //measuring time it takes to parse a 6-cell down to 10-cells.and wou
        //        intersectsPolygonRuntimes.Add(sw.ElapsedMilliseconds);
        //    }

        //    for (int i = 0; i < 50; i++)
        //    {
        //        var point = GetRandomBoundedCoordPair();
        //        var olc = OpenLocationCode.Encode(point.lat, point.lon);
        //        var codeString = olc.Substring(0, 6);
        //        sw.Restart();
        //        var box = OpenLocationCode.DecodeValid(codeString);
        //        var cord1 = new NetTopologySuite.Geometries.Coordinate(box.Min.Longitude, box.Min.Latitude);
        //        var cord2 = new NetTopologySuite.Geometries.Coordinate(box.Min.Longitude, box.Max.Latitude);
        //        var cord3 = new NetTopologySuite.Geometries.Coordinate(box.Max.Longitude, box.Max.Latitude);
        //        var cord4 = new NetTopologySuite.Geometries.Coordinate(box.Max.Longitude, box.Min.Latitude);
        //        var cordSeq = new NetTopologySuite.Geometries.Coordinate[5] { cord4, cord3, cord2, cord1, cord4 };
        //        var location = factory.CreatePolygon(cordSeq); //the 6 cell.

        //        var places = db.MapData.Where(md => md.place.Intersects(location)).ToList();
        //        double resolution10 = .000125; //as defined
        //        for (double x = box.Min.Longitude; x <= box.Max.Longitude; x += resolution10)
        //        {
        //            for (double y = box.Min.Latitude; y <= box.Max.Latitude; y += resolution10)
        //            {
        //                //Option 2, is Contains on a point faster?
        //                var location2 = factory.CreatePoint(new NetTopologySuite.Geometries.Coordinate(x, y));
        //                var places2 = places.Where(md => md.place.Contains(location)).ToList();

        //            }
        //        }
        //        sw.Stop(); //measuring time it takes to parse a 6-cell down to 10-cells.and wou
        //        containsPointRuntimes.Add(sw.ElapsedMilliseconds);
        //    }

        //    for (int i = 0; i < 50; i++)
        //    {
        //        var point = GetRandomBoundedCoordPair();
        //        var olc = OpenLocationCode.Encode(point.lat, point.lon);
        //        var codeString = olc.Substring(0, 6);
        //        sw.Restart();
        //        var box = OpenLocationCode.DecodeValid(codeString);
        //        var cord1 = new NetTopologySuite.Geometries.Coordinate(box.Min.Longitude, box.Min.Latitude);
        //        var cord2 = new NetTopologySuite.Geometries.Coordinate(box.Min.Longitude, box.Max.Latitude);
        //        var cord3 = new NetTopologySuite.Geometries.Coordinate(box.Max.Longitude, box.Max.Latitude);
        //        var cord4 = new NetTopologySuite.Geometries.Coordinate(box.Max.Longitude, box.Min.Latitude);
        //        var cordSeq = new NetTopologySuite.Geometries.Coordinate[5] { cord4, cord3, cord2, cord1, cord4 };
        //        var location = factory.CreatePolygon(cordSeq); //the 6 cell.

        //        //var places = db.MapData.Where(md => md.place.Intersects(location)).ToList();
        //        var indexedIn = db.MapData.Where(md => md.place.Contains(location)).Select(md => new NetTopologySuite.Algorithm.Locate.IndexedPointInAreaLocator(md.place)).ToList();
        //        var fakeCoord = new NetTopologySuite.Geometries.Coordinate(point.lon, point.lat);
        //        foreach (var ii in indexedIn)
        //            ii.Locate(fakeCoord); //force index creation on all items now instead of later.

        //        double resolution10 = .000125; //as defined
        //        for (double x = box.Min.Longitude; x <= box.Max.Longitude; x += resolution10)
        //        {
        //            for (double y = box.Min.Latitude; y <= box.Max.Latitude; y += resolution10)
        //            {
        //                //Option 2, is Contains on a point faster?
        //                var location2 = new NetTopologySuite.Geometries.Coordinate(x, y);
        //                var places3 = indexedIn.Where(i => i.Locate(location2) == NetTopologySuite.Geometries.Location.Interior);
        //            }
        //        }
        //        sw.Stop(); //measuring time it takes to parse a 6-cell down to 10-cells.and wou
        //        precachedAlgorithmRuntimes.Add(sw.ElapsedMilliseconds);
        //    }

        //    //these commented numbers are out of date.
        //    //var a = AlgorithmRuntimes.Average();
        //    var b = intersectsPolygonRuntimes.Average();
        //    var c = containsPointRuntimes.Average();
        //    var d = precachedAlgorithmRuntimes.Average();

        //    Log.WriteLog("Intersect test average result is " + b + "ms");
        //    Log.WriteLog("Contains Point test average result is " + c + "ms");
        //    Log.WriteLog("Precached point test average result is " + d + "ms");


        //    return;
        //}

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
                //var eightCodePlaces = GetPlacesNoTrack(area8);
                sw.Stop();
                var eightCodeTime = sw.ElapsedMilliseconds;
                //avg8 += eightCodeTime;

                sw.Restart();
                GeoArea area6 = OpenLocationCode.DecodeValid(cell6);
                var sixCodePlaces = GetPlaces(area6);
                sw.Stop();
                var sixCodeTime = sw.ElapsedMilliseconds;
                avg6 += sixCodeTime;

                sw.Restart();
                GeoArea area4 = OpenLocationCode.DecodeValid(cell4);
                var fourCodePlaces = GetPlaces(area4);
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
            Log.WriteLog("6-code search time would be " + (avg8 * 400 / loopCount) + " linearly, is actually " + avg6 + " (" + ((avg8 * 400 / loopCount) / avg6) + "x faster)");
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
                //var sixCodePlacesNT = GetPlacesNoTrack(area6);
                sw.Stop();
                //var sixCodeNTTime = sw.ElapsedMilliseconds;
                sw.Restart();
                //var sixCodePlacesPrecomp = GetPlacesPrecompiled(area6);
                sw.Stop();
                var sixCodePrecompTime = sw.ElapsedMilliseconds;
                //Log.WriteLog("6code- Tracking: " + sixCodeTime + "ms VS NoTracking: " + sixCodeNTTime + "ms VS Precompiled: " + sixCodePrecompTime + "ms");


                sw.Restart();
                GeoArea area4 = OpenLocationCode.DecodeValid(cell4);
                var fourCodePlaces = GetPlacesBase(area4);
                sw.Stop();
                var fourCodeTime = sw.ElapsedMilliseconds;
                sw.Restart();
                //var fourCodePlacesNT = GetPlacesNoTrack(area4);
                sw.Stop();
                var fourCodeNTTime = sw.ElapsedMilliseconds;
                sw.Restart();
                //var fourCodePlacesPrecomp = GetPlacesPrecompiled(area4);
                sw.Stop();
                var fourCodePrecompTime = sw.ElapsedMilliseconds;
                Log.WriteLog("4code- Tracking: " + fourCodeTime + "ms VS NoTracking: " + fourCodeNTTime + "ms VS Precompiled: " + fourCodePrecompTime + "ms");
            }
        }

        public static List<DbTables.Place> GetPlacesBase(GeoArea area, List<DbTables.Place> source = null)
        {
            var location = Converters.GeoAreaToPolygon(area);
            List<DbTables.Place> places;
            if (source == null)
            {
                var db = new PraxisCore.PraxisContext();
                places = db.Places.Where(md => md.ElementGeometry.Intersects(location)).ToList();
            }
            else
                places = source.Where(md => md.ElementGeometry.Intersects(location)).ToList();
            return places;
        }

        //This was only used in TestPerf, and isn't good enough to use.
        //public static List<MapData> GetPlacesPrecompiled(GeoArea area, List<MapData> source = null)
        //{
        //    var coordSeq = Converters.GeoAreaToCoordArray(area);
        //    var location = factory.CreatePolygon(coordSeq);
        //    List<MapData> places;
        //    if (source == null)
        //    {
        //        var db = new PraxisCore.PraxisContext();
        //        places = db.getPlaces((location)).ToList();
        //    }
        //    else
        //        places = source.Where(md => md.place.Intersects(location)).ToList();
        //    return places;
        //}

        //Another TestPerf only functoin.
        //public static List<MapData> GetPlacesNoTrack(GeoArea area, List<MapData> source = null)
        //{
        //    //The flexible core of the lookup functions. Takes an area, returns results that intersect from Source. If source is null, looks into the DB.
        //    //Intersects is the only indexable function on a geography column I would want here. Distance and Equals can also use the index, but I don't need those in this app.
        //    var coordSeq = Converters.GeoAreaToCoordArray(area);
        //    var location = factory.CreatePolygon(coordSeq);
        //    List<MapData> places;
        //    if (source == null)
        //    {
        //        var db = new PraxisCore.PraxisContext();
        //        db.ChangeTracker.AutoDetectChangesEnabled = false;
        //        places = db.MapData.Where(md => md.place.Intersects(location)).ToList();
        //    }
        //    else
        //        places = source.Where(md => md.place.Intersects(location)).ToList();
        //    return places;
        //}

        //public static void TestMapDataAbbrev()
        //{
        //    System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
        //    var db = new PraxisCore.PraxisContext();

        //    for (int i = 0; i < 5; i++)
        //    {
        //        sw.Restart();
        //        var places2 = db.MapData.Take(10000).ToList();
        //        sw.Stop();
        //        var placesTime = sw.ElapsedMilliseconds;
        //        sw.Restart();
        //        var places3 = db.MapData.Take(10000).Select(m => new MapDataAbbreviated(m.name, m.type, m.place)).ToList();
        //        sw.Stop();
        //        var abbrevTime = sw.ElapsedMilliseconds;

        //        Log.WriteLog("Full data time took " + placesTime + "ms");
        //        Log.WriteLog("short data time took " + abbrevTime + "ms");
        //    }
        //}

        public static void TestPrecompiledQuery()
        {
            System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
            var db = new PraxisCore.PraxisContext();


            for (int i = 0; i < 5; i++)
            {
                sw.Restart();
                var places1 = GetPlacesBase(OpenLocationCode.DecodeValid(cell6));
                sw.Stop();
                var placesTime = sw.ElapsedMilliseconds;
                sw.Restart();
                //var places2 = GetPlacesPrecompiled(OpenLocationCode.DecodeValid(cell6));
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
            List<DbTables.Place> contents2 = new List<DbTables.Place>();
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
                    TagParser.GetAreaType(p.Tags) != "")
                .Select(p => (OsmSharp.Relation)p)
                .ToList();
            else if (areaType == "admin")
                filteredEntries = progress.Where(p => p.Type == OsmGeoType.Relation &&
                    TagParser.GetAreaType(p.Tags).StartsWith(areaType))
                .Select(p => (OsmSharp.Relation)p)
                .ToList();
            else
                filteredEntries = progress.Where(p => p.Type == OsmGeoType.Relation &&
                TagParser.GetAreaType(p.Tags) == areaType
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
            //MapDataForJson test1 = new MapDataForJson("TestPlace", testPlaceWKT, "Way", 12345, null, null, 1, 1);
            string tempFile = System.IO.Path.GetTempFileName();
            sw.Start();
            //WriteMapDataToFile(tempFile, ref l);
            //var test2 = JsonSerializer.Serialize(test1, typeof(MapDataForJson));
            sw.Stop();
            Log.WriteLog("Single MapData to Json took " + sw.ElapsedTicks + " ticks (" + sw.ElapsedMilliseconds + "ms)");
            sw.Restart();
            //MapDataForJson j = (MapDataForJson)JsonSerializer.Deserialize(test2, typeof(MapDataForJson));
            sw.Stop();
            Log.WriteLog("Single Json to MapData took " + sw.ElapsedTicks + " ticks (" + sw.ElapsedMilliseconds + "ms)");
            File.Delete(tempFile); //Clean up after ourselves.
            sw.Restart();
            var test3 = reader.Read(testPlaceWKT);
            sw.Stop();
            Log.WriteLog("Converting 1 polygon from Text to Geometry took " + sw.ElapsedTicks + " ticks (" + sw.ElapsedMilliseconds + "ms)");
            sw.Restart();
            var result3 = CCWCheck((Polygon)test3);
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
            List<DbTables.Place> contents2 = new List<DbTables.Place>();
            contents2.Capacity = 100000;

            var source2 = new PBFOsmStreamSource(ms);
            var progress2 = source2.ShowProgress();

            //List<OsmSharp.Relation> filteredEntries2;
            var normalListTest = progress2
                .Where(p => p.Type == OsmGeoType.Relation)
                .Select(p => (OsmSharp.Relation)p)
            .ToList();

            var concurrentTest = new ConcurrentBag<OsmSharp.Relation>(normalListTest);
            Log.WriteLog("Both data sources populated. Starting test.");

            Stopwatch sw = new Stopwatch();
            sw.Start();
            var data1 = normalListTest.AsParallel().Select(r => r.Members.Length).ToList();
            sw.Stop();
            Log.WriteLog("Standard list took " + sw.ElapsedTicks + " ticks (" + sw.ElapsedMilliseconds + "ms)");
            sw.Restart();
            var data2 = concurrentTest.AsParallel().Select(r => r.Members.Length).ToList();
            sw.Stop();
            Log.WriteLog("ConcurrentBag took " + sw.ElapsedTicks + " ticks (" + sw.ElapsedMilliseconds + "ms)");

            //lets do lookup VS dictionary vs concurrentdictionary. This eats a lot more RAM with nodes.
            var list = progress2.Where(p => p.Type == OsmGeoType.Way).Select(p => (OsmSharp.Way)p).ToList();
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
            var randomCap = db.Places.Count();
            Random r = new Random();
            string website = "http://localhost/GPSExploreServerAPI/MapData/CalculateMapDataScore/";
            for (int i = 0; i < 100; i++)
            {
                WebClient wc = new WebClient();
                wc.DownloadString(website + r.Next(1, randomCap));
            }
        }

        //public static void TestRecordVsStringBuilders()
        //{
        //    List<MapData> mapData = new List<MapData>();
        //    StringBuilder sb = new StringBuilder();
        //    GeoArea area = new GeoArea(1, 2, 3, 4);

        //    var xCells = area.LongitudeWidth / resolutionCell10;
        //    var yCells = area.LatitudeHeight / resolutionCell10;

        //    Stopwatch sw = new Stopwatch();
        //    sw.Start();
        //    for (double xx = 0; xx < xCells; xx += 1)
        //    {
        //        for (double yy = 0; yy < yCells; yy += 1)
        //        {
        //            double x = area.Min.Longitude + (resolutionCell10 * xx);
        //            double y = area.Min.Latitude + (resolutionCell10 * yy);

        //            var placesFound = AreaTypeInfo.FindPlacesInCell10(x, y, ref mapData, true);
        //            if (!string.IsNullOrWhiteSpace(placesFound))
        //                sb.AppendLine(placesFound);
        //        }
        //    }
        //    sw.Stop();
        //    Log.WriteLog("Searched and built String response in " + sw.ElapsedMilliseconds);

        //    //now test again with new function
        //    List<Cell10Info> info = new List<Cell10Info>();
        //    sw.Restart();
        //    for (double xx = 0; xx < xCells; xx += 1)
        //    {
        //        for (double yy = 0; yy < yCells; yy += 1)
        //        {
        //            double x = area.Min.Longitude + (resolutionCell10 * xx);
        //            double y = area.Min.Latitude + (resolutionCell10 * yy);

        //            var placesFound = CellInfoFindPlacesInCell10(x, y, ref mapData);
        //            if (placesFound != null)
        //                info.Add(placesFound);
        //        }
        //    }
        //    sw.Stop();
        //    Log.WriteLog("Searched and built Cell10Info Record response in " + sw.ElapsedMilliseconds);

        //    StringBuilder sb2 = new StringBuilder();
        //    sw.Restart();
        //    foreach (var place in info.Select(i => i.placeName).Distinct())
        //    {
        //        var codes = string.Join(",", info.Where(i => i.placeName == place).Select(i => i.Cell10));
        //        sb.Append(place + "|" + codes + Environment.NewLine);
        //    }
        //    sw.Stop();
        //    Log.WriteLog("converted record list to string output in " + sw.ElapsedMilliseconds);
        //}

        public static void TestDBPerformance(string mode)
        {
            if (mode == "SQLServer")
            {
                Log.WriteLog("Starting SqlServer performance test.");
                PraxisContext.connectionString = "Data Source=localhost\\SQLDEV;UID=GpsExploreService;PWD=lamepassword;Initial Catalog=Praxis;";
                PraxisContext.serverMode = "SQLServer";
            }
            else if (mode == "MariaDB")
            {
                Log.WriteLog("Starting MariaDb performance test.");
                PraxisContext.connectionString = "server=localhost;database=praxis;user=root;password=asdf;";
                PraxisContext.serverMode = "MariaDB";
            }
            else if (mode == "PostgreSQL")
            {
                Log.WriteLog("Starting PostgreSQL performance test.");
                PraxisContext.connectionString = "server=localhost;database=praxis;user=root;password=asdf;";
                PraxisContext.serverMode = "PostgreSQL";
            }
            MakePraxisDB(PraxisContext.serverMode);

            Random r = new Random();
            Stopwatch sw = new Stopwatch();
            PraxisContext dbPG = new PraxisContext();
            Log.WriteLog("Loading Delaware data for consistency.");
            LoadBaselineData(dbPG, @"D:\Projects\PraxisMapper Files\XmlToProcess\delaware-latest.osm.pbf"); //~17MB PBF, shouldn't be serious stress on anything.


            int maxRandom = dbPG.Places.Count();
            sw.Restart();
            for (var i = 0; i < 10000; i++)
            {
                //read 1000 random entries;
                int entry = r.Next(1, maxRandom);
                var tempEntry = dbPG.Places.Where(m => m.Id == entry).FirstOrDefault();
            }
            sw.Stop();
            Log.WriteLog("10,000 random reads done in " + sw.ElapsedMilliseconds + "ms");

            //load all Delaware elements back into memory.
            GeoArea delaware = new GeoArea(38, -77, 41, -74);
            var poly = Converters.GeoAreaToPolygon(delaware);
            sw.Restart();
            var allEntires = dbPG.Places.Where(w => w.ElementGeometry.Intersects(poly)).ToList();
            sw.Stop();
            Log.WriteLog("Loaded all Delaware items in " + sw.ElapsedMilliseconds + "ms");

            sw.Start();
            for (var i = 0; i < 10000; i++)
            {
                //write 1000 random entries;
                var entry = CreateInterestingPlaces("22334455", false);
                dbPG.Places.AddRange(entry);
            }
            dbPG.SaveChanges();
            sw.Stop();
            Log.WriteLog("10,000 random writes done in " + sw.ElapsedMilliseconds + "ms");
        }

        public static void LoadBaselineData(PraxisContext db, string filename)
        {
            var fs = System.IO.File.OpenRead(filename);
            var source = new PBFOsmStreamSource(fs);

            var relations = source.AsParallel()
            .ToComplete()
            .Where(p => p.Type == OsmGeoType.Relation);
            foreach (var r in relations)
            {
                var convertedRelation = ConvertOsmEntryToStoredWay(r);
                if (convertedRelation == null)
                {
                    continue;
                }

                //locker.EnterWriteLock();
                convertedRelation.ElementGeometry = SimplifyPlace(convertedRelation.ElementGeometry);
                if (convertedRelation.ElementGeometry == null)
                    continue;
                db.Places.Add(convertedRelation);
                //db.SaveChanges();
            }

            var ways = source.AsParallel()
            .ToComplete()
            .Where(p => p.Type == OsmGeoType.Way);
            foreach (var w in ways)
            {
                var convertedWay = ConvertOsmEntryToStoredWay(w);
                if (convertedWay == null)
                {
                    continue;
                }

                //locker.EnterWriteLock();
                convertedWay.ElementGeometry = SimplifyPlace(convertedWay.ElementGeometry);
                if (convertedWay.ElementGeometry == null)
                    continue;
                db.Places.Add(convertedWay);
                //db.SaveChanges();
            }

            Stopwatch sw = new Stopwatch();
            sw.Start();
            db.SaveChanges();
            sw.Stop();
            Log.WriteLog("Saved baseline data to DB in " + sw.Elapsed);
        }

        public static DbTables.Place ConvertOsmEntryToStoredWay(OsmSharp.Complete.ICompleteOsmGeo g)
        {
            try
            {
                var feature = OsmSharp.Geo.FeatureInterpreter.DefaultInterpreter.Interpret(g);
                if (feature.Count != 1)
                {
                    Log.WriteLog("Error: " + g.Type.ToString() + " " + g.Id + " didn't return expected number of features (" + feature.Count + ")", Log.VerbosityLevels.High);
                    return null;
                }
                var sw = new DbTables.Place();
                //sw.name = TagParser.GetPlaceName(g.Tags);
                sw.SourceItemID = g.Id;
                sw.SourceItemType = (g.Type == OsmGeoType.Relation ? 3 : g.Type == OsmGeoType.Way ? 2 : 1);
                var geo = SimplifyPlace(feature.First().Geometry);
                if (geo == null)
                    return null;
                geo.SRID = 4326;//Required for SQL Server to accept data this way.
                sw.ElementGeometry = geo;
                sw.Tags = TagParser.getFilteredTags(g.Tags);
                if (sw.ElementGeometry.GeometryType == "LinearRing" || (sw.ElementGeometry.GeometryType == "LineString" && sw.ElementGeometry.Coordinates.First() == sw.ElementGeometry.Coordinates.Last()))
                {
                    //I want to update all LinearRings to Polygons, and let the style determine if they're Filled or Stroked.
                    var poly = factory.CreatePolygon((LinearRing)sw.ElementGeometry);
                    sw.ElementGeometry = poly;
                }
                return sw;
            }
            catch
            {
                return null;
            }
        }

        //testing if this is better/more efficient (on the phone side) than passing strings along. Only used in TestPerf.
        //public static Cell10Info CellInfoFindPlacesInCell10(double x, double y, ref List<StoredOsmElement> places)
        //{
        //    var box = new GeoArea(new GeoPoint(y, x), new GeoPoint(y + resolutionCell10, x + resolutionCell10));
        //    var entriesHere = GetPlaces(box, places).ToList(); 

        //    if (entriesHere.Count() == 0)
        //        return null;

        //    //string area = DetermineAreaPoint(entriesHere);
        //    var area = AreaTypeInfo.PickSmallestEntry(entriesHere);
        //    if (area != null)
        //    {
        //        string olc;
        //        //if (entireCode)
        //        olc = new OpenLocationCode(y, x).CodeDigits;
        //        //else
        //        //olc = new OpenLocationCode(y, x).CodeDigits.Substring(6, 4); //This takes lat, long, Coordinate takes X, Y. This line is correct.
        //        // olc = new OpenLocationCode(y, x).CodeDigits.Substring(8, 2); //This takes lat, long, Coordinate takes X, Y. This line is correct.
        //        return new Cell10Info(area.name, olc, area.sourceItemType); //TODO: set this up later to hold gameplay area type.
        //    }
        //    return null;
        //}

        public static void TestIntersectsPreparedVsNot()
        {
            Log.WriteLog("Loading data for Intersect performance test.");
            var pgf = new PreparedGeometryFactory();
            //Compare intersects speed (as the app will do them): one Area from a Cell8 against a list of MapData places. 
            //Switch up which ones are prepared, which ones aren't and test with none prepared.
            GeoArea Cell6 = OpenLocationCode.DecodeValid("86HW");
            var places = GetPlaces(Cell6);

            Log.WriteLog("Cell6 Data loaded.");
            GeoArea Cell8 = OpenLocationCode.DecodeValid("86HWG855");


            System.Diagnostics.Stopwatch sw = new Stopwatch();
            sw.Start();
            var placesNormal = PraxisCore.Place.GetPlaces(Cell8, places);
            sw.Stop();
            Log.WriteLog("Normal geometries search took " + sw.ElapsedTicks + " ticks (" + sw.ElapsedMilliseconds + "ms)");
            sw.Restart();

            var preppedPlace = pgf.Create(Converters.GeoAreaToPolygon(Cell8));
            //var placesPreppedCell = places.Where(md => preppedPlace.Intersects(md.place)).ToList();
            sw.Stop();
            Log.WriteLog("Prepped Cell8 & Normal geometries search took " + sw.ElapsedTicks + " ticks (" + sw.ElapsedMilliseconds + "ms)");
            sw.Restart();

            //var preppedPlaces = places.Select(p => pgf.Create(p.place)).ToList();
            var prepTime = sw.ElapsedTicks;
            var locationNormal = Converters.GeoAreaToPolygon(Cell8);
            //var placesPreppedList = preppedPlaces.Where(p => p.Intersects(locationNormal)).ToList();
            sw.Stop();

            Log.WriteLog("Prepped List & Normal Cell8 search took " + sw.ElapsedTicks + " ticks (" + sw.ElapsedMilliseconds + "ms), " + prepTime + " ticks were prepping list");

        }


        //public static void TestRasterVsVectorCell8()
        //{
        //    Log.WriteLog("Loading data for Raster Vs Vector performance test. 400 cell8 test.");
        //    string testCell = "86HWHH";
        //    Stopwatch raster = new Stopwatch();
        //    Stopwatch vector = new Stopwatch();
        //    for (int pos1 = 0; pos1 < 20; pos1++)
        //        //System.Threading.Tasks.Parallel.For(0, 20, (pos2) =>
        //        for (int pos2 = 0; pos2 < 20; pos2++)
        //        {
        //            string cellToCheck = testCell + OpenLocationCode.CodeAlphabet[pos1].ToString() + OpenLocationCode.CodeAlphabet[pos2].ToString();
        //            var area = new OpenLocationCode(cellToCheck).Decode();
        //            var places = GetPlacesMapDAta(area, null, false, false);

        //            raster.Start();
        //            //MapTiles.DrawAreaMapTileRaster(ref places, area, 11); 
        //            raster.Stop();

        //            vector.Start();
        //            //MapTiles.DrawAreaMapTile(ref places, area, 11);
        //            vector.Stop();
        //        }

        //    Log.WriteLog("Raster performance:" + raster.ElapsedMilliseconds + "ms");
        //    Log.WriteLog("Vector performance:" + vector.ElapsedMilliseconds + "ms");
        //}

        //public static void TestRasterVsVectorCell10()
        //{
        //    Log.WriteLog("Loading data for Raster Vs Vector performance test. 400 cell10 test.");
        //    string testCell = "86HWHHQ6";
        //    Stopwatch raster = new Stopwatch();
        //    Stopwatch vector = new Stopwatch();
        //    for (int pos1 = 0; pos1 < 20; pos1++)
        //        //System.Threading.Tasks.Parallel.For(0, 20, (pos2) =>
        //        for (int pos2 = 0; pos2 < 20; pos2++)
        //        {
        //            string cellToCheck = testCell + OpenLocationCode.CodeAlphabet[pos1].ToString() + OpenLocationCode.CodeAlphabet[pos2].ToString();
        //            var area = new OpenLocationCode(cellToCheck).Decode();
        //            var places = GetPlacesMapDAta(area, null, false, false);

        //            raster.Start();
        //            //MapTiles.DrawAreaMapTileRaster(ref places, area, 11);
        //            raster.Stop();

        //            vector.Start();
        //            //MapTiles.DrawAreaMapTile(ref places, area, 11);
        //            vector.Stop();
        //        }

        //    Log.WriteLog("Raster performance:" + raster.ElapsedMilliseconds + "ms");
        //    Log.WriteLog("Vector performance:" + vector.ElapsedMilliseconds + "ms");
        //}

        //public static void TestImageSharpVsSkiaSharp()
        //{
        //    //params : 1119/1527/12/1
        //    //params: 8957 / 12224 / 15 / 1
        //    Log.WriteLog("Loading data for ImageSharp vs SkiaSharp performance test");
        //    //var x = 8957;
        //    //var y = 12224;
        //    //var zoom = 15;
        //    //var layer = 1;

        //    var x = 1119;
        //    var y = 1527;
        //    var zoom = 12;
        //    var layer = 1;

        //    var n = Math.Pow(2, zoom);

        //    var lon_degree_w = x / n * 360 - 180;
        //    var lon_degree_e = (x + 1) / n * 360 - 180;
        //    var lat_rads_n = Math.Atan(Math.Sinh(Math.PI * (1 - 2 * y / n)));
        //    var lat_degree_n = lat_rads_n * 180 / Math.PI;
        //    var lat_rads_s = Math.Atan(Math.Sinh(Math.PI * (1 - 2 * (y + 1) / n)));
        //    var lat_degree_s = lat_rads_s * 180 / Math.PI;

        //    var relevantArea = new GeoArea(lat_degree_s, lon_degree_w, lat_degree_n, lon_degree_e);
        //    var areaHeightDegrees = lat_degree_n - lat_degree_s;
        //    var areaWidthDegrees = 360 / n;


        //    var places = GetPlacesMapDAta(relevantArea, includeGenerated: false);
        //    var places2 = places.Select(p => p.Clone()).ToList(); //Trimming occurs in the draw functions, so we need a copy to make the test fair.
        //    Stopwatch sw = new Stopwatch();
        //    sw.Start();
        //    //var results1 = MapTiles.DrawAreaMapTileSlippy(ref places, relevantArea, areaHeightDegrees, areaWidthDegrees);
        //    sw.Stop();
        //    //System.IO.File.WriteAllBytes("ImageSharp-" + x + "_" + y + "_" + "_" + zoom + ".png", results1);
        //    Log.WriteLog("ImageSharp performance:" + sw.ElapsedMilliseconds + "ms");
        //    sw.Restart();
        //    var results2 = MapTiles.DrawAreaMapTileSlippySkia(ref places2, relevantArea, areaHeightDegrees, areaWidthDegrees);
        //    sw.Stop();
        //    System.IO.File.WriteAllBytes("SkiaSharp-" + x + "_" + y + "_" + "_" + zoom + ".png", results2);
        //    Log.WriteLog("SkiaSharp performance:" + sw.ElapsedMilliseconds + "ms"); //This is 3x as fast at zoom 12 and zoom 15.


        //    //Log.WriteLog("Raster performance:" + raster.ElapsedMilliseconds + "ms");
        //    //Log.WriteLog("Vector performance:" + vector.ElapsedMilliseconds + "ms");
        //}

        public static void TestMapTileIndexSpeed()
        {
            //This looks like MariaDB and SQL Server are pretty close on performance in this case. Generating map tiles seems to favor SQL Server a lot though.
            //Trying to see whats the fastest way to update some map tile info.
            var db = new PraxisContext();
            var tilecount = db.SlippyMapTiles.Count();
            Log.WriteLog("Current maptile count: " + tilecount);

            Stopwatch sw = new Stopwatch();
            sw.Start();
            var allTiles = db.SlippyMapTiles.ToList();
            sw.Stop();
            Log.WriteLog("All tiles loaded from DB in " + sw.Elapsed);
            var randomTileArea = allTiles.OrderBy(t => Guid.NewGuid()).First().AreaCovered; //To use for checking the index.
            sw.Restart();
            var allTiles2 = db.SlippyMapTiles.ToList();
            Log.WriteLog("All Tiles pulled again while in RAM in: " + sw.Elapsed);
            sw.Restart();
            var oneTile = db.SlippyMapTiles.First();
            sw.Stop();
            Log.WriteLog("Loaded 1 map tile in: " + sw.Elapsed);
            sw.Restart();
            var indexedTile = db.SlippyMapTiles.Where(s => s.AreaCovered.Intersects(randomTileArea)).ToList();
            sw.Stop();
            Log.WriteLog("loaded 1 random tile via index in : " + sw.Elapsed);
            sw.Restart();
            //MapTiles.ExpireSlippyMapTiles(randomTileArea, "mapTiles");
            sw.Stop();
            Log.WriteLog("Expired 1 random map tile in:" + sw.Elapsed);


        }


        public static void MakePraxisDB(string mode)
        {
            PraxisContext db = new PraxisContext();
            db.Database.EnsureCreated(); //all the automatic stuff EF does for us

            //Not automatic entries executed below:
            //PostgreSQL will make automatic spatial indexes
            if (mode == "PostgreSQL")
            {
                //db.Database.ExecuteSqlRaw(PraxisContext.MapDataIndexPG); //PostgreSQL needs its own create-index syntax
                //db.Database.ExecuteSqlRaw(PraxisContext.GeneratedMapDataIndexPG);
                db.Database.ExecuteSqlRaw(PraxisContext.MapTileIndexPG);
                db.Database.ExecuteSqlRaw(PraxisContext.SlippyMapTileIndexPG);
                db.Database.ExecuteSqlRaw(PraxisContext.StoredElementsIndexPG);

            }
            else
            {
                //db.Database.ExecuteSqlRaw(PraxisContext.MapDataIndex); //PostgreSQL needs its own create-index syntax
                //db.Database.ExecuteSqlRaw(PraxisContext.GeneratedMapDataIndex);
                db.Database.ExecuteSqlRaw(PraxisContext.MapTileIndex);
                db.Database.ExecuteSqlRaw(PraxisContext.SlippyMapTileIndex);
                db.Database.ExecuteSqlRaw(PraxisContext.StoredElementsIndex);
                db.Database.ExecuteSqlRaw(PraxisContext.customDataPlusCodesIndex);
            }

            if (mode == "SQLServer")
            {
                db.Database.ExecuteSqlRaw(PraxisContext.MapDataValidTriggerMSSQL);
                db.Database.ExecuteSqlRaw(PraxisContext.GeneratedMapDataValidTriggerMSSQL);
            }
            if (mode == "MariaDB")
            {
                db.Database.ExecuteSqlRaw("SET collation_server = 'utf8mb4_unicode_ci'; SET character_set_server = 'utf8mb4'"); //MariaDB defaults to latin2_swedish, we need Unicode.
            }

            InsertDefaultServerConfig();
            InsertDefaultStyle(mode);
        }

        //public static void InsertAreaTypesToDb(string mode)
        //{
        //    var db = new PraxisContext();
        //    if (mode == "SQLServer")
        //    {
        //        db.Database.BeginTransaction();
        //        db.Database.ExecuteSqlRaw("SET IDENTITY_INSERT AreaTypes ON;");
        //    }
        //    db.AreaTypes.AddRange(areaTypes);
        //    db.SaveChanges();
        //    if (mode == "SQLServer")
        //    {
        //        db.Database.ExecuteSqlRaw("SET IDENTITY_INSERT dbo.AreaTypes OFF;");
        //        db.Database.CommitTransaction();
        //    }
        //}

        public static void InsertDefaultServerConfig()
        {
            var db = new PraxisContext();
            db.ServerSettings.Add(new ServerSetting() { NorthBound = 90, SouthBound = -90, EastBound = 180, WestBound = -180 });
            db.SaveChanges();
        }

        public static void InsertDefaultStyle(string mode)
        {
            var db = new PraxisContext();
            //Remove any existing entries, in case I'm refreshing the rules on an existing entry.
            if (mode != "PostgreSQL") //PostgreSQL has stricter requirements on its syntax.
            {
                //db.Database.ExecuteSqlRaw("DELETE FROM TagParserEntriesTagParserMatchRules");
                //db.Database.ExecuteSqlRaw("DELETE FROM TagParserEntries");
                //db.Database.ExecuteSqlRaw("DELETE FROM TagParserMatchRules");
            }

            if (mode == "SQLServer")
            {
                db.Database.BeginTransaction();
                db.Database.ExecuteSqlRaw("SET IDENTITY_INSERT TagParserEntries ON;");
            }
            db.StyleEntries.AddRange(Singletons.defaultStyleEntries);
            db.SaveChanges();
            if (mode == "SQLServer")
            {
                db.Database.ExecuteSqlRaw("SET IDENTITY_INSERT TagParserEntries OFF;");
                db.Database.CommitTransaction();
            }
        }

        //public static void InsertDefaultPaintTownConfigs()
        //{
        //    var db = new PraxisContext();
        //    //we set the reset time to next Saturday at midnight for a default.
        //    var nextSaturday = DateTime.Now.AddDays(6 - (int)DateTime.Now.DayOfWeek);
        //    nextSaturday.AddHours(-nextSaturday.Hour);
        //    nextSaturday.AddMinutes(-nextSaturday.Minute);
        //    nextSaturday.AddSeconds(-nextSaturday.Second);

        //    var tomorrow = new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day).AddDays(1);
        //    db.PaintTownConfigs.Add(new PaintTownConfig() { Name = "All-Time", Cell10LockoutTimer = 300, DurationHours = -1, NextReset = nextSaturday });
        //    db.PaintTownConfigs.Add(new PaintTownConfig() { Name = "Weekly", Cell10LockoutTimer = 300, DurationHours = 168, NextReset = new DateTime(2099, 12, 31) });
        //    //db.PaintTownConfigs.Add(new PaintTownConfig() { Name = "Daily", Cell10LockoutTimer = 30, DurationHours = 24, NextReset = tomorrow });

        //    //PaintTheTown requires dummy entries in the playerData table, or it doesn't know which factions exist. It's faster to do this once here than to check on every call to playerData
        //    foreach (var faction in Singletons.defaultFaction)
        //        GenericData.SetPlayerData("dummy", "FactionId", faction.FactionId.ToString());
        //    db.SaveChanges();
        //}

        public static void CleanDb()
        {
            Log.WriteLog("Cleaning DB at " + DateTime.Now);
            PraxisContext osm = new PraxisContext();
            osm.Database.SetCommandTimeout(900);

            osm.Database.ExecuteSqlRaw("TRUNCATE TABLE MapData");
            Log.WriteLog("MapData cleaned at " + DateTime.Now, Log.VerbosityLevels.High);
            osm.Database.ExecuteSqlRaw("TRUNCATE TABLE MapTiles");
            Log.WriteLog("MapTiles cleaned at " + DateTime.Now, Log.VerbosityLevels.High);
            osm.Database.ExecuteSqlRaw("TRUNCATE TABLE PerformanceInfo");
            Log.WriteLog("PerformanceInfo cleaned at " + DateTime.Now, Log.VerbosityLevels.High);
            osm.Database.ExecuteSqlRaw("TRUNCATE TABLE GeneratedMapData");
            Log.WriteLog("GeneratedMapData cleaned at " + DateTime.Now, Log.VerbosityLevels.High);
            osm.Database.ExecuteSqlRaw("TRUNCATE TABLE SlippyMapTiles");
            Log.WriteLog("SlippyMapTiles cleaned at " + DateTime.Now, Log.VerbosityLevels.High);
            Log.WriteLog("DB cleaned at " + DateTime.Now);
        }

        public static void TestTagParser()
        {
            //For future reference: default was the code from the previous commit, 
            //alt is the code checked in with this change. over 1,000 loops its faster on all the scenarios tested
            //usually running in about half the time.
            Log.WriteLog("perf-testing tag parser options");
            var asm2 = Assembly.LoadFrom(@"PraxisMapTilesImageSharp.dll");
            var mtImage = (IMapTiles)Activator.CreateInstance(asm2.GetType("PraxisCore.MapTiles")); //Not actually used to draw, but I need to initialize TagParser.
            TagParser.Initialize(false, mtImage);
            System.Threading.Thread.Sleep(100);
            List<PlaceTags> emptyList = new List<PlaceTags>();
            Stopwatch sw = new Stopwatch();
            sw.Start();
            for (var i = 0; i < 100000; i++)
                foreach (var style in TagParser.allStyleGroups.First().Value)
                    TagParser.GetStyleForOsmWay(emptyList);
            //TagParser.MatchOnTags(style, emptyList);
            sw.Stop();
            Log.WriteLog("100000 empty lists run in " + sw.ElapsedTicks + " ticks with default (" + sw.ElapsedMilliseconds / 100000.0 + "ms avg)");

            //sw.Restart();
            //for (var i = 0; i < 1000; i++)
            //    foreach (var style in TagParser.styles)
            //        TagParser.MatchOnTagsAlt(style, emptyList);
            //sw.Stop();
            //Log.WriteLog("1000 empty lists run in " + sw.ElapsedTicks + " ticks with alt");

            //test with a set that matches on the default entry only.
            List<PlaceTags> defaultSingle = new List<PlaceTags>();
            defaultSingle.Add(new PlaceTags() { Key = "badEntry", Value = "mustBeDefault" });

            sw.Restart();
            for (var i = 0; i < 100000; i++)
                foreach (var style in TagParser.allStyleGroups.First().Value)
                    TagParser.GetStyleForOsmWay(defaultSingle);
            //TagParser.MatchOnTags(style, defaultSingle);
            sw.Stop();
            Log.WriteLog("100000 single entry default-match lists run in " + sw.ElapsedTicks + " ticks (" + sw.ElapsedMilliseconds / 100000.0 + "ms avg)");

            //sw.Restart();
            //for (var i = 0; i < 1000; i++)
            //    foreach (var style in TagParser.styles)
            //        TagParser.MatchOnTagsAlt(style, defaultSingle);
            //sw.Stop();
            //Log.WriteLog("1000 default-match lists run in " + sw.ElapsedTicks + " ticks with alt");

            //test with a set that has a lot of tags.
            List<PlaceTags> biglist = new List<PlaceTags>();
            biglist.Add(new PlaceTags() { Key = "badEntry", Value = "nothing" });
            biglist.Add(new PlaceTags() { Key = "place", Value = "neighborhood" });
            biglist.Add(new PlaceTags() { Key = "natual", Value = "hill" });
            biglist.Add(new PlaceTags() { Key = "lanes", Value = "7" });
            biglist.Add(new PlaceTags() { Key = "placeholder", Value = "stuff" });
            biglist.Add(new PlaceTags() { Key = "screensize", Value = "small" });
            biglist.Add(new PlaceTags() { Key = "twoMore", Value = "entries" });
            biglist.Add(new PlaceTags() { Key = "andHere", Value = "WeGo" });
            biglist.Add(new PlaceTags() { Key = "waterway", Value = "river" });

            sw.Restart();
            for (var i = 0; i < 100000; i++)
                foreach (var style in TagParser.allStyleGroups.First().Value)
                    TagParser.GetStyleForOsmWay(biglist);
            //TagParser.MatchOnTags(style, defaultSingle);
            sw.Stop();
            Log.WriteLog("100000 8-tag match-water lists run in " + sw.ElapsedTicks + " ticks(" + sw.ElapsedMilliseconds / 100000.0 + "ms avg)");

            biglist.Remove(biglist.Last()); //Remove the water-match tag.
            biglist.Add(new PlaceTags() { Key = "other", Value = "tag" });

            sw.Restart();
            for (var i = 0; i < 100000; i++)
                foreach (var style in TagParser.allStyleGroups.First().Value)
                    TagParser.GetStyleForOsmWay(biglist);
            //TagParser.MatchOnTags(style, defaultSingle);
            sw.Stop();
            Log.WriteLog("100000 8-tag default match lists run in " + sw.ElapsedTicks + " ticks(" + sw.ElapsedMilliseconds / 100000.0 + "ms avg)");

            var biglist2 = biglist.Select(b => b).ToList();
            biglist2.Add(new PlaceTags() { Key = "2badEntry", Value = "nothing" });
            biglist2.Add(new PlaceTags() { Key = "2place", Value = "neighborhood" });
            biglist2.Add(new PlaceTags() { Key = "2natual", Value = "hill" });
            biglist2.Add(new PlaceTags() { Key = "2lanes", Value = "7" });
            biglist2.Add(new PlaceTags() { Key = "2placeholder", Value = "stuff" });
            biglist2.Add(new PlaceTags() { Key = "2screensize", Value = "small" });
            biglist2.Add(new PlaceTags() { Key = "2twoMore", Value = "entries" });
            biglist2.Add(new PlaceTags() { Key = "2andHere", Value = "WeGo" });
            biglist2.Add(new PlaceTags() { Key = "2waterway", Value = "river" });
            biglist2.Add(new PlaceTags() { Key = "32badEntry", Value = "nothing" });
            biglist2.Add(new PlaceTags() { Key = "32place", Value = "neighborhood" });
            biglist2.Add(new PlaceTags() { Key = "32natual", Value = "hill" });
            biglist2.Add(new PlaceTags() { Key = "32lanes", Value = "7" });
            biglist2.Add(new PlaceTags() { Key = "32placeholder", Value = "stuff" });
            biglist2.Add(new PlaceTags() { Key = "32screensize", Value = "small" });
            biglist2.Add(new PlaceTags() { Key = "32twoMore", Value = "entries" });
            biglist2.Add(new PlaceTags() { Key = "32andHere", Value = "WeGo" });
            biglist2.Add(new PlaceTags() { Key = "32waterway", Value = "river" });
            biglist2.Add(new PlaceTags() { Key = "42badEntry", Value = "nothing" });
            biglist2.Add(new PlaceTags() { Key = "42place", Value = "neighborhood" });
            biglist2.Add(new PlaceTags() { Key = "42natual", Value = "hill" });
            biglist2.Add(new PlaceTags() { Key = "42lanes", Value = "7" });
            biglist2.Add(new PlaceTags() { Key = "42placeholder", Value = "stuff" });
            biglist2.Add(new PlaceTags() { Key = "42screensize", Value = "small" });
            biglist2.Add(new PlaceTags() { Key = "42twoMore", Value = "entries" });
            biglist2.Add(new PlaceTags() { Key = "42andHere", Value = "WeGo" });
            biglist2.Add(new PlaceTags() { Key = "42waterway", Value = "river" });
            biglist2.Add(new PlaceTags() { Key = "52badEntry", Value = "nothing" });
            biglist2.Add(new PlaceTags() { Key = "52place", Value = "neighborhood" });
            biglist2.Add(new PlaceTags() { Key = "52natual", Value = "hill" });
            biglist2.Add(new PlaceTags() { Key = "52lanes", Value = "7" });
            biglist2.Add(new PlaceTags() { Key = "52placeholder", Value = "stuff" });
            biglist2.Add(new PlaceTags() { Key = "52screensize", Value = "small" });
            biglist2.Add(new PlaceTags() { Key = "52twoMore", Value = "entries" });
            biglist2.Add(new PlaceTags() { Key = "52andHere", Value = "WeGo" });
            biglist2.Add(new PlaceTags() { Key = "52waterway", Value = "river" });

            sw.Restart();
            for (var i = 0; i < 100000; i++)
                foreach (var style in TagParser.allStyleGroups.First().Value)
                    TagParser.GetStyleForOsmWay(biglist2);
            //TagParser.MatchOnTags(style, defaultSingle);
            sw.Stop();
            Log.WriteLog("100000 48-tag default match lists run in " + sw.ElapsedTicks + " ticks(" + sw.ElapsedMilliseconds / 100000.0 + "ms avg)");

            //sw.Restart();
            //for (var i = 0; i < 1000; i++)
            //    foreach (var style in TagParser.styles)
            //        TagParser.MatchOnTagsAlt(style, defaultSingle);
            //sw.Stop();
            //Log.WriteLog("1000 big match on water lists run in " + sw.ElapsedTicks + " ticks with alt");

            Log.WriteLog("Using dictionary instead of list:");
            Dictionary<string, string> searchDict = new Dictionary<string, string>();

            sw.Restart();
            for (var i = 0; i < 100000; i++)
                foreach (var style in TagParser.allStyleGroups.First().Value)
                    TagParser.GetStyleForOsmWay(searchDict);
            sw.Stop();
            Log.WriteLog("100000 empty dicts run in " + sw.ElapsedTicks + " ticks with default (" + sw.ElapsedMilliseconds / 100000.0 + "ms avg)");

            searchDict.Add("badEntry", "mustBeDefault");

            sw.Restart();
            for (var i = 0; i < 100000; i++)
                foreach (var style in TagParser.allStyleGroups.First().Value)
                    TagParser.GetStyleForOsmWay(searchDict);
            sw.Stop();
            Log.WriteLog("100000 single entry default-matchdicts run in " + sw.ElapsedTicks + " ticks (" + sw.ElapsedMilliseconds / 100000.0 + "ms avg)");

            searchDict = biglist.ToDictionary(k => k.Key, v => v.Value);

            sw.Restart();
            for (var i = 0; i < 100000; i++)
                foreach (var style in TagParser.allStyleGroups.First().Value)
                    TagParser.GetStyleForOsmWay(searchDict);
            sw.Stop();
            Log.WriteLog("100000 8-tag match-default dicts run in " + sw.ElapsedTicks + " ticks(" + sw.ElapsedMilliseconds / 100000.0 + "ms avg)");

            searchDict.Add("waterway", "natural");
            sw.Restart();
            for (var i = 0; i < 100000; i++)
                foreach (var style in TagParser.allStyleGroups.First().Value)
                    TagParser.GetStyleForOsmWay(searchDict);
            sw.Stop();
            Log.WriteLog("100000 9-tag match-water dicts run in " + sw.ElapsedTicks + " ticks(" + sw.ElapsedMilliseconds / 100000.0 + "ms avg)");

            searchDict = biglist2.ToDictionary(k => k.Key, v => v.Value);
            sw.Restart();
            for (var i = 0; i < 100000; i++)
                foreach (var style in TagParser.allStyleGroups.First().Value)
                    TagParser.GetStyleForOsmWay(searchDict);
            sw.Stop();
            Log.WriteLog("100000 45-tag match-default dicts run in " + sw.ElapsedTicks + " ticks(" + sw.ElapsedMilliseconds / 100000.0 + "ms avg)");


            searchDict.Add("waterway", "natural");
            sw.Restart();
            for (var i = 0; i < 100000; i++)
                foreach (var style in TagParser.allStyleGroups.First().Value)
                    TagParser.GetStyleForOsmWay(searchDict);
            sw.Stop();
            Log.WriteLog("100000 46-tag match-water dicts run in " + sw.ElapsedTicks + " ticks(" + sw.ElapsedMilliseconds / 100000.0 + "ms avg)");





        }

        public static void TestCropVsNoCropDraw(string CellToTest)
        {
            Log.WriteLog("perf-testing cropping StoredWay objects before drawing");

            //load objects
            GeoArea testArea6 = OpenLocationCode.DecodeValid(CellToTest);
            var areaPoly = Converters.GeoAreaToPolygon(testArea6);
            var db = new PraxisContext();
            var places = db.Places.Include(w => w.Tags).Where(w => w.ElementGeometry.Intersects(areaPoly)).ToList();
            Log.WriteLog("Loaded " + places.Count + " objects for test");

            ImageStats info = new ImageStats(testArea6, 512, 512); //using default Slippy map tile size for comparison.
            //draw objects as is.
            Stopwatch sw = new Stopwatch();
            sw.Start();
            var tile1 = MapTiles.DrawAreaAtSize(info, places);
            sw.Stop();
            Log.WriteLog("Uncropped tile drawn in " + sw.ElapsedMilliseconds + "ms");

            //crop all objects
            sw.Restart();
            foreach (var ap in places) //Crop geometry and set tags for coloring.
                try //Error handling for 'non-noded intersection' errors.
                {
                    ap.ElementGeometry = ap.ElementGeometry.Intersection(areaPoly); //This is a ref list, so this crop will apply if another call is made to this function with the same list.
                }
                catch (Exception ex)
                {
                    //not actually handling it.
                }

            sw.Stop();
            Log.WriteLog("Geometry objects cropped in " + sw.ElapsedMilliseconds + "ms");

            sw.Restart();
            var tile2 = MapTiles.DrawAreaAtSize(info, places);
            sw.Stop();
            Log.WriteLog("Cropped tile drawn in " + sw.ElapsedMilliseconds + "ms");
        }

        //public static TestIndexingPerf()
        //{
        //    //Current Node Index logic
        //    long nodecounter = 0;
        //    long minNode = long.MaxValue;
        //    long maxNode = long.MinValue;
        //    if (pb2.primitivegroup[0].dense != null)
        //    {
        //        foreach (var n in pb2.primitivegroup[0].dense.id)
        //        {
        //            nodecounter += n;
        //            if (nodecounter < minNode)
        //                minNode = nodecounter;
        //            if (nodecounter > maxNode)
        //                maxNode = nodecounter;
        //        }
        //        nodeFinder2.TryAdd(passedBC, new Tuple<long, long>(minNode, maxNode));
        //    }
        //    //new logic.
        //}

        public static void TestDrawingHoles()
        {
            //Results: correctly adding holes to a path makes it draw faster at all sizes, and uses far less RAM.
            //Load one item from a file. Using Lake Erie for now
            var elementID = 4039900;
            string filename = @"D:\Projects\PraxisMapper Files\XmlToProcess\ohio-latest.osm.pbf";
            PraxisCore.PbfReader.PbfReader r = new PraxisCore.PbfReader.PbfReader();
            var testElement = r.LoadOneRelationFromFile(filename, elementID);
            var NTSelement = GeometrySupport.ConvertOsmEntryToPlace(testElement);

            //prep the drawing area.
            var geoarea = Converters.GeometryToGeoArea(NTSelement.ElementGeometry);
            geoarea = new Google.OpenLocationCode.GeoArea(geoarea.SouthLatitude - ConstantValues.resolutionCell10,
                geoarea.WestLongitude - ConstantValues.resolutionCell10,
                geoarea.NorthLatitude + ConstantValues.resolutionCell10,
                geoarea.EastLongitude + ConstantValues.resolutionCell10); //add some padding to the edges.
            ImageStats stats = new ImageStats(geoarea, (int)(geoarea.LongitudeWidth / ConstantValues.resolutionCell11Lon * .5), (int)(geoarea.LatitudeHeight / ConstantValues.resolutionCell11Lat * .5));

            //Draw the image the old way.
            //Stopwatch sw = new Stopwatch();
            //sw.Start();
            //SKBitmap bitmap = new SKBitmap(stats.imageSizeX, stats.imageSizeY, SKColorType.Rgba8888, SKAlphaType.Premul);
            //SKCanvas canvas = new SKCanvas(bitmap);
            //var bgColor = SKColors.White;
            //canvas.Clear(bgColor);
            //canvas.Scale(1, -1, stats.imageSizeX / 2, stats.imageSizeY / 2);
            //SKPaint paint = new SKPaint();
            //paint.Color = SKColors.Blue;

            //var path = new SKPath();
            //path.FillType = SKPathFillType.EvenOdd;
            //var p = NTSelement.elementGeometry as Polygon;
            //if (p.Holes.Length == 0)
            //{
            //    path.AddPoly(Converters.PolygonToSKPoints(p, stats.area, stats.degreesPerPixelX, stats.degreesPerPixelY));
            //    //path.
            //    canvas.DrawPath(path, paint);
            //}
            //else
            //{
            //    //var innerBitmap = MapTiles.DrawPolygon(p, paint, stats);
            //    //canvas.DrawBitmap(innerBitmap, 0, 0, paint);
            //}
            //var ms = new MemoryStream();
            //var skms = new SKManagedWStream(ms);
            //bitmap.Encode(skms, SKEncodedImageFormat.Png, 100);
            //var image1 = ms.ToArray();
            //skms.Dispose(); ms.Close(); ms.Dispose();
            //sw.Stop();
            //Log.WriteLog("Separate bitmap drawn in " + sw.Elapsed);

            //and draw again the new way
            Stopwatch sw2 = new Stopwatch();
            sw2.Start();
            SKBitmap bitmap2 = new SKBitmap(stats.imageSizeX, stats.imageSizeY, SKColorType.Rgba8888, SKAlphaType.Premul);
            SKCanvas canvas2 = new SKCanvas(bitmap2);
            var bgColor2 = SKColors.White;
            canvas2.Clear(bgColor2);
            canvas2.Scale(1, -1, stats.imageSizeX / 2, stats.imageSizeY / 2);
            SKPaint paint2 = new SKPaint();
            paint2.Color = SKColors.Blue;

            var path2 = new SKPath();
            path2.FillType = SKPathFillType.EvenOdd;
            var p2 = NTSelement.ElementGeometry as Polygon;
            //path2.AddPoly(PolygonToSKPoints(p2.ExteriorRing, stats.area, stats.degreesPerPixelX, stats.degreesPerPixelY));
            foreach (var hole in p2.InteriorRings)
            {
                //path2.AddPoly(PolygonToSKPoints(hole, stats.area, stats.degreesPerPixelX, stats.degreesPerPixelY));
            }
            canvas2.DrawPath(path2, paint2);

            var ms2 = new MemoryStream();
            var skms2 = new SKManagedWStream(ms2);
            bitmap2.Encode(skms2, SKEncodedImageFormat.Png, 100);
            var image2 = ms2.ToArray();
            skms2.Dispose(); ms2.Close(); ms2.Dispose();
            sw2.Stop();

            Log.WriteLog("Multiple polys in path drawn in " + sw2.Elapsed);
            //Log.WriteLog("Images are identical: " + image1.SequenceEqual(image2));

            //File.WriteAllBytes("test1.png", image1);
            File.WriteAllBytes("test2.png", image2);
        }

        public static void TestPbfParsing()
        {
            var pmFI = new PMFeatureInterpreter();
            var osmFI = new OsmSharp.Geo.DefaultFeatureInterpreter();

            System.Diagnostics.Stopwatch sw = new Stopwatch();
            sw.Start();
            var pbfR = new PraxisCore.PbfReader.PbfReader();
            pbfR.outputPath = "";
            var pmComplete = pbfR.LoadOneRelationFromFile(@"C:\praxis\ohio-latest.osm.pbf", 4039900);
            sw.Stop();
            Log.WriteLog("Customized PBF reader loaded 1 area in " + sw.Elapsed);

            sw.Restart();
            OsmSharp.Streams.PBFOsmStreamSource pbfO = new PBFOsmStreamSource(new FileStream(@"C:\praxis\ohio-latest.osm.pbf", FileMode.Open));
            var osmComplete = pbfO.Where(p => p.Id == 4039900 && p.Type == OsmGeoType.Relation).ToComplete().FirstOrDefault();
            sw.Stop();
            Log.WriteLog("Original PBF reader loaded 1 area in " + sw.Elapsed);
        }

        public static void TestMaptileDrawing()
        {
            Log.WriteLog("Testing Maptile drawing");
            var db = new PraxisContext();
            IMapTiles mtSkia;
            IMapTiles mtImage;
            var asm = Assembly.LoadFrom(@"PraxisMapTilesSkiaSharp.dll");
            mtSkia = (IMapTiles)Activator.CreateInstance(asm.GetType("PraxisCore.MapTiles"));

            var asm2 = Assembly.LoadFrom(@"PraxisMapTilesImageSharp.dll");
            mtImage = (IMapTiles)Activator.CreateInstance(asm2.GetType("PraxisCore.MapTiles"));

            Log.WriteLog("Both engines loaded.");

            TagParser.Initialize(false, mtSkia);
            mtImage.Initialize(); //also needs called since it's not initialized yet.

            List<long> skiaDurations = new List<long>();
            List<long> imageDurations = new List<long>();
            //Get an area from the DB, draw some map tiles with each.
            var mapArea = OpenLocationCode.DecodeValid("86HWF5");
            var mapPoly = Converters.GeoAreaToPolygon(mapArea);
            var mapData = db.Places.Where(e => e.ElementGeometry.Intersects(mapPoly)).ToList(); //pull them all into RAM to skip DB perf issue.

            string startPoint = "86HWF5"; //add 4 chars to draw cell8 tiles.
                                          //string endPoint = "86HW99"; //15 Cell6 blocks to draw, so 400 * 15 tiles for testing.

            for (int pos1 = 0; pos1 < 20; pos1++)
                for (int pos2 = 0; pos2 < 20; pos2++)
                {
                    string cellToCheck = startPoint + OpenLocationCode.CodeAlphabet[pos1].ToString() + OpenLocationCode.CodeAlphabet[pos2].ToString();
                    var area = new OpenLocationCode(cellToCheck).Decode();
                    ImageStats stats = new ImageStats(area, 80, 100);
                    var places = GetPlaces(area, mapData, 0, "mapTiles");
                    var drawOps = MapTileSupport.GetPaintOpsForPlaces(places, "mapTiles", stats);

                    Stopwatch swSkia = new Stopwatch();
                    Stopwatch swImage = new Stopwatch();
                    swSkia.Start();
                    //draw tile
                    mtSkia.DrawAreaAtSize(stats, drawOps);
                    swSkia.Stop();
                    skiaDurations.Add(swSkia.ElapsedTicks);
                    swImage.Start();
                    //draw tile
                    mtImage.DrawAreaAtSize(stats, drawOps);
                    swImage.Stop();
                    imageDurations.Add(swImage.ElapsedTicks);

                    Console.WriteLine(cellToCheck + ": " + swSkia.Elapsed + " vs " + swImage.Elapsed);
                }
            Console.WriteLine("Note: tick is " + Stopwatch.Frequency + " per second");
            Console.WriteLine("Average Skia time:" + skiaDurations.Average());
            Console.WriteLine("Average Skia tiles per second:" + Stopwatch.Frequency / skiaDurations.Average());
            Console.WriteLine("Average ImageSharp time:" + imageDurations.Average());
            Console.WriteLine("Average ImageSharp tiles per second:" + Stopwatch.Frequency / imageDurations.Average());
        }

        public static void TestTagParsers()
        {
            var asm2 = Assembly.LoadFrom(@"PraxisMapTilesImageSharp.dll");
            var mtImage = (IMapTiles)Activator.CreateInstance(asm2.GetType("PraxisCore.MapTiles")); //Not actually used to draw, but I need to initialize TagParser.
            TagParser.Initialize(false, mtImage);

            //create collection of tags.
            List<PlaceTags> biglist = new List<PlaceTags>();
            biglist.Add(new PlaceTags() { Key = "badEntry", Value = "nothing" });
            biglist.Add(new PlaceTags() { Key = "place", Value = "neighborhood" });
            biglist.Add(new PlaceTags() { Key = "natual", Value = "hill" });
            biglist.Add(new PlaceTags() { Key = "lanes", Value = "7" });
            biglist.Add(new PlaceTags() { Key = "placeholder", Value = "stuff" });
            biglist.Add(new PlaceTags() { Key = "screensize", Value = "small" });
            biglist.Add(new PlaceTags() { Key = "twoMore", Value = "entries" });
            biglist.Add(new PlaceTags() { Key = "andHere", Value = "WeGo" });
            biglist.Add(new PlaceTags() { Key = "waterway", Value = "river" });

            for (var l = 0; l < 5; l++)
            {
                var sw = new Stopwatch();
                sw.Start();
                for (var i = 0; i < 100000; i++)
                    foreach (var style in TagParser.allStyleGroups.First().Value)
                        MatchOnTags(style.Value, biglist);
                sw.Stop();
                Console.WriteLine("Ran normal matchOnTags in \r\n" + sw.ElapsedTicks + " ticks");

                var firstTicks = sw.ElapsedTicks;

                sw.Restart();
                for (var i = 0; i < 100000; i++)
                    foreach (var style in TagParser.allStyleGroups.First().Value)
                        MatchOnTagsSpans(style.Value, biglist);
                sw.Stop();
                Console.WriteLine("Ran matchOnTagsSpan in \r\n" + sw.ElapsedTicks + " ticks");

                var secondTicks = sw.ElapsedTicks;
                Console.WriteLine("Performance Difference: Second runs in " + ((double)secondTicks / (double)firstTicks) * 100.0 + "% of the time.");

                sw.Restart();
                for (var i = 0; i < 100000; i++)
                    foreach (var style in TagParser.allStyleGroups.First().Value)
                        MatchOnTags(style.Value, biglist.ToDictionary(k => k.Key, v => v.Value));
                sw.Stop();
                Console.WriteLine("Ran matchOnTags via dictionary in \r\n" + sw.ElapsedTicks + " ticks");

                var thirdTicks = sw.ElapsedTicks;
                Console.WriteLine("Performance Difference: Third runs in " + ((double)thirdTicks / (double)firstTicks) * 100.0 + "% of the time.");


                sw.Restart();
                for (var i = 0; i < 100000; i++)
                    foreach (var style in TagParser.allStyleGroups.First().Value)
                        MatchOnTagsDictstyle(style.Value, biglist);
                sw.Stop();
                Console.WriteLine("Ran matchOnTags with dictstyle logic in \r\n" + sw.ElapsedTicks + " ticks");

                var fourthTicks = sw.ElapsedTicks;
                Console.WriteLine("Performance Difference: fourth runs in " + ((double)fourthTicks / (double)firstTicks) * 100.0 + "% of the time.");
            }

        }

        public static bool MatchOnTags(StyleEntry tpe, ICollection<PlaceTags> tags)
        {
            bool OrMatched = false;
            int orRuleCount = 0;

            //Step 1: check all the rules against these tags.
            //The * value is required for all the rules, so check it first.
            for (var i = 0; i < tpe.StyleMatchRules.Count; i++)
            {
                var entry = tpe.StyleMatchRules.ElementAt(i);
                if (entry.Value == "*") //The Key needs to exist, but any value counts.
                {
                    if (tags.Any(t => t.Key == entry.Key))
                        continue;
                }

                switch (entry.MatchType)
                {
                    case "any":
                        if (!tags.Any(t => t.Key == entry.Key))
                            return false;

                        var possibleValues = entry.Value.Split("|");
                        var actualValue = tags.FirstOrDefault(t => t.Key == entry.Key).Value;
                        if (!possibleValues.Contains(actualValue))
                            return false;
                        break;
                    case "or": //Or rules must be counted, but we can skip checking once we have a match, since only one of them needs to match. Otherwise is the same as ANY logic.
                        orRuleCount++;
                        if (!tags.Any(t => t.Key == entry.Key) || OrMatched)
                            continue;

                        var possibleValuesOr = entry.Value.Split("|");
                        var actualValueOr = tags.FirstOrDefault(t => t.Key == entry.Key).Value;
                        if (possibleValuesOr.Contains(actualValueOr))
                            OrMatched = true;
                        break;
                    case "not":
                        if (!tags.Any(t => t.Key == entry.Key))
                            continue;

                        var possibleValuesNot = entry.Value.Split("|");
                        var actualValueNot = tags.FirstOrDefault(t => t.Key == entry.Key).Value;
                        if (possibleValuesNot.Contains(actualValueNot))
                            return false; //Not does not want to match this.
                        break;
                    case "equals": //for single possible values, EQUALS is slightly faster than ANY
                        if (!tags.Any(t => t.Key == entry.Key))
                            return false;
                        if (tags.FirstOrDefault(t => t.Key == entry.Key).Value != entry.Value)
                            return false;
                        break;
                    case "none":
                        //never matches anything. Useful for background color or other special styles that need to exist but don't want to appear normally.
                        return false;
                    case "default":
                        //Always matches. Can only be on one entry, which is the last entry and the default color
                        return true;
                }
            }

            //Now, we should have bailed out if any mandatory thing didn't match. Should only be on whether or not any of our Or checks passsed.
            if (OrMatched || orRuleCount == 0)
                return true;

            return false;
        }

        public static bool MatchOnTagsSpans(StyleEntry tpe, List<PlaceTags> tags)
        {
            bool OrMatched = false;
            int orRuleCount = 0;

            //Step 1: check all the rules against these tags.
            //The * value is required for all the rules, so check it first.
            for (var i = 0; i < tpe.StyleMatchRules.Count; i++)
            {
                var entry = tpe.StyleMatchRules.ElementAt(i);
                if (entry.Value == "*") //The Key needs to exist, but any value counts.
                {
                    if (tags.Any(t => t.Key == entry.Key))
                        continue;
                }

                switch (entry.MatchType)
                {
                    case "any":
                        if (!tags.Any(t => t.Key == entry.Key))
                            return false;
                        var desc = entry.Value.AsSpan();
                        var actualValue = tags.FirstOrDefault(t => t.Key == entry.Key).Value;
                        bool hasMatch = false;
                        while (desc.Length > 0)
                        {
                            var nextvalue = desc.SplitNext('|');
                            if (nextvalue.Equals(actualValue, StringComparison.Ordinal))
                            {
                                hasMatch = true;
                                break;
                            }
                        }
                        if (!hasMatch) return false;
                        break;
                    case "or": //Or rules must be counted, but we can skip checking once we have a match, since only one of them needs to match. Otherwise is the same as ANY logic.
                        orRuleCount++;
                        if (!tags.Any(t => t.Key == entry.Key) || OrMatched)
                            continue;

                        var actualValueOr = tags.FirstOrDefault(t => t.Key == entry.Key).Value;
                        var descOr = entry.Value.AsSpan();
                        while (descOr.Length > 0)
                        {
                            var nextvalue = descOr.SplitNext('|');
                            if (nextvalue.Equals(actualValueOr, StringComparison.Ordinal))
                            {
                                OrMatched = true;
                                break;
                            }
                        }
                        break;
                    case "not":
                        if (!tags.Any(t => t.Key == entry.Key))
                            continue;

                        var descNot = entry.Value.AsSpan();
                        var actualValueNot = tags.FirstOrDefault(t => t.Key == entry.Key).Value;
                        while (descNot.Length > 0)
                        {
                            var nextvalue = descNot.SplitNext('|');
                            if (nextvalue.Equals(actualValueNot, StringComparison.Ordinal))
                                return false; //nots fail out.
                        }
                        break;
                    case "equals": //for single possible values, EQUALS is slightly faster than ANY
                        if (!tags.Any(t => t.Key == entry.Key))
                            return false;
                        if (tags.FirstOrDefault(t => t.Key == entry.Key).Value != entry.Value)
                            return false;
                        break;
                    case "none":
                        //never matches anything. Useful for background color or other special styles that need to exist but don't want to appear normally.
                        return false;
                    case "default":
                        //Always matches. Can only be on one entry, which is the last entry and the default color
                        return true;
                }
            }

            //Now, we should have bailed out if any mandatory thing didn't match. Should only be on whether or not any of our Or checks passsed.
            if (OrMatched || orRuleCount == 0)
                return true;

            return false;
        }

        public static bool MatchOnTags(StyleEntry tpe, Dictionary<string, string> tags)
        {
            bool OrMatched = false;
            int orRuleCount = 0;

            StyleMatchRule entry;

            //Step 1: check all the rules against these tags.
            //The * value is required for all the rules, so check it first.
            for (var i = 0; i < tpe.StyleMatchRules.Count; i++)
            {
                entry = tpe.StyleMatchRules.ElementAt(i);

                string actualvalue = "";
                bool isPresent = tags.TryGetValue(entry.Key, out actualvalue);

                switch (entry.MatchType)
                {
                    case "any":
                        if (!isPresent)
                            return false;

                        if (entry.Value == "*")
                            continue;

                        if (!entry.Value.Contains(actualvalue))
                            return false;
                        break;
                    case "or": //Or rules don't fail early, since only one of them needs to match. Otherwise is the same as ANY logic.
                        orRuleCount++;
                        if (!isPresent || OrMatched) //Skip checking the actual value if we already matched on an OR rule.
                            continue;

                        if (entry.Value == "*" || entry.Value.Contains(actualvalue))
                            OrMatched = true;
                        break;
                    case "not":
                        if (!isPresent)
                            continue;

                        if (entry.Value.Contains(actualvalue) || entry.Value == "*")
                            return false; //Not does not want to match this.
                        break;
                    case "equals": //for single possible values, EQUALS is slightly faster than ANY
                        if (entry.Value == "*" && isPresent)
                            continue;

                        if (!isPresent || actualvalue != entry.Value)
                            return false;
                        break;
                    case "none":
                        //never matches anything. Useful for background color or other special styles that need to exist but don't want to appear normally.
                        return false;
                    case "default":
                        //Always matches. Can only be on one entry, which is the last entry and the default color
                        return true;
                }
            }

            //Now, we should have bailed out if any mandatory thing didn't match. Now make sure that we either had 0 OR checks or matched on any OR rule provided.
            if (OrMatched || orRuleCount == 0)
                return true;

            //We did not match an OR clause, so this TPE is not a match.
            return false;
        }

        public static bool MatchOnTagsDictstyle(StyleEntry tpe, List<PlaceTags> tags)
        {
            bool OrMatched = false;
            int orRuleCount = 0;

            StyleMatchRule entry;

            //Step 1: check all the rules against these tags.
            //The * value is required for all the rules, so check it first.
            for (var i = 0; i < tpe.StyleMatchRules.Count; i++)
            {
                entry = tpe.StyleMatchRules.ElementAt(i);

                var thisTag = tags.FirstOrDefault(t => t.Key == entry.Key);
                string thisValue = null;
                if (thisTag != null)
                    thisValue = thisTag.Value;

                switch (entry.MatchType)
                {
                    case "any":
                        if (thisValue == null)
                            return false;

                        if (entry.Value == "*")
                            continue;

                        if (!entry.Value.Contains(thisValue))
                            return false;
                        break;
                    case "or": //Or rules don't fail early, since only one of them needs to match. Otherwise is the same as ANY logic.
                        orRuleCount++;
                        if (thisValue == null || OrMatched) //Skip checking the actual value if we already matched on an OR rule.
                            continue;

                        if (entry.Value == "*" || entry.Value.Contains(thisValue))
                            OrMatched = true;
                        break;
                    case "not":
                        if (thisValue == null)
                            continue;

                        if (entry.Value.Contains(thisValue) || entry.Value == "*")
                            return false; //Not does not want to match this.
                        break;
                    case "equals": //for single possible values, EQUALS is slightly faster than ANY
                        if (entry.Value == "*" && thisValue != null)
                            continue;

                        if (thisValue == null || thisValue != entry.Value)
                            return false;
                        break;
                    case "none":
                        //never matches anything. Useful for background color or other special styles that need to exist but don't want to appear normally.
                        return false;
                    case "default":
                        //Always matches. Can only be on one entry, which is the last entry and the default color
                        return true;
                }
            }

            //Now, we should have bailed out if any mandatory thing didn't match. Now make sure that we either had 0 OR checks or matched on any OR rule provided.
            if (OrMatched || orRuleCount == 0)
                return true;

            //We did not match an OR clause, so this TPE is not a match.
            return false;
        }

        public static void TestSpanOnEntry(string sw)
        {
            Stopwatch timer = new Stopwatch();
            List<long> splitParse = new List<long>();
            List<long> spanParse = new List<long>();

            for (int i = 0; i < 100000; i++)
            {
                timer.Start();
                var parts = sw.Split('\t');
                DbTables.Place entry = new DbTables.Place();
                entry.SourceItemID = parts[0].ToLong();
                entry.SourceItemType = parts[1].ToInt();
                entry.ElementGeometry = GeometrySupport.GeometryFromWKT(parts[2]);
                entry.AreaSize = parts[3].ToDouble();
                entry.PrivacyId = Guid.Parse(parts[4]);
                timer.Stop();
                splitParse.Add(timer.ElapsedTicks);
                timer.Restart();

                var source = sw.AsSpan();
                DbTables.Place e2 = new DbTables.Place();
                e2.SourceItemID = source.SplitNext('\t').ToLong();
                e2.SourceItemType = source.SplitNext('\t').ToInt();
                e2.ElementGeometry = GeometryFromWKT(source.SplitNext('\t').ToString());
                e2.AreaSize = source.SplitNext('\t').ToDouble();
                e2.PrivacyId = Guid.Parse(source);
                timer.Stop();
                spanParse.Add(timer.ElapsedTicks);
            }

            Console.WriteLine("Average string.split() results: " + splitParse.Average());
            Console.WriteLine("Average span() results: " + spanParse.Average());
        }

        public static DbTables.Place ConvertSingleTsvPlace(string sw)
        {
            var source = sw.AsSpan();
            DbTables.Place entry = new DbTables.Place();
            entry.SourceItemID = source.SplitNext('\t').ToLong();
            entry.SourceItemType = source.SplitNext('\t').ToInt();
            entry.ElementGeometry = GeometryFromWKT(source.SplitNext('\t').ToString());
            entry.AreaSize = source.SplitNext('\t').ToDouble();
            entry.PrivacyId = Guid.Parse(source);

            if (entry.ElementGeometry is Polygon e)
                e = GeometrySupport.CCWCheck((Polygon)entry.ElementGeometry);

            if (entry.ElementGeometry is MultiPolygon mp)
            {
                for (int i = 0; i < mp.Geometries.Length; i++)
                {
                    mp.Geometries[i] = GeometrySupport.CCWCheck((Polygon)mp.Geometries[i]);
                }
            }
            if (entry.ElementGeometry == null) //it failed the CCWCheck logic and couldn't be correctly oriented.
            {
                Log.WriteLog("NOTE: Item " + entry.SourceItemID + " - Failed to create valid geometry", Log.VerbosityLevels.Errors);
                return null;
            }

            return entry;
        }

        public static DbTables.Place ConvertSingleTsvPlaceSkipChecks(string sw)
        {
            var source = sw.AsSpan();
            DbTables.Place entry = new DbTables.Place();
            entry.SourceItemID = source.SplitNext('\t').ToLong();
            entry.SourceItemType = source.SplitNext('\t').ToInt();
            entry.ElementGeometry = GeometryFromWKT(source.SplitNext('\t').ToString());
            entry.AreaSize = source.SplitNext('\t').ToDouble();
            entry.PrivacyId = Guid.Parse(source);

            return entry;
        }

        public static void TestConvertFromTsv()
        {
            Stopwatch timer = new Stopwatch();
            List<long> withCheck = new List<long>();
            List<long> skipCheck = new List<long>();
            var data = System.IO.File.ReadAllLines(@"D:\Projects\PraxisMapper Files\Trimmed JSON Files\ohio\ohio-latest.osm-3235.geomDatadone");

            foreach (var line in data)
            {
                timer.Start();
                var osm = ConvertSingleTsvPlace(line);
                timer.Stop();
                if (osm == null)
                    Console.WriteLine("A saved line didn't convert. It DID fail a check.");
                withCheck.Add(timer.ElapsedTicks);
                timer.Restart();
                ConvertSingleTsvPlaceSkipChecks(line);
                timer.Stop();
                skipCheck.Add(timer.ElapsedTicks);
            }
            Console.WriteLine("Average checked results: " + withCheck.Average());
            Console.WriteLine("Average unchecked results: " + skipCheck.Average());
            Console.WriteLine("Total times (ms): " + withCheck.Sum() / 10000 + " vs  " + skipCheck.Sum() / 10000);
            //Summary:  unchecked is a good bit faster, but its also not a giant performance penalty to do it. 
        }

        public static void TestSearchArea()
        {
            var db = new PraxisContext();
            var location = "87C6J9JR"; //JR

            var area = (GeoArea)OpenLocationCode.DecodeValid(location);
            var places = GetPlaces(area);
            for (int i = 0; i < 5; i++)
            {
                Stopwatch sw = new Stopwatch();
                sw.Start();
                //old search
                StringBuilder sb1 = new StringBuilder();
                var results1 = SearchAreaOld(ref area, ref places);
                foreach (var d in results1)
                    foreach (var v in d.Value)
                        sb1.Append(d.Key).Append('|').Append(v.Name).Append('|').Append(v.areaType).Append('|').Append(v.PrivacyId).Append('\n');
                var results2 = sb1.ToString();
                sw.Stop();
                Console.WriteLine("Old search ran in " + sw.ElapsedMilliseconds);
                //new search:
                sw.Restart();
                var sb2 = new StringBuilder();
                var results3 = PraxisCore.TerrainInfo.SearchAreaFull(ref area, ref places);
                foreach (var d in results3)
                    foreach (var v in d.data)
                        sb2.Append(d.plusCode).Append('|').Append(v.Name).Append('|').Append(v.areaType).Append('|').Append(v.PrivacyId).Append('\n');
                var results4 = sb2.ToString();
                sw.Stop();
                Console.WriteLine("New search ran in " + sw.ElapsedMilliseconds);
            }
        }

        public static List<FindPlacesResult> SearchAreaNew(ref GeoArea area, ref List<DbTables.Place> elements)
        {
            List<FindPlacesResult> results = new List<FindPlacesResult>(400); //starting capacity for a full Cell8

            //Singular function, returns 1 item entry per cell10.
            if (elements.Count == 0)
                return results;

            //var xCells = area.LongitudeWidth / ConstantValues.resolutionCell10;
            //var yCells = area.LatitudeHeight / ConstantValues.resolutionCell10;
            double x = area.Min.Longitude;
            double y = area.Min.Latitude;

            GeoArea searchArea;
            List<DbTables.Place> searchPlaces;
            FindPlacesResult? placeFound;

            //for (double xx = 0; xx < xCells; xx += 1)
            while (x < area.Max.Longitude)
            {
                searchArea = new GeoArea(area.Min.Latitude, x - ConstantValues.resolutionCell10, area.Max.Latitude, x + ConstantValues.resolutionCell10);
                searchPlaces = GetPlaces(searchArea, elements, skipTags: true);
                //TODO: test trimming this down into strips instead of searching full list every block.
                //for (double yy = 0; yy < yCells; yy += 1)
                while (y < area.Max.Latitude)
                {
                    placeFound = PraxisCore.TerrainInfo.FindPlacesInCell10(x, y, ref searchPlaces);
                    if (placeFound.HasValue)
                        results.Add(placeFound.Value);

                    y = Math.Round(y + ConstantValues.resolutionCell10, 6); //Round ensures we get to the next pluscode in the event of floating point errors.
                }
                x = Math.Round(x + ConstantValues.resolutionCell10, 6);
                y = area.Min.Latitude;
            }

            return results;
        }

        public static Dictionary<string, List<TerrainDataStandalone>> SearchAreaOld(ref GeoArea area, ref List<DbTables.Place> elements)
        {
            Dictionary<string, List<TerrainDataStandalone>> results = new Dictionary<string, List<TerrainDataStandalone>>(400); //starting capacity for a full Cell8

            //Singular function, returns 1 item entry per cell10.
            if (elements.Count == 0)
                return results;

            var xCells = area.LongitudeWidth / ConstantValues.resolutionCell10;
            var yCells = area.LatitudeHeight / ConstantValues.resolutionCell10;
            double x = area.Min.Longitude;
            double y = area.Min.Latitude;

            for (double xx = 0; xx < xCells; xx += 1)
            {
                for (double yy = 0; yy < yCells; yy += 1)
                {
                    var placeFound = FindPlacesInCell10(x, y, ref elements);
                    if (placeFound != null)
                        results.Add(placeFound.Item1, placeFound.Item2);

                    y = Math.Round(y + ConstantValues.resolutionCell10, 6); //Round ensures we get to the next pluscode in the event of floating point errors.
                }
                x = Math.Round(x + ConstantValues.resolutionCell10, 6);
                y = area.Min.Latitude;
            }

            return results;
        }

        public static Tuple<string, List<TerrainDataStandalone>> FindPlacesInCell10(double lon, double lat, ref List<DbTables.Place> places)
        {
            //Plural function, gets all areas in each cell10.
            var box = new GeoArea(new GeoPoint(lat, lon), new GeoPoint(lat + .000125, lon + .000125));
            var entriesHere = GetPlaces(box, places, skipTags: true);

            if (entriesHere.Count == 0)
                return null;

            var area = DetermineAreaPlaces(entriesHere);
            if (area.Count > 0)
            {
                string olc = new OpenLocationCode(lat, lon).CodeDigits;
                return new Tuple<string, List<TerrainDataStandalone>>(olc, area);
            }
            return null;
        }

        public static List<TerrainDataStandalone> DetermineAreaPlaces(List<DbTables.Place> entriesHere)
        {
            //Which Place in this given Area is the one that should be displayed on the game/map as the name? picks the smallest one.
            //This one return all entries, for a game mode that might need all of them.
            var results = new List<TerrainDataStandalone>(entriesHere.Count);
            foreach (var e in entriesHere)
                results.Add(new TerrainDataStandalone() { Name = TagParser.GetPlaceName(e.Tags), areaType = e.GameElementName, PrivacyId = e.PrivacyId });

            return results;
        }

        public static void BcryptSpeedCheck()
        {
            Stopwatch sw = new Stopwatch();
            List<long> results13 = new List<long>(100);
            List<long> results32 = new List<long>(100);
            var options13 = new CrypterOptions() {
                { CrypterOption.Rounds, 13}
            };
            var options32 = new CrypterOptions() {
                { CrypterOption.Rounds, 16}
            };
            BlowfishCrypter crypter = new BlowfishCrypter();
            string salt = "";
            string results = "";

            for (int i = 0; i < 10; i++)
            {
                sw.Restart();
                Console.WriteLine("making 13-round salt");
                salt = crypter.GenerateSalt(options13);
                Console.WriteLine("salt made");
                results = crypter.Crypt("password", salt);
                Console.WriteLine("pwd encrypted");
                sw.Stop();
                results13.Add(sw.ElapsedMilliseconds);
                sw.Restart();
                Console.WriteLine("making 31-round salt");
                salt = crypter.GenerateSalt(options32);
                Console.WriteLine("salt made");
                results = crypter.Crypt("password", salt);
                Console.WriteLine("pwd encrypted.");
                sw.Stop();
                results32.Add(sw.ElapsedMilliseconds);
                Console.WriteLine("test " + i + " done.");
            }

            Console.WriteLine("Creating password hashes:");
            Console.WriteLine("Average 13-round results: " +  results13.Average());
            Console.WriteLine("Average 31-round results: " + results32.Average());
            Console.WriteLine("Total times (ms): " + results13.Sum() + " vs  " + results32.Sum());

            salt = crypter.GenerateSalt(options13);
            var pwd13 = crypter.Crypt("password", salt);

            salt = crypter.GenerateSalt(options32);
            var pwd32 = crypter.Crypt("password", salt);

            results13.Clear();
            results32.Clear();

            for (int i = 0; i < 10; i++)
            {
                sw.Restart();
                crypter.Crypt("password", pwd13);
                sw.Stop();
                results13.Add(sw.ElapsedMilliseconds);
                sw.Restart();
                crypter.Crypt("password", pwd32);
                sw.Stop();
                results32.Add(sw.ElapsedMilliseconds);
            }

            Console.WriteLine("Checking password hashes vs existing entry:");
            Console.WriteLine("Average 13-round results: " + results13.Average());
            Console.WriteLine("Average 31-round results: " + results32.Average());
            Console.WriteLine("Total times (ms): " + results13.Sum() + " vs  " + results32.Sum());

        }

        public record RecordTest(int a, string b);
        public record struct RecordTest2(int a, string b);
        public static void TupleVsRecords()
        {
            for (int t = 0; t < 5; t++)
            {
                List<Tuple<int, string>> tuples = new List<Tuple<int, string>>(100000);
                List<RecordTest> records = new List<RecordTest>(100000);
                List<RecordTest2> recordstruct = new List<RecordTest2>(100000);

                Stopwatch sw = new Stopwatch();

                sw.Start();
                for (int i = 0; i < 1000000; i++)
                {
                    tuples.Add(Tuple.Create(i, "asdf"));
                }
                sw.Stop();
                Console.WriteLine("tuples created in " + sw.ElapsedTicks);

                sw.Restart();
                for (int i = 0; i < 1000000; i++)
                {
                    records.Add(new RecordTest(i, "asdf"));
                }
                sw.Stop();
                Console.WriteLine("records created in " + sw.ElapsedTicks);

                sw.Restart();
                for (int i = 0; i < 1000000; i++)
                {
                    recordstruct.Add(new RecordTest2(i, "asdf"));
                }
                sw.Stop();
                Console.WriteLine("recordstructs created in " + sw.ElapsedTicks);
            }
        }
    }
}
