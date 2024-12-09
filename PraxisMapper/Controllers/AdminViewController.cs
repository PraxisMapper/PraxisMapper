using Microsoft.AspNetCore.Http;
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
using System.ComponentModel;
using System.Linq;
using static System.Runtime.InteropServices.JavaScript.JSType;

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
        [EndpointSummary("Opens the core AdminView page")]
        [EndpointDescription("Not an API endpoint. Opens the View in a browser window. Intended to be opened on localhost rather than a remote system.")]
        public IActionResult Index() {
            return View();
        }

        [HttpGet]
        [Route("/[controller]/GetMapTileInfo/{zoom}/{x}/{y}")]
        [EndpointSummary("Displays some information on the Slippy MapTile requested")]
        [EndpointDescription("Not an API endpoint. Returns the View with the map tile requested, containing some performance-related stats and info on places contained.")]
        public ActionResult GetMapTileInfo([Description("X Slippy coord")]int x,
            [Description("Y Slippy coord")] int y,
            [Description("Zoom level Slippy coord")] int zoom) 
        {
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

        [HttpGet]
        [Route("/[controller]/GetAreaInfo/{plusCode}")]
        [Route("/[controller]/GetAreaInfo/{plusCode}/{filterSize}")]
        [EndpointSummary("Displays some information on the PlusCode area requested")]
        [EndpointDescription("The View with the map tile requested, containing some performance-related stats and info on places contained.")]
        public ActionResult GetAreaInfo([Description("The PlusCode to analyze.")]string plusCode, 
            [Description("Ignore places/elements with an area/length below this value. Use -1 to see all places/elements.")]int filterSize = -1)
        {
            ViewBag.areaName = plusCode;
            System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
            var mapArea = plusCode.ToGeoArea();
            int imageMaxEdge = Configuration["imageMaxSide"].ToInt();
            long maxImagePixels = Configuration["maxImagePixels"].ToLong();

            MapTileSupport.GetPlusCodeImagePixelSize(plusCode, out int X, out int Y);
            ImageStats istats = new ImageStats(mapArea, X, Y);
            istats = MapTileSupport.ScaleBoundsCheck(istats, imageMaxEdge, maxImagePixels);

            if (filterSize <= 0)
                istats.filterSize = 0;

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

        /// <param name="sourceElementId">The OpenStreetMap Id of the Place</param>
        /// <param name="sourceElementType">The OpenStreetMap type of the Place. 1 = Point, 2 = Way, 3 = Relation</param>
        /// <returns></returns>
        [HttpGet]
        [Route("/[controller]/GetPlaceInfo/{sourceElementId}/{sourceElementType}")]
        [EndpointSummary("Displays some information on the Place requested and the surrounding area.")]
        [EndpointDescription("The View with a custom map tile focused on the Place requested, and some performance-related stats and info.")]
        public ActionResult GetPlaceInfo([Description("")]long sourceElementId,
            [Description("")] int sourceElementType) 
        {
            using var db = new PraxisContext();
            db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            var place = db.Places.Include(e => e.Tags).FirstOrDefault(e => e.SourceItemID == sourceElementId && e.SourceItemType == sourceElementType);
            if (place == null)
                return View();

            int imageMaxEdge = Configuration["imageMaxSide"].ToInt();
            long maxImagePixels = Configuration["maxImagePixels"].ToLong();

            TagParser.ApplyTags(new List<DbTables.Place>() { place }, "mapTiles");
            ViewBag.areaname = place.Name;
            ViewBag.type = place.StyleName;
            ViewBag.geoType = place.ElementGeometry.GeometryType;
            ViewBag.tags = System.String.Join(", ", place.Tags.Select(t => t.Key + ":" + t.Value));

            var geoarea = place.ElementGeometry.Envelope.ToGeoArea().PadGeoArea(ConstantValues.resolutionCell10);

            ImageStats istats = new ImageStats(geoarea, (int)(geoarea.LongitudeWidth / ConstantValues.resolutionCell11Lon), (int)(geoarea.LatitudeHeight / ConstantValues.resolutionCell11Lat));
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
        [EndpointSummary("Displays some information on the Place requested and the surrounding area.")]
        [EndpointDescription("The View with a custom map tile focused on the Place requested, and some performance-related stats and info.")]
        public ActionResult GetPlaceInfo([Description("The GUID associated to the Place in this PraxisMapper server")] Guid privacyId) {
            using var db = new PraxisContext();
            db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            var area = db.Places.Include(e => e.Tags).FirstOrDefault(e => e.PrivacyId == privacyId);
            if (area != null)
                return GetPlaceInfo(area.SourceItemID, area.SourceItemType);

            return null;
        }

        [HttpGet]
        [Route("/[controller]/EditData")]
        [EndpointSummary("An endpoint to edit some data live.")]
        [EndpointDescription("The(simple, incomplete) View for editing data")]
        public ActionResult EditData() {
            //TODO: break these out into separate views when ready.
            Models.EditData model = new Models.EditData();
            using var db = new PraxisContext();
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
        [EndpointSummary("A View to edit geography. May be incomplete or nonfunctional")]
        [EndpointDescription("")]
        public ActionResult EditGeography() {
            return View();
        }

        
        //NOTE: This endpoint doesn't function. Use MapTile/StyleTest/{styleSet}
        //[HttpGet]
        //[Route("/[controller]/StyleTest")]
        //[EndpointSummary("A View to get a quick preview of all Styles.")]
        //[EndpointDescription("The response is an image, with each entry in a StyleSet drawn as a circle.")]
        //public ActionResult StyleTest() {
        //    List<byte[]> previews = new List<byte[]>();
        //    List<string> names = new List<string>();
        //    //this is already on MapTile controller, might move it. or expand it to show all styles.
        //    foreach (var styleDataKVP in TagParser.allStyleGroups) {
        //        var styleData = styleDataKVP.Value.ToList();
        //        //Draw style as an X by X grid of circles, where X is square root of total sets
        //        int gridSize = (int)Math.Ceiling(Math.Sqrt(styleData.Count));

        //        ImageStats stats = new ImageStats("234567"); //Constructor is ignored, all the values are overridden.
        //        stats.imageSizeX = gridSize * 60;
        //        stats.imageSizeY = gridSize * 60;
        //        stats.degreesPerPixelX = stats.area.LongitudeWidth / stats.imageSizeX;
        //        stats.degreesPerPixelY = stats.area.LatitudeHeight / stats.imageSizeY;
        //        var circleSize = stats.degreesPerPixelX * 25;

        //        List<CompletePaintOp> testCircles = new List<CompletePaintOp>();

        //        var spacingX = stats.area.LongitudeWidth / gridSize;
        //        var spacingY = stats.area.LatitudeHeight / gridSize;

        //        for (int x = 0; x < gridSize; x++)
        //            for (int y = 0; y < gridSize; y++) {
        //                var index = (y * gridSize) + x;
        //                if (index < styleData.Count) {
        //                    var circlePosX = stats.area.WestLongitude + (spacingX * .5) + (spacingX * x);
        //                    var circlePosY = stats.area.NorthLatitude - (spacingY * .5) - (spacingY * y);
        //                    var circle = new NetTopologySuite.Geometries.Point(circlePosX, circlePosY).Buffer(circleSize);
        //                    foreach (var op in styleData[index].Value.PaintOperations) {
        //                        var entry = new CompletePaintOp() { paintOp = op, elementGeometry = circle, lineWidthPixels = 3 };
        //                        if (op.FromTag)
        //                            entry.tagValue = "999999";
        //                        testCircles.Add(entry);
        //                    }
        //                }
        //            }

        //        var test = MapTiles.DrawAreaAtSize(stats, testCircles);
        //        previews.Add(test);
        //        names.Add(styleDataKVP.Key);
        //    }
        //    ViewBag.previews = previews;
        //    ViewBag.names = names;

        //    return View();
        //}

        /// <summary>
        /// Forces all map tiles on the server to be expired
        /// </summary>
        /// <returns>The base AdminView</returns>
        /// <remarks>This only sets the expiration date, and tiles will be redrawn the next time they are requested.
        /// Should only be necessary when map data is updated</remarks>
        [HttpGet]
        [Route("/[controller]/ExpireTiles")]
        [EndpointSummary("")]
        [EndpointDescription("")]
        public IActionResult ExpireTiles()
        {
            using var db = new PraxisContext();
            db.ExpireAllMapTiles();
            db.ExpireAllSlippyMapTiles();

            return Index();
        }

        /// <summary>
        /// Forces all styles on the database to be reset to defaults
        /// </summary>
        /// <returns>the default AdminView page</returns>
        /// <remarks>This is mostly intended for development while editing styles, if a need to clear out changes occurs</remarks>
        [HttpGet]
        [Route("/[controller]/ResetStyles")]
        [EndpointSummary("")]
        [EndpointDescription("")]
        public IActionResult ResetStyles()
        {
            using var db = new PraxisContext();
            db.ResetStyles();
            TagParser.Initialize(false, MapTileSupport.MapTiles);
            db.ExpireAllMapTiles();
            db.ExpireAllSlippyMapTiles();
            return Index();
        }
    }
}