using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using PraxisCore;
using PraxisCore.Support;
using PraxisMapper.Classes;
using System;
using System.Collections.Generic;
using System.Linq;
using static PraxisCore.Place;

namespace PraxisMapper.Controllers
{
    [Route("[controller]")]
    [ApiController]
    public class MapTileController : Controller {
        private readonly IConfiguration Configuration;
        private static IMemoryCache cache;
        private IMapTiles MapTiles;

        public MapTileController(IConfiguration configuration, IMemoryCache memoryCacheSingleton, IMapTiles mapTile) {
            Configuration = configuration;
            cache = memoryCacheSingleton;
            MapTiles = mapTile;
        }

        public override void OnActionExecuting(ActionExecutingContext context) {
            base.OnActionExecuting(context);
            if (Configuration.GetValue<bool>("enableMapTileEndpoints") == false)
                HttpContext.Abort();
        }

        private bool SaveMapTiles() {
            return Configuration.GetValue<bool>("saveMapTiles");
        }

        private byte[] FinishSlippyMapTile(ImageStats info, List<CompletePaintOp> paintOps, string tileKey, string styleSet) {
            byte[] results = null;
            results = MapTiles.DrawAreaAtSize(info, paintOps);

            if (SaveMapTiles())
            {
                var currentGen = MapTileSupport.SaveSlippyMapTile(info, tileKey, styleSet, results);
                cache.Set("gen" + tileKey + styleSet, currentGen);
            }
            return results;
        }

        private byte[] FinishMapTile(ImageStats info, List<CompletePaintOp> paintOps, string code, string styleSet) {
            byte[] results = MapTiles.DrawAreaAtSize(info, paintOps);

            if (SaveMapTiles()) {
                var currentGen = MapTileSupport.SaveMapTile(code, styleSet, results);
                cache.Set("gen" + code + styleSet, currentGen);
            }

            return results;
        }

        /// <summary>
        /// Get the Slippy tile for the given area of the map
        /// </summary>
        /// <param name="zoom">The Slippy zoom level. Each number doubles the number of tiles required to cover the full map.</param>
        /// <param name="x">the Slippy x coordinate</param>
        /// <param name="y">the Slippy y coordinate</param>
        /// <param name="styleSet">Which StyleSet to use when drawing the map tile. Defaults to 'mapTiles'</param>
        /// <param name="onlyLayer">If provided, only draws elements that match this type in the StyleSet</param>
        /// <returns>The Slippy maptile for the requested area per the server's drawing settings.</returns>
        /// <remarks>Slippy tiles are the Google Maps style tiles. Most clients will prefer to use the default game map tiles, that cover 
        /// a Cell8 sized PlusCode specifically.
        /// If the route with no parameters is called, they will be loaded from the body.</remarks>
        [HttpGet]
        [Route("/[controller]/DrawSlippyTile/{styleSet}/{zoom}/{x}/{y}.png")] //slippy map conventions.
        [Route("/[controller]/Slippy/{styleSet}/{zoom}/{x}/{y}.png")] //slippy map conventions.
        [Route("/[controller]/Slippy/{styleSet}/{onlyLayer}/{zoom}/{x}/{y}.png")] //slippy map conventions.
        [Route("/[controller]/Slippy/{styleSet}/{onlyLayer}/{skipType}/{zoom}/{x}/{y}.png")] //slippy map conventions.
        [Route("/[controller]/Slippy/")] //slippy map conventions.
        public ActionResult DrawSlippyTile(int zoom = -1, int x = -1, int y = -1, string styleSet = null, string onlyLayer = null) {
            try {
                Response.Headers.Add("X-noPerfTrack", "Maptiles/Slippy/" + styleSet + "/VARSREMOVED");

                if (styleSet == null)
                {
                    var data = Request.ReadBody();
                    var decoded = GenericData.DeserializeAnonymousType(data, new { zoom = -1, x = -1, y = -1, styleSet = "mapTiles", onlyLayer = "" });
                    zoom = decoded.zoom;
                    x = decoded.x;
                    y = decoded.y;
                    styleSet = decoded.styleSet == "" ? null : decoded.styleSet;
                    onlyLayer = decoded.onlyLayer == "" ? null : decoded.onlyLayer;
                    //skipType = decoded.skipType == "" ? null : decoded.skipType;
                }

                string tileKey = x.ToString() + "|" + y.ToString() + "|" + zoom.ToString() + onlyLayer;
                var info = new ImageStats(zoom, x, y, MapTileSupport.SlippyTileSizeSquare);
                info = MapTileSupport.ScaleBoundsCheck(info, Configuration["imageMaxSide"].ToInt(), Configuration["maxImagePixels"].ToLong());

                if (!DataCheck.IsInBounds(info.area)) {
                    Response.Headers.Add("X-notes", "OOB");
                    return StatusCode(500);
                }

                byte[] tileData = MapTileSupport.GetExistingSlippyTile(tileKey, styleSet);
                if (tileData != null) {
                    Response.Headers.Add("X-notes", "cached");
                    return File(tileData, "image/png");
                }

                //Make tile. NEW: skipTags because we're relying on PlaceData now instead.
                var skipTags = !TagParser.allStyleGroups[styleSet].Any(s => s.Value.PaintOperations.Any(o => o.StaticColorFromName)); //These needs tags for name.
                var places = GetPlaces(info.area, null, skipTags:skipTags, styleSet: styleSet, filterSize: info.filterSize, dataKey: styleSet, dataValue: onlyLayer); 
                var paintOps = MapTileSupport.GetPaintOpsForPlaces(places, styleSet, info);
                tileData = FinishSlippyMapTile(info, paintOps, tileKey, styleSet);

                Response.Headers.Add("X-notes", Configuration.GetValue<string>("MapTilesEngine"));
                return File(tileData, "image/png");
            }
            catch (Exception ex) {
                ErrorLogger.LogError(ex);
                return StatusCode(500);
            }
        }

        /// <summary>
        /// Draws a Slippy tile, using PlusCode terrain values instead of drawing detailed Places.
        /// </summary>
        /// <param name="zoom">The Slippy zoom level. Each number doubles the number of tiles required to cover the full map.</param>
        /// <param name="x">the Slippy x coordinate</param>
        /// <param name="y">the Slippy y coordinate</param>
        /// <param name="styleSet">Which StyleSet to use when drawing the map tile. Defaults to 'mapTiles'</param>
        /// <returns>The Slippy map tile showing Area data instead of Places.</returns>
        /// <remarks> It will search if any PlusCodes have a key-value pair matching the style set, and draw those that do. 
        /// This is probably not what you actually want. This may be a legacy function that shouldn't be active.</remarks>
        [HttpGet]
        [Route("/[controller]/DrawSlippyTileAreaData/{styleSet}/{zoom}/{x}/{y}.png")] //slippy map conventions.
        [Route("/[controller]/SlippyAreaData/{styleSet}/{zoom}/{x}/{y}.png")] //slippy map conventions.
        [Route("/[controller]/SlippyAreaData/")]
        public ActionResult DrawSlippyTileAreaData(int zoom = -1, int x = -1, int y = -1, string styleSet = null) {
            try {
                Response.Headers.Add("X-noPerfTrack", "Maptiles/SlippyAreaData/" + styleSet + "/VARSREMOVED");

                if (styleSet == null)
                {
                    var data = Request.ReadBody();
                    var decoded = GenericData.DeserializeAnonymousType(data, new { zoom = -1, x = -1, y = -1, styleSet = "mapTiles"});
                    zoom = decoded.zoom;
                    x = decoded.x;
                    y = decoded.y;
                    styleSet = decoded.styleSet == "" ? null : decoded.styleSet;
                }

                string tileKey = x.ToString() + "|" + y.ToString() + "|" + zoom.ToString();
                var info = new ImageStats(zoom, x, y, MapTileSupport.SlippyTileSizeSquare);
                info = MapTileSupport.ScaleBoundsCheck(info, Configuration["imageMaxSide"].ToInt(), Configuration["maxImagePixels"].ToLong());

                if (!DataCheck.IsInBounds(info.area)) {
                    Response.Headers.Add("X-notes", "OOB");
                    return StatusCode(500);
                }

                byte[] tileData = MapTileSupport.GetExistingSlippyTile(tileKey, styleSet);
                if (tileData != null) {
                    Response.Headers.Add("X-notes", "cached");
                    return File(tileData, "image/png");
                }

                //Make tile
                var paintOps = MapTileSupport.GetPaintOpsForAreas(styleSet, info);
                tileData = FinishSlippyMapTile(info, paintOps, tileKey, styleSet);

                Response.Headers.Add("X-notes", Configuration.GetValue<string>("MapTilesEngine"));
                return File(tileData, "image/png");
            }
            catch (Exception ex) {
                ErrorLogger.LogError(ex);
                return StatusCode(500);
            }
        }

        /// <summary>
        /// Draws the given PlusCode as an image.
        /// </summary>
        /// <param name="code">the PlusCode to draw. Can be any valid PlusCode size.</param>
        /// <param name="styleSet">The StyleSet to draw in. Defaults to 'mapTiles'</param>
        /// <param name="onlyLayer">If provided, only draws the elements that match this StyleSet type.</param>
        /// <param name="skipType">If provided, Places with a key matching this value will not be included in the drawing.</param>
        /// <returns>A maptile for the given PlusCode area with the given parameters, according to the server's drawing settings</returns>
        /// <remarks>This is the expected endpoint for the PraxisMapper Godot Client to use when its calling a server instead of drawing tiles locally.
        /// You can call any PlusCode here, but each character pair removed from the code increases the area by 400x. Drawing time varies depending on the number and complexity of places to draw.
        /// For large PlusCodes, the image may be scaled down to match the server's max image settings.
        /// </remarks>
        [HttpGet]
        [Route("/[controller]/DrawPlusCode/{code}/{styleSet}")]
        [Route("/[controller]/DrawPlusCode/{code}")]
        [Route("/[controller]/Area/{code}/{styleSet}/{onlyLayer}")]
        [Route("/[controller]/Area/{code}/{styleSet}/{onlyLayer}/{skipType}")]
        [Route("/[controller]/Area/{code}/{styleSet}")]
        [Route("/[controller]/Area/{code}")]
        [Route("/[controller]/Area/")]
        public ActionResult DrawTile(string code =null, string styleSet = "mapTiles", string onlyLayer = null, string skipType = null) {
            Response.Headers.Add("X-noPerfTrack", "Maptiles/Area/" + styleSet + "/VARSREMOVED");

            if (code == null)
            {
                var data = Request.ReadBody();
                var decoded = GenericData.DeserializeAnonymousType(data, new { code = "", styleSet = "mapTiles", onlyLayer = "", skipType = "" });
                code = decoded.code;
                styleSet = decoded.styleSet == "" ? "mapTiles" : decoded.styleSet;
                onlyLayer = decoded.onlyLayer == "" ? null : decoded.onlyLayer;
                skipType = decoded.skipType == "" ? null : decoded.skipType;
            }

            try {
                var info = new ImageStats(code);
                info = MapTileSupport.ScaleBoundsCheck(info, Configuration["imageMaxSide"].ToInt(), Configuration["maxImagePixels"].ToLong());

                if (!DataCheck.IsInBounds(info.area)) {
                    Response.Headers.Add("X-notes", "OOB");
                    return StatusCode(500);
                }

                byte[] tileData = MapTileSupport.GetExistingTileImage(code, styleSet);
                if (tileData != null) {
                    Response.Headers.Add("X-notes", "cached");
                    return File(tileData, "image/png");
                }

                //Make tile
                var skipTags = !TagParser.allStyleGroups[styleSet].Any(s => s.Value.PaintOperations.Any(o => o.StaticColorFromName)); //These needs tags for name.
                var places = GetPlaces(info.area, null, styleSet:styleSet, skipTags:skipTags, filterSize: info.filterSize, dataKey:styleSet, dataValue: onlyLayer, skipType: skipType);
                var paintOps = MapTileSupport.GetPaintOpsForPlaces(places, styleSet, info);
                tileData = FinishMapTile(info, paintOps, code, styleSet);

                Response.Headers.Add("X-notes", Configuration.GetValue<string>("MapTilesEngine"));
                return File(tileData, "image/png");
            }
            catch (Exception ex) {
                ErrorLogger.LogError(ex);
                return StatusCode(500);
            }
        }

        /// <summary>
        /// Draws the Area data for the given PlusCode
        /// </summary>
        /// <param name="code">the PlusCode to draw</param>
        /// <param name="styleSet">The StyleSet to draw. Defaults to 'mapTiles'</param>
        /// <returns>A map tile showing the Area Data matches for hte style set</returns>
        /// <remarks>This will search if any PlusCodes have a key-value pair matching the style set, and draw those that do.
        /// You probably don't want this, and this might be a very old idea that hasn't been removed yet.</remarks>
        [HttpGet]
        [Route("/[controller]/AreaData/{code}/{styleSet}")]
        [Route("/[controller]/AreaData/{code}")]
        [Route("/[controller]/AreaData/")]
        public ActionResult DrawTileAreaData(string code = null, string styleSet = "mapTiles") {
            Response.Headers.Add("X-noPerfTrack", "Maptiles/AreaData/" + styleSet + "/VARSREMOVED");

            if (code == null)
            {
                var data = Request.ReadBody();
                var decoded = GenericData.DeserializeAnonymousType(data, new { code = "", styleSet = "mapTiles", onlyLayer = "", skipType = "" });
                code = decoded.code;
                styleSet = decoded.styleSet == "" ? "mapTiles" : decoded.styleSet;
            }

            try {
                var info = new ImageStats(code);
                info = MapTileSupport.ScaleBoundsCheck(info, Configuration["imageMaxSide"].ToInt(), Configuration["maxImagePixels"].ToLong());

                if (!DataCheck.IsInBounds(info.area)) {
                    Response.Headers.Add("X-notes", "OOB");
                    return StatusCode(500);
                }

                byte[] tileData = MapTileSupport.GetExistingTileImage(code, styleSet);
                if (tileData != null) {
                    Response.Headers.Add("X-notes", "cached");
                    return File(tileData, "image/png");
                }

                //Make tile
                var paintOps = MapTileSupport.GetPaintOpsForAreas(styleSet, info);
                tileData = FinishMapTile(info, paintOps, code, styleSet);

                Response.Headers.Add("X-notes", Configuration.GetValue<string>("MapTilesEngine"));
                return File(tileData, "image/png");
            }
            catch (Exception ex) {
                ErrorLogger.LogError(ex);
                return StatusCode(500);
            }
        }

        /// <summary>
        /// Tell the server to expire MapTiles that touch the given Place using the given StyleSet
        /// </summary>
        /// <param name="elementId">the privacyID(GUID) of the Place in question</param>
        /// <param name="styleSet">the StyleSet to expire tiles for.</param>
        /// <remarks>If you have data on Places that players can change via gameplay, you will want to call this when it changes. 
        /// It will only expire tiles that contain the place in question, and expired tiles aren't redrawn until a request for them occurs.
        /// Or, if you update geometry data for one place while the server is live, you might want to call this for it. Otherwise, you probably don't need this.</remarks>
        [HttpPut]
        [Route("/[controller]/ExpireTiles/{elementId}/{styleSet}")]
        [Route("/[controller]/Expire/{elementId}/{styleSet}")]
        public void ExpireTiles(Guid elementId, string styleSet) {
            Response.Headers.Add("X-noPerfTrack", "Maptiles/Expire/VARSREMOVED");
            using var db = new PraxisContext();
            db.ExpireMapTiles(elementId, styleSet);
        }

        /// <summary>
        /// Ask the server for the tile's generation ID.
        /// </summary>
        /// <param name="plusCode">The plusCode to check</param>
        /// <param name="styleSet">The StyleSet to check.</param>
        /// <returns>the generation ID of the tile in question</returns>
        /// <remarks>This is intended to help reduce server use/bandwidth on games with tiles that get redrawn frequently.
        /// If the client saves the generationID of the image it downloaded, it can just check for the generationID and only pull the updated tile
        /// if the server's ID is larger. Intended for overlay tiles that might change due to in-game activities frequently.</remarks>
        [HttpGet]
        [Route("/[controller]/GetTileGenerationId/{plusCode}/{styleSet}")]
        [Route("/[controller]/Generation/{plusCode}/{styleSet}")]
        [Route("/[controller]/Generation/")]
        public long GetTileGenerationId(string plusCode = null, string styleSet = null) {
            //Returns generationID on the tile on the server
            //if value is *more* than previous value, client should refresh it.
            //if value is equal to previous value, tile has not changed.
            //As is, the client will probably download map tiles twice on change. Once when its expired and being redrawn (-1 return value),
            //and once when the generationID value is incremented from the previous value.
            //Avoiding that might require an endpoint for 'please draw this tile' that returns true or false rather than the actual maptile.

            if (plusCode == null)
            {
                var data = Request.ReadBody();
                var decoded = GenericData.DeserializeAnonymousType(data, new { plusCode = "", styleSet = ""});
                plusCode = decoded.plusCode;
                styleSet = decoded.styleSet == "" ? null : decoded.styleSet;
            }

            Response.Headers.Add("X-noPerfTrack", "Maptiles/Generation/" + styleSet + "/VARSREMOVED");
            try {
                using var db = new PraxisContext();
                db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
                db.ChangeTracker.AutoDetectChangesEnabled = false;
                long tileGenId = -1;
                var tile = db.MapTiles.FirstOrDefault(m => m.PlusCode == plusCode && m.StyleSet == styleSet);
                if (tile != null && tile.ExpireOn > DateTime.UtcNow)
                    tileGenId = tile.GenerationID;

                return tileGenId;
            }
            catch (Exception ex) {
                ErrorLogger.LogError(ex);
                return -1; //negative answers will be treated as an expiration.
            }
        }

        /// <summary>Ask the server for the Slippy tile's generation ID.</summary>
        /// <param name="x">The Slippy x coordinate.</param>
        /// <param name="y">The Slippy y coordinate.</param>
        /// <param name="zoom">The Slippy zoom level.</param>
        /// <param name="styleSet">The StyleSet to check.</param>
        /// <returns>the generation ID of the Slippy tile in question</returns>
        /// <remarks>This is intended to help reduce server use/bandwidth on games with tiles that get redrawn frequently.
        /// If the client saves the generationID of the image it downloaded, it can just check for the generationID and only pull the updated tile
        /// if the server's ID is larger. Intended for overlay tiles that might change due to in-game activities frequently.</remarks>
        [HttpGet]
        [Route("/[controller]/GetSlippyTileGenerationId/{x}/{y}/{zoom}/{styleSet}")]
        [Route("/[controller]/Generation/{zoom}/{x}/{y}/{styleSet}")]
        public long GetSlippyTileGenerationId(string x = null, string y = null, string zoom = null, string styleSet = null) {
            //Returns generationID on the tile on the server
            //if value is *more* than previous value, client should refresh it.
            //if value is equal to previous value, tile has not changed.
            Response.Headers.Add("X-noPerfTrack", "Maptiles/SlippyGeneration/" + styleSet + "/VARSREMOVED");

            if (styleSet == null)
            {
                var data = Request.ReadBody();
                var decoded = GenericData.DeserializeAnonymousType(data, new { zoom = "", x = "", y = "", styleSet = "mapTiles" });
                zoom = decoded.zoom;
                x = decoded.x;
                y = decoded.y;
                styleSet = decoded.styleSet == "" ? null : decoded.styleSet;
            }

            try {
                using var db = new PraxisContext();
                db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
                db.ChangeTracker.AutoDetectChangesEnabled = false;
                long tileGenId = -1;
                var tile = db.SlippyMapTiles.FirstOrDefault(m => m.Values == x + "|" + y + "|" + zoom && m.StyleSet == styleSet);
                if (tile != null && tile.ExpireOn > DateTime.UtcNow)
                    tileGenId = tile.GenerationID;
                return tileGenId;
            }
            catch (Exception ex) {
                ErrorLogger.LogError(ex);
                return -1; //negative answers will be treated as an expiration.
            }
        }

        /// <summary>
        /// Draws all entries in a StyleSet in 1 image.
        /// </summary>
        /// <param name="styleSet">The Style Set to draw</param>
        /// <returns>An image that shows a sample of the StyleSet</returns>
        /// <remarks>Each entry in the style set will be drawn as a circle. Useful to help see at a glance how distinct each entry is.</remarks>
        [HttpGet]
        [Route("/[controller]/StyleTest/{styleSet}")]
        public ActionResult DrawAllStyleEntries(string styleSet) {
            var styleData = TagParser.allStyleGroups[styleSet].ToList();
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
            return File(test, "image/png");
        }
    }
}
