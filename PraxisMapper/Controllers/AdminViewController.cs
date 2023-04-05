using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using PraxisCore;
using PraxisCore.Support;
using PraxisMapper.Classes;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PraxisMapper.Controllers {
    [Route("[controller]")]
    public class AdminViewController : Controller {
        private readonly IConfiguration Configuration;
        private IMapTiles MapTiles;
        public AdminViewController(IConfiguration config, IMapTiles mapTiles) {
            Configuration = config;
            MapTiles = mapTiles;
        }
        public override void OnActionExecuting(ActionExecutingContext context) {
            base.OnActionExecuting(context);
            PraxisAuthentication.GetAuthInfo(Response, out var accountId, out var _);
            if (!PraxisAuthentication.IsAdmin(accountId) && !HttpContext.Request.Host.IsLocalIpAddress())
                HttpContext.Abort();
        }

        [HttpGet]
        [Route("/[controller]")]
        [Route("/[controller]/Index")]
        public IActionResult Index() {
            return View();
        }

        [HttpGet]
        [Route("/[controller]/GetMapTileInfo/{zoom}/{x}/{y}")]
        public ActionResult GetMapTileInfo(int x, int y, int zoom) {
            //Draw the map tile, with extra info to send over.
            ImageStats istats = new ImageStats(zoom, x, y, MapTileSupport.SlippyTileSizeSquare);

            System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
            sw.Start();
            var places = Place.GetPlaces(istats.area);
            var tile = MapTiles.DrawAreaAtSize(istats, places);
            sw.Stop();

            ViewBag.placeCount = places.Count;
            ViewBag.timeToDraw = sw.Elapsed.ToString();
            ViewBag.imageString = "data:image/png;base64," + Convert.ToBase64String(tile);

            return View();
        }

        [Route("/[controller]/GetAreaInfo/{plusCode}")]
        [Route("/[controller]/GetAreaInfo/{plusCode}/{filterSize}")]
        public ActionResult GetAreaInfo(string plusCode, int filterSize = -1) {
            ViewBag.areaName = plusCode;
            System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
            var mapArea = plusCode.ToGeoArea();
            int imageMaxEdge = Configuration["imageMaxSide"].ToInt();
            long maxImagePixels = Configuration["maxImagePixels"].ToLong();

            ImageStats istats = new ImageStats(mapArea, (int)(mapArea.LongitudeWidth / ConstantValues.resolutionCell11Lon) * (int)MapTileSupport.GameTileScale, (int)(mapArea.LatitudeHeight / ConstantValues.resolutionCell11Lat) * (int)MapTileSupport.GameTileScale);
            istats = MapTileSupport.ScaleBoundsCheck(istats, imageMaxEdge, maxImagePixels);

            if (filterSize >= 0)
                istats.filterSize = filterSize;

            //MapTile data
            sw.Start();
            var tile = MapTiles.DrawAreaAtSize(istats);
            sw.Stop();
            ViewBag.timeToDraw = sw.Elapsed;

            //Reload all elements, to get accurate counts on what wasn't drawn.
            sw.Restart();
            var places = Place.GetPlaces(mapArea, filterSize: 0, skipGeometry: true);
            sw.Stop();
            ViewBag.loadTime = sw.Elapsed;
            ViewBag.placeCount = places.Count;
            var grouped = places.GroupBy(p => p.StyleName);

            ViewBag.imageString = "data:image/png;base64," + Convert.ToBase64String(tile);
            ViewBag.areasByType = "";

            string areasByType = "";
            foreach (var g in grouped)
                areasByType += g.Key + ": " + g.Count() + "<br />";

            ViewBag.areasByType = areasByType;
            places.Clear();
            places = null;

            return View();
        }


        [HttpGet]
        [Route("/[controller]/GetPlaceInfo/{sourceElementId}/{sourceElementType}")]
        public ActionResult GetPlaceInfo(long sourceElementId, int sourceElementType) {
            var db = new PraxisContext();
            db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            var area = db.Places.Include(e => e.Tags).FirstOrDefault(e => e.SourceItemID == sourceElementId && e.SourceItemType == sourceElementType);
            if (area == null)
                return View();

            int imageMaxEdge = Configuration["imageMaxSide"].ToInt();
            long maxImagePixels = Configuration["maxImagePixels"].ToLong();

            TagParser.ApplyTags(new List<DbTables.Place>() { area }, "mapTiles");
            ViewBag.areaname = TagParser.GetName(area);
            ViewBag.type = area.StyleName;
            ViewBag.geoType = area.ElementGeometry.GeometryType;
            ViewBag.tags = String.Join(", ", area.Tags.Select(t => t.Key + ":" + t.Value));

            var geoarea = area.ElementGeometry.Envelope.ToGeoArea().PadGeoArea(ConstantValues.resolutionCell10);

            ImageStats istats = new ImageStats(geoarea, (int)(geoarea.LongitudeWidth / ConstantValues.resolutionCell11Lon) * (int)MapTileSupport.GameTileScale, (int)(geoarea.LatitudeHeight / ConstantValues.resolutionCell11Lat) * (int)MapTileSupport.GameTileScale);
            istats = MapTileSupport.ScaleBoundsCheck(istats, imageMaxEdge, maxImagePixels);

            System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
            sw.Start();
            var places = Place.GetPlaces(istats.area);
            var tile = MapTiles.DrawAreaAtSize(istats, places); 
            ViewBag.UseSvg = false;
            sw.Stop();

            ViewBag.imageString = "data:image/png;base64," + Convert.ToBase64String(tile);
            ViewBag.timeToDraw = sw.Elapsed;
            ViewBag.placeCount = 0;
            ViewBag.areasByType = "";

            ViewBag.placeCount = places.Count;
            var grouped = places.GroupBy(p => p.StyleName);
            string areasByType = "";
            foreach (var g in grouped)
                areasByType += g.Key + ": " + g.Count() + "<br />";

            ViewBag.areasByType = areasByType;

            return View();
        }

        [HttpGet]
        [Route("/[controller]/GetPlaceInfo/{privacyId}/")]
        public ActionResult GetPlaceInfo(Guid privacyId) {
            var db = new PraxisContext();
            db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            var area = db.Places.Include(e => e.Tags).FirstOrDefault(e => e.PrivacyId == privacyId);
            if (area != null)
                return GetPlaceInfo(area.SourceItemID, area.SourceItemType);

            return null;
        }

        [HttpGet]
        [Route("/[controller]/EditData")]
        public ActionResult EditData() {
            //TODO: break these out into separate views when ready.
            Models.EditData model = new Models.EditData();
            var db = new PraxisContext();
            db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            model.accessKey = "?PraxisAuthKey=" + Configuration["serverAuthKey"];
            model.globalDataKeys = db.GlobalData.Select(g => new SelectListItem(g.DataKey, g.DataValue.ToUTF8String())).ToList();
            model.playerKeys = db.PlayerData.Select(p => p.accountId).Distinct().Select(g => new SelectListItem(g, g)).ToList();

            model.stylesetKeys = db.StyleEntries.Select(t => t.StyleSet).Distinct().Select(t => new SelectListItem(t, t)).ToList();
            model.stylesetKeys.Insert(0, new SelectListItem("", ""));

            return View(model);
        }

        [HttpGet]
        [Route("/[controller]/EditGeography")]
        public ActionResult EditGeography() {
            return View();
        }

        [HttpGet]
        [Route("/[controller]/StyleTest")]
        public ActionResult StyleTest() {
            List<byte[]> previews = new List<byte[]>();
            List<string> names = new List<string>();
            //this is already on MapTile controller, might move it. or expand it to show all styles.
            foreach (var styleDataKVP in TagParser.allStyleGroups) {
                var styleData = styleDataKVP.Value.ToList();
                //Draw style as an X by X grid of circles, where X is square root of total sets
                int gridSize = (int)Math.Ceiling(Math.Sqrt(styleData.Count));

                ImageStats stats = new ImageStats("234567"); //Constructor is ignored, all the values are overridden.
                stats.imageSizeX = gridSize * 60;
                stats.imageSizeY = gridSize * 60;
                stats.degreesPerPixelX = stats.area.LongitudeWidth / stats.imageSizeX;
                stats.degreesPerPixelY = stats.area.LatitudeHeight / stats.imageSizeY;
                var circleSize = stats.degreesPerPixelX * 25;

                List<CompletePaintOp> testCircles = new List<CompletePaintOp>();

                var spacingX = stats.area.LongitudeWidth / gridSize;
                var spacingY = stats.area.LatitudeHeight / gridSize;

                for (int x = 0; x < gridSize; x++)
                    for (int y = 0; y < gridSize; y++) {
                        var index = (y * gridSize) + x;
                        if (index < styleData.Count) {
                            var circlePosX = stats.area.WestLongitude + (spacingX * .5) + (spacingX * x);
                            var circlePosY = stats.area.NorthLatitude - (spacingY * .5) - (spacingY * y);
                            var circle = new NetTopologySuite.Geometries.Point(circlePosX, circlePosY).Buffer(circleSize);
                            foreach (var op in styleData[index].Value.PaintOperations) {
                                var entry = new CompletePaintOp() { paintOp = op, elementGeometry = circle, lineWidthPixels = 3 };
                                testCircles.Add(entry);
                            }
                        }
                    }

                var test = MapTiles.DrawAreaAtSize(stats, testCircles);
                previews.Add(test);
                names.Add(styleDataKVP.Key);
            }
            ViewBag.previews = previews;
            ViewBag.names = names;

            return View();
        }

        [HttpGet]
        [Route("/[controller]/ExpireTiles")]
        public IActionResult ExpireTiles()
        {
            var db = new PraxisContext();
            db.ExpireAllMapTiles();
            db.ExpireAllSlippyMapTiles();

            return Index();
        }

        [HttpGet]
        [Route("/[controller]/ResetStyles")]
        public IActionResult ResetStyles()
        {
            var db = new PraxisContext();
            db.ResetStyles();
            TagParser.Initialize(false, MapTileSupport.MapTiles);
            db.ExpireAllMapTiles();
            db.ExpireAllSlippyMapTiles();
            return Index();
        }
    }
}