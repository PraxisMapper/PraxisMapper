using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using NetTopologySuite.Geometries;
using PraxisCore;
using PraxisCore.GameTools;
using PraxisCore.Support;
using PraxisMapper.Classes;

namespace PraxisDemosPlugin.Controllers
{
    [ApiController]
    [Route("/[controller]")]
    public class SplatterController : Controller, IPraxisPlugin
    {
        string accountId, password;
        public static int colors = 32;
        public static Dictionary<int, GeometryTracker> splatCollection = new Dictionary<int, GeometryTracker>();
        //Perf note: Using raw geometries, instead of GeometryTracker, seems to be about twice as fast for some reason. GeometryTracker is supposed to be lighter-weight than that.

        readonly IConfiguration Configuration;

        public SplatterController(IConfiguration config)
        {
            Configuration = config;
        }

        public override void OnActionExecuting(ActionExecutingContext context)
        {
            base.OnActionExecuting(context);
            PraxisAuthentication.GetAuthInfo(Response, out accountId, out password);
            context.CheckCache(Request.Path, accountId); //If cached, sets context.Response and skips further processing
        }

        [HttpGet]
        [Route("/[controller]/")]
        [Route("/[controller]/Index")]
        public ActionResult Index()
        {
            return View();
        }


        [HttpPut]
        [Route("/[controller]/Splat/{plusCode}/{radius}")]
        public void Splat(string plusCode, double radius)
        {
            Response.Headers.Append("X-noPerfTrack", "Splatter/Splat/VARSREMOVED");
            var points = GenericData.GetPlayerData(accountId, "splatPoints").ToUTF8String().ToInt();
            if (points >= radius)
            {
                //A user wants to throw down a paint mark in the center of {plusCode} with a size of {radius} (in Cell10 tiles)
                var newGeo = MakeSplatShape(plusCode.ToGeoArea().ToPoint(), radius * ConstantValues.resolutionCell10);
                var color = Random.Shared.Next(colors);
                var updateTasks = new Task[colors];

                SimpleLockable.PerformWithLock("splatter", () =>
                {
                    GenericData.IncrementPlayerData(accountId, "splatPoints", -radius);

                    foreach (var s in splatCollection)
                    {
                        if (s.Key == color)
                            s.Value.AddGeometry(newGeo);
                        else
                            s.Value.RemoveGeometry(newGeo);
                        updateTasks[s.Key] = Task.Run(() => GenericData.SetGlobalDataJson("splat-" + s.Key, s.Value));
                    }

                    var db = new PraxisContext();
                    db.ExpireMapTiles(newGeo, "splatter");
                    db.ExpireSlippyMapTiles(newGeo, "splatter");
                    PraxisCacheHelper.Remove(plusCode + "-splatter");


                    Task.WaitAll(updateTasks);
                });
            }
        }

        [HttpPut]
        [Route("/[controller]/FreeSplat/{plusCode}/{radius}/{colorId}")]
        public void FreeSplat(string plusCode, double radius, int colorId)
        {
            Response.Headers.Append("X-noPerfTrack", "Splatter/Splat/VARSREMOVED");
            //A user wants to throw down a paint mark in the center of {plusCode} with a size of {radius} (in Cell10 tiles)
            var newGeo = MakeSplatShape(plusCode.ToGeoArea().ToPoint(), radius * ConstantValues.resolutionCell10);
            //var color = Random.Shared.Next(colors);
            var updateTasks = new Task[colors];

            SimpleLockable.PerformWithLock("splatter", () =>
            {
                GenericData.IncrementPlayerData(accountId, "splatPoints", -radius);

                foreach (var s in splatCollection)
                {
                    if (s.Key == colorId)
                        s.Value.AddGeometry(newGeo);
                    else
                        s.Value.RemoveGeometry(newGeo);
                    updateTasks[s.Key] = Task.Run(() => GenericData.SetGlobalDataJson("splat-" + s.Key, s.Value));
                }

                var db = new PraxisContext();
                db.ExpireMapTiles(newGeo, "splatter");
                db.ExpireSlippyMapTiles(newGeo, "splatter");
                PraxisCacheHelper.Remove(plusCode + "-splatter");

                Task.WaitAll(updateTasks);
            });
        }

        [HttpGet]
        [Route("/MapTile/SplatterSlippy/{zoom}/{x}/{y}.png")]
        [Route("/[controller]/Slippy/{zoom}/{x}/{y}.png")]
        public ActionResult SplatterSlippy(int zoom, int x, int y)
        {
            string tileKey = x.ToString() + "|" + y.ToString() + "|" + zoom.ToString();
            var info = new ImageStats(zoom, x, y, MapTileSupport.SlippyTileSizeSquare);
            info = MapTileSupport.ScaleBoundsCheck(info, Configuration["imageMaxSide"].ToInt(), Configuration["maxImagePixels"].ToLong());

            Response.Headers.Append("X-noPerfTrack", "Splatter/Slippy/VARSREMOVED");
            if (!DataCheck.IsInBounds(info.area))
            {
                Response.Headers.Append("X-notes", "OOB");
                return StatusCode(500);
            }
            byte[] tileData = MapTileSupport.GetExistingSlippyTile(tileKey, "splatter");
            if (tileData != null)
            {
                Response.Headers.Append("X-notes", "cached");
                return File(tileData, "image/png");
            }

            List<DbTables.Place> places = new List<DbTables.Place>(colors);
            foreach (var s in splatCollection)
                places.Add(new DbTables.Place() { ElementGeometry = s.Value.explored, StyleName = s.Key.ToString() });

            var paintOps = MapTileSupport.GetPaintOpsForPlaces(places, "splatter", info);
            var mapTile = MapTileSupport.MapTiles.DrawAreaAtSize(info, paintOps);
            MapTileSupport.SaveSlippyMapTile(info, tileKey, "splatter", mapTile);

            return File(mapTile, "image/png");
        }

        [HttpGet]
        [Route("/MapTile/Splatter/{plusCode}")]
        [Route("/[controller]/MapTile/{plusCode}")]
        public ActionResult SplatterTile(string plusCode)
        {
            Response.Headers.Append("X-noPerfTrack", "Splatter/MapTile/VARSREMOVED");
            if (!DataCheck.IsInBounds(plusCode))
            {
                Response.Headers.Append("X-notes", "OOB");
                return StatusCode(500);
            }

            var db = new PraxisContext();
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            var existingResults = MapTileSupport.GetExistingTileImage(plusCode, "splatter");
            if (existingResults != null)
            {
                Response.Headers.Append("X-notes", "cached");
                return File(existingResults, "image/png");
            }

            List<DbTables.Place> places = new List<DbTables.Place>(colors);
            foreach (var s in splatCollection)
                places.Add(new DbTables.Place() { ElementGeometry = s.Value.explored, StyleName = s.Key.ToString() });

            ImageStats info = new ImageStats(plusCode);
            var paintOps = MapTileSupport.GetPaintOpsForPlaces(places, "splatter", info);
            var mapTile = MapTileSupport.MapTiles.DrawAreaAtSize(info, paintOps);
            MapTileSupport.SaveMapTile(plusCode, "splatter", mapTile);

            return File(mapTile, "image/png");
        }

        [HttpPut]
        [Route("/[controller]/Enter/{plusCode}")]
        public int Enter(string plusCode)
        {
            Response.Headers.Add("X-noPerfTrack", "Splatter/Enter/VARSREMOVED");
            //A user has entered a space, grant them a point to use to Splat later.
            SimpleLockable.PerformWithLock("splatEnter-" + accountId, () =>
            {
                var recent = GenericData.GetSecurePlayerData<RecentActivityTracker>(accountId, "recentSplat", password);
                if (recent == null)
                    recent = new RecentActivityTracker();

                if (recent.IsRecent(plusCode))
                {
                    GenericData.IncrementPlayerData(accountId, "splatPoints", 1);
                    GenericData.SetSecurePlayerDataJson(accountId, "recentSplat", recent, password);
                }
            });

            return GenericData.GetPlayerData(accountId, "splatPoints").ToUTF8String().ToInt();

        }

        [HttpGet]
        [Route("/[controller]/Test/{plusCode8}")]
        public ActionResult TestShapes(string plusCode8)
        {
            Response.Headers.Add("X-noPerfTrack", "Splatter/Test/VARSREMOVED");
            byte[] results = Array.Empty<byte>();

            if (!PraxisAuthentication.IsAdmin(accountId))
                return File(results, "image/png");

            var mapTile1 = MapTileSupport.DrawPlusCode(plusCode8);
            var possiblePoints = plusCode8.GetSubCells();

            List<DbTables.Place> places = new List<DbTables.Place>();

            int splatCount = 48;
            for (int i = 0; i < splatCount; i++)
            {
                var thisPoint = possiblePoints.PickOneRandom();
                var color = Random.Shared.Next(DemoStyles.splatterStyle.Count - 2); //-2, to exclude background.
                possiblePoints.Remove(thisPoint);
                var splat = MakeSplatShape(thisPoint.ToGeoArea().ToPoint(), Random.Shared.Next(2, 7) * .0001);
                for (int j = 0; j < colors; j++)
                {
                    if (color == j)
                        splatCollection[j].AddGeometry(splat);
                    else
                        splatCollection[j].RemoveGeometry(splat);
                }
            }

            foreach (var splat in splatCollection)
            {
                GenericData.SetGlobalDataJson("splat-" + splat.Key, splat.Value);
                places.Add(new DbTables.Place() { DrawSizeHint = 1, ElementGeometry = splat.Value.explored, StyleName = splat.Key.ToString() });
            }

            var stats = new ImageStats(plusCode8);
            var paintOps = MapTileSupport.GetPaintOpsForPlaces(places, "splatter", stats);
            var overlay = MapTileSupport.MapTiles.DrawAreaAtSize(stats, paintOps);

            results = MapTileSupport.MapTiles.LayerTiles(stats, mapTile1, overlay);

            return File(results, "image/png");
        }

        private static Geometry MakeSplatShapeSimple(Point p, double radius) //~6ms average
        {
            //The lazy option for this: a few random circles.
            List<Geometry> geometries = new List<Geometry>();
            var randCount = Random.Shared.Next(5, 9);
            var bufferSize = radius / 3;
            for (int i = 0; i < randCount; i++)
            {
                var randomMoveX = ((Random.Shared.Next() % 2 == 0 ? 1 : -1) * radius * (Random.Shared.NextDouble()));
                var randomMoveY = ((Random.Shared.Next() % 2 == 0 ? 1 : -1) * radius * (Random.Shared.NextDouble()));
                var workPoint = new Point(p.X + randomMoveX, p.Y + randomMoveY);
                workPoint.Buffer(bufferSize);
                geometries.Add(workPoint);
            }

            var resultGeo = NetTopologySuite.Operation.Union.CascadedPolygonUnion.Union(geometries);
            return resultGeo;
        }

        private static Geometry MakeSplatShape(Point p, double radius) //12ms average
        {
            //Do some geometry actions to make a shape to put on the map.
            List<Geometry> geometries = new List<Geometry>(15);

            //Step 1: fill in the middle half of the radius.
            var workPoint = new Point(p.X, p.Y);
            var centerGeo = workPoint.Buffer(radius / 2);
            geometries.Add(centerGeo);

            //Step 2: Add some bulges around it for asymmetry. 30% of the radius in size, centered between 15-35% of the radius. The center will always be inside centerGeo
            var randCount = Random.Shared.Next(3, 8);
            for (int i = 0; i < randCount; i++)
            {
                var randomMoveX = ((Random.Shared.Next() % 2 == 0 ? 1 : -1) * radius * (Random.Shared.Next(15, 35)) / 100);
                var randomMoveY = ((Random.Shared.Next() % 2 == 0 ? 1 : -1) * radius * (Random.Shared.Next(15, 35)) / 100);
                workPoint = new Point(p.X + randomMoveX, p.Y + randomMoveY);
                var bulgeGeo = workPoint.Buffer(radius / 3);
                geometries.Add(bulgeGeo);
            }

            //Step 3: add some distant circles, and connect them to the center with a triangle.  sized 20% of the radius, centered between 50-100% of the radius
            randCount = Random.Shared.Next(3, 6);
            for (int i = 0; i < randCount; i++)
            {
                var randomMoveX = ((Random.Shared.Next() % 2 == 0 ? 1 : -1) * radius * (Random.Shared.Next(50, 100)) / 100);
                var randomMoveY = ((Random.Shared.Next() % 2 == 0 ? 1 : -1) * radius * (Random.Shared.Next(50, 100)) / 100);
                workPoint = new Point(p.X + randomMoveX, p.Y + randomMoveY);
                var outerCircle = workPoint.Buffer(radius / 5);
                geometries.Add(outerCircle);

                Point t1, t2;
                //draw triangle connecting outerCircle to center. Could use trig to grab points for slightly nicer triangles, this is easier math.
                if (Math.Abs(randomMoveX) > Math.Abs(randomMoveY))
                {
                    //use north/south
                    t1 = new Point(outerCircle.Centroid.X, outerCircle.EnvelopeInternal.MaxY);
                    t2 = new Point(outerCircle.Centroid.X, outerCircle.EnvelopeInternal.MinY);
                }
                else
                {
                    //use east/west
                    t1 = new Point(outerCircle.EnvelopeInternal.MaxX, outerCircle.Centroid.Y);
                    t2 = new Point(outerCircle.EnvelopeInternal.MinX, outerCircle.Centroid.Y);
                }

                var connector = Singletons.geometryFactory.CreatePolygon(new Coordinate[] {
                    new Coordinate(centerGeo.Centroid.X, centerGeo.Centroid.Y),
                    new Coordinate(t1.X, t1.Y),
                    new Coordinate(t2.X, t2.Y),
                    new Coordinate(centerGeo.Centroid.X, centerGeo.Centroid.Y)
                });
                geometries.Add(connector);
            }

            var resultGeo = NetTopologySuite.Operation.Union.CascadedPolygonUnion.Union(geometries);  //gets reduced when added to geometryTracker
            return resultGeo;
        }
    }
}