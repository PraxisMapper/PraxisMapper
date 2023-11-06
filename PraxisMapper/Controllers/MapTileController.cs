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

        private void FinishMapTile(byte[] tiledata, string code, string styleSet)
        {
            if (SaveMapTiles())
            {
                var currentGen = MapTileSupport.SaveMapTile(code, styleSet, tiledata);
                cache.Set("gen" + code + styleSet, currentGen);
            }
        }

        [HttpGet]
        [Route("/[controller]/DrawSlippyTile/{styleSet}/{zoom}/{x}/{y}.png")] //slippy map conventions.
        [Route("/[controller]/Slippy/{styleSet}/{zoom}/{x}/{y}.png")] //slippy map conventions.
        [Route("/[controller]/Slippy/{styleSet}/{onlyLayer}/{zoom}/{x}/{y}.png")] //slippy map conventions.
        public ActionResult DrawSlippyTile(int zoom, int x, int y, string styleSet, string onlyLayer = "") {
            try {
                Response.Headers.Add("X-noPerfTrack", "Maptiles/Slippy/" + styleSet + "/VARSREMOVED");
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

                //Make tile
                var places = GetPlaces(info.area, null, styleSet: styleSet, dataKey: styleSet); //If pre-tag is on, this makes it a lot faster.
                if (onlyLayer != "") //TODO: could boost this into the same GetPlaces call as the dataValue
                    places = places.Where(p => p.StyleName == onlyLayer).ToList();
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

        //AreaData is already very likely to be a single 'layer' of elements and shouldn't take the onlyLayer extension above.
        [HttpGet]
        [Route("/[controller]/DrawSlippyTileAreaData/{styleSet}/{zoom}/{x}/{y}.png")] //slippy map conventions.
        [Route("/[controller]/SlippyAreaData/{styleSet}/{zoom}/{x}/{y}.png")] //slippy map conventions.
        public ActionResult DrawSlippyTileAreaData(int zoom, int x, int y, string styleSet) {
            try {
                Response.Headers.Add("X-noPerfTrack", "Maptiles/SlippyAreaData/" + styleSet + "/VARSREMOVED");
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

        [HttpGet]
        [Route("/[controller]/DrawPlusCode/{code}/{styleSet}")]
        [Route("/[controller]/DrawPlusCode/{code}")]
        [Route("/[controller]/Area/{code}/{styleSet}/{onlyLayer}")]
        [Route("/[controller]/Area/{code}/{styleSet}")]
        [Route("/[controller]/Area/{code}")]
        public ActionResult DrawTile(string code, string styleSet = "mapTiles", string onlyLayer = "") {
            Response.Headers.Add("X-noPerfTrack", "Maptiles/Area/" + styleSet + "/VARSREMOVED");
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
                //tileData = MapTiles.DrawAreaAtSize(info, null, styleSet);
                var places = GetPlaces(info, null, styleSet);
                if (onlyLayer != "")
                    places = places.Where(p => p.StyleName == onlyLayer).ToList();
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

        [HttpGet]
        [Route("/[controller]/AreaData/{code}/{styleSet}")]
        [Route("/[controller]/AreaData/{code}")]
        public ActionResult DrawTileAreaData(string code, string styleSet = "mapTiles") {
            Response.Headers.Add("X-noPerfTrack", "Maptiles/AreaData/" + styleSet + "/VARSREMOVED");
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

        [HttpPut]
        [Route("/[controller]/ExpireTiles/{elementId}/{styleSet}")]
        [Route("/[controller]/Expire/{elementId}/{styleSet}")]
        public void ExpireTiles(Guid elementId, string styleSet) {
            Response.Headers.Add("X-noPerfTrack", "Maptiles/Expire/VARSREMOVED");
            using var db = new PraxisContext();
            db.ExpireMapTiles(elementId, styleSet);
        }

        [HttpGet]
        [Route("/[controller]/GetTileGenerationId/{plusCode}/{styleSet}")]
        [Route("/[controller]/Generation/{plusCode}/{styleSet}")]
        public long GetTileGenerationId(string plusCode, string styleSet) {
            //Returns generationID on the tile on the server
            //if value is *more* than previous value, client should refresh it.
            //if value is equal to previous value, tile has not changed.
            //As is, the client will probably download map tiles twice on change. Once when its expired and being redrawn (-1 return value),
            //and once when the generationID value is incremented from the previous value.
            //Avoiding that might require an endpoint for 'please draw this tile' that returns true or false rather than the actual maptile.
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

        [HttpGet]
        [Route("/[controller]/GetSlippyTileGenerationId/{x}/{y}/{zoom}/{styleSet}")]
        [Route("/[controller]/Generation/{zoom}/{x}/{y}/{styleSet}")]
        public long GetSlippyTileGenerationId(string x, string y, string zoom, string styleSet) {
            //Returns generationID on the tile on the server
            //if value is *more* than previous value, client should refresh it.
            //if value is equal to previous value, tile has not changed.
            Response.Headers.Add("X-noPerfTrack", "Maptiles/SlippyGeneration/" + styleSet + "/VARSREMOVED");
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
