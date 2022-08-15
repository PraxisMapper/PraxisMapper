using PraxisCore;
using PraxisCore.Support;
using Google.OpenLocationCode;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using NetTopologySuite.Geometries.Prepared;
using PraxisMapper.Classes;
using System;
using System.Linq;
using static PraxisCore.DbTables;
using static PraxisCore.Place;
using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc.Filters;

namespace PraxisMapper.Controllers
{
    [Route("[controller]")]
    [ApiController]
    public class MapTileController : Controller
    {
        private readonly IConfiguration Configuration;
        private static IMemoryCache cache;
        private IMapTiles MapTiles;

        public MapTileController(IConfiguration configuration, IMemoryCache memoryCacheSingleton, IMapTiles mapTile)
        {
            Configuration = configuration;
            cache = memoryCacheSingleton;
            MapTiles = mapTile;
        }

        public override void OnActionExecuting(ActionExecutingContext context)
        {
            base.OnActionExecuting(context);
            if (Configuration.GetValue<bool>("enableMapTileEndpoints") == false)
                HttpContext.Abort();
        }

        private bool SaveMapTiles()
        {
            return Configuration.GetValue<bool>("saveMapTiles");
        }

        public byte[] getExistingSlippyTile(string tileKey, string styleSet)
        {
            if (!SaveMapTiles())
                return null;

            var db = new PraxisContext();
            var existingResults = db.SlippyMapTiles.FirstOrDefault(mt => mt.Values == tileKey && mt.StyleSet == styleSet);
            if (existingResults == null || existingResults.ExpireOn < DateTime.UtcNow)
                return null;

            return existingResults.TileData;
        }

        public byte[] getExistingTile(string code, string styleSet)
        {
            if (!SaveMapTiles())
                return null;

            var db = new PraxisContext();
            var existingResults = db.MapTiles.FirstOrDefault(mt => mt.PlusCode == code && mt.StyleSet == styleSet);
            if (existingResults == null || existingResults.ExpireOn < DateTime.UtcNow)
                return null;

            return existingResults.TileData;
        }

        private byte[] FinishSlippyMapTile(ImageStats info, List<CompletePaintOp> paintOps, string tileKey, string styleSet)
        {
            byte[] results = null;
            results = MapTiles.DrawAreaAtSize(info, paintOps);

            if (SaveMapTiles())
            {
                var db = new PraxisContext();
                var existingResults = db.SlippyMapTiles.FirstOrDefault(mt => mt.Values == tileKey && mt.StyleSet == styleSet);
                if (existingResults == null)
                {
                    existingResults = new SlippyMapTile() { Values = tileKey, StyleSet = styleSet, AreaCovered = Converters.GeoAreaToPolygon(GeometrySupport.MakeBufferedGeoArea(info.area)) };
                    db.SlippyMapTiles.Add(existingResults);
                }

                existingResults.ExpireOn = DateTime.UtcNow.AddYears(10);
                existingResults.TileData = results;
                existingResults.GenerationID++;
                db.SaveChanges();
            }

            return results;
        }

        private byte[] FinishMapTile(ImageStats info, List<CompletePaintOp> paintOps, string code, string styleSet)
        {
            byte[] results = null;
            results = MapTiles.DrawAreaAtSize(info, paintOps);

            if (SaveMapTiles())
            {
                var db = new PraxisContext();
                var existingResults = db.MapTiles.FirstOrDefault(mt => mt.PlusCode == code && mt.StyleSet == styleSet);
                if (existingResults == null)
                {
                    existingResults = new MapTile() { PlusCode = code, StyleSet = styleSet, AreaCovered = Converters.GeoAreaToPolygon(GeometrySupport.MakeBufferedGeoArea(info.area)) };
                    db.MapTiles.Add(existingResults);
                }

                existingResults.ExpireOn = DateTime.UtcNow.AddYears(10);
                existingResults.TileData = results;
                existingResults.GenerationID++;
                db.SaveChanges();
                cache.Set("gen" + code + styleSet, existingResults.GenerationID);
            }

            return results;
        }

        [HttpGet]
        [Route("/[controller]/DrawSlippyTile/{styleSet}/{zoom}/{x}/{y}.png")] //slippy map conventions.
        [Route("/[controller]/Slippy/{styleSet}/{zoom}/{x}/{y}.png")] //slippy map conventions.
        public ActionResult DrawSlippyTile(int zoom, int x, int y, string styleSet)
        {
            try
            {
                string tileKey = x.ToString() + "|" + y.ToString() + "|" + zoom.ToString();
                var info = new ImageStats(zoom, x, y, IMapTiles.SlippyTileSizeSquare);

                if (!DataCheck.IsInBounds(info.area))
                {
                    Response.Headers.Add("X-notes", "OOB");
                    return StatusCode(500);
                }

                byte[] tileData = getExistingSlippyTile(tileKey, styleSet);
                if (tileData != null)
                {
                    Response.Headers.Add("X-notes", "cached");
                    return File(tileData, "image/png");
                }

                //Make tile
                var places = GetPlacesForTile(info, null, styleSet, false);
                var paintOps = MapTileSupport.GetPaintOpsForPlaces(places, styleSet, info);
                tileData = FinishSlippyMapTile(info, paintOps, tileKey, styleSet);

                Response.Headers.Add("X-notes", Configuration.GetValue<string>("MapTilesEngine"));
                return File(tileData, "image/png");
            }
            catch (Exception ex)
            {
                ErrorLogger.LogError(ex);
                return StatusCode(500);
            }
        }

        //This might not be a real useful function? Or it might need a better name.
        [HttpGet]
        [Route("/[controller]/DrawSlippyTileCustomElements/{styleSet}/{dataKey}/{zoom}/{x}/{y}.png")] //slippy map conventions.
        [Route("/[controller]/SlippyCustomElements/{styleSet}/{dataKey}/{zoom}/{x}/{y}.png")] //slippy map conventions.
        [Route("/[controller]/SlippyPlaceData/{styleSet}/{dataKey}/{zoom}/{x}/{y}.png")] //slippy map conventions.
        public ActionResult DrawSlippyTileCustomElements(int x, int y, int zoom, string styleSet, string dataKey)
        {
            try
            {
                string tileKey = x.ToString() + "|" + y.ToString() + "|" + zoom.ToString();
                var info = new ImageStats(zoom, x, y, IMapTiles.SlippyTileSizeSquare);

                if (!DataCheck.IsInBounds(info.area))
                {
                    Response.Headers.Add("X-notes", "OOB");
                    return StatusCode(500);
                }

                byte[] tileData = getExistingSlippyTile(tileKey, styleSet);
                if (tileData != null)
                {
                    Response.Headers.Add("X-notes", "cached");
                    return File(tileData, "image/png");
                }

                //Make tile
                var places = GetPlacesForTile(info, null, styleSet);
                var paintOps = MapTileSupport.GetPaintOpsForPlacesData(dataKey, styleSet, info);
                tileData = FinishSlippyMapTile(info, paintOps, tileKey, styleSet);

                Response.Headers.Add("X-notes", Configuration.GetValue<string>("MapTilesEngine"));
                return File(tileData, "image/png");
            }
            catch (Exception ex)
            {
                ErrorLogger.LogError(ex);
                return StatusCode(500);
            }
        }

        [HttpGet]
        [Route("/[controller]/DrawSlippyTileCustomPlusCodes/{styleSet}/{dataKey}/{zoom}/{x}/{y}.png")] //slippy map conventions.
        [Route("/[controller]/SlippyAreaData/{styleSet}/{dataKey}/{zoom}/{x}/{y}.png")] //slippy map conventions.
        public ActionResult DrawSlippyTileCustomPlusCodes(int x, int y, int zoom, string styleSet, string dataKey)
        {
            try
            {
                string tileKey = x.ToString() + "|" + y.ToString() + "|" + zoom.ToString();
                var info = new ImageStats(zoom, x, y, IMapTiles.SlippyTileSizeSquare);

                if (!DataCheck.IsInBounds(info.area))
                {
                    Response.Headers.Add("X-notes", "OOB");
                    return StatusCode(500);
                }

                byte[] tileData = getExistingSlippyTile(tileKey, styleSet);
                if (tileData != null)
                {
                    Response.Headers.Add("X-notes", "cached");
                    return File(tileData, "image/png");
                }

                //Make tile
                var places = GetPlacesForTile(info, null, styleSet);
                var paintOps = MapTileSupport.GetPaintOpsForAreaData(dataKey, styleSet, info);
                tileData = FinishSlippyMapTile(info, paintOps, tileKey, styleSet);

                Response.Headers.Add("X-notes", Configuration.GetValue<string>("MapTilesEngine"));
                return File(tileData, "image/png");
            }
            catch (Exception ex)
            {
                ErrorLogger.LogError(ex);
                return StatusCode(500);
            }
        }

        [HttpGet]
        [Route("/[controller]/DrawPath")]
        public byte[] DrawPath()
        {
            //NOTE: URL limitations block this from being a usable REST style path, so this one may require reading data bindings from the body instead
            string path = new System.IO.StreamReader(Request.Body).ReadToEnd();
            return MapTiles.DrawUserPath(path);
        }

        [HttpGet]
        [Route("/[controller]/DrawPlusCode/{code}/{styleSet}")]
        [Route("/[controller]/DrawPlusCode/{code}")]
        [Route("/[controller]/Area/{code}/{styleSet}")]
        [Route("/[controller]/Area/{code}")]
        public ActionResult DrawTile(string code, string styleSet = "mapTiles")
        {
            try
            {
                var info = new ImageStats(code);

                if (!DataCheck.IsInBounds(info.area))
                {
                    Response.Headers.Add("X-notes", "OOB");
                    return StatusCode(500);
                }

                byte[] tileData = getExistingTile(code, styleSet);
                if (tileData != null)
                {
                    Response.Headers.Add("X-notes", "cached");
                    return File(tileData, "image/png");
                }

                //Make tile
                var places = GetPlacesForTile(info, null, styleSet, false);
                var paintOps = MapTileSupport.GetPaintOpsForPlaces(places, styleSet, info);
                tileData = FinishMapTile(info, paintOps, code, styleSet);

                Response.Headers.Add("X-notes", Configuration.GetValue<string>("MapTilesEngine"));
                return File(tileData, "image/png");
            }
            catch (Exception ex)
            {
                ErrorLogger.LogError(ex);
                return StatusCode(500);
            }
        }

        [HttpGet]
        [Route("/[controller]/DrawPlusCodeCustomData/{code}/{styleSet}/{dataKey}")]
        [Route("/[controller]/AreaData/{code}/{styleSet}/{dataKey}")]
        public ActionResult DrawPlusCodeCustomData(string code, string styleSet, string dataKey)
        {
            try
            {
                var info = new ImageStats(code);

                if (!DataCheck.IsInBounds(info.area))
                {
                    Response.Headers.Add("X-notes", "OOB");
                    return StatusCode(500);
                }

                byte[] tileData = getExistingTile(code, styleSet);
                if (tileData != null)
                {
                    Response.Headers.Add("X-notes", "cached");
                    return File(tileData, "image/png");
                }

                //Make tile
                var places = GetPlacesForTile(info, null, styleSet, false);
                var paintOps = MapTileSupport.GetPaintOpsForAreaData(dataKey, styleSet, info);
                tileData = FinishMapTile(info, paintOps, code, styleSet);

                Response.Headers.Add("X-notes", Configuration.GetValue<string>("MapTilesEngine"));
                return File(tileData, "image/png");
            }
            catch (Exception ex)
            {
                ErrorLogger.LogError(ex);
                return StatusCode(500);
            }
        }

        [HttpGet]
        [Route("/[controller]/DrawPlusCodeCustomElements/{code}/{styleSet}/{dataKey}")]
        [Route("/[controller]/AreaPlaceData/{code}/{styleSet}/{dataKey}")] //Draw an area using place data.
        public ActionResult DrawPlusCodeCustomElements(string code, string styleSet, string dataKey)
        {
            try
            {
                var info = new ImageStats(code);

                if (!DataCheck.IsInBounds(info.area))
                {
                    Response.Headers.Add("X-notes", "OOB");
                    return StatusCode(500);
                }

                byte[] tileData = getExistingTile(code, styleSet);
                if (tileData != null)
                {
                    Response.Headers.Add("X-notes", "cached");
                    return File(tileData, "image/png");
                }

                //Make tile
                var places = GetPlacesForTile(info, null, styleSet, false);
                var paintOps = MapTileSupport.GetPaintOpsForPlacesData(dataKey, styleSet, info);
                tileData = FinishMapTile(info, paintOps, code, styleSet);

                Response.Headers.Add("X-notes", Configuration.GetValue<string>("MapTilesEngine"));
                return File(tileData, "image/png");
            }
            catch (Exception ex)
            {
                ErrorLogger.LogError(ex);
                return StatusCode(500);
            }
        }

        [HttpPut]
        [Route("/[controller]/ExpireTiles/{elementId}/{styleSet}")]
        [Route("/[controller]/Expire/{elementId}/{styleSet}")]
        public void ExpireTiles(Guid elementId, string styleSet)
        {
            var db = new PraxisContext();
            db.ExpireMapTiles(elementId, styleSet);
        }

        [HttpGet]
        [Route("/[controller]/GetTileGenerationId/{plusCode}/{styleSet}")]
        [Route("/[controller]/Generation/{plusCode}/{styleSet}")]
        public long GetTileGenerationId(string plusCode, string styleSet)
        {
            //Returns generationID on the tile on the server
            //if value is *more* than previous value, client should refresh it.
            //if value is equal to previous value, tile has not changed.
            //As is, the client will probably download map tiles twice on change. Once when its expired and being redrawn (-1 return value),
            //and once when the generationID value is incremented from the previous value.
            //Avoiding that might require an endpoint for 'please draw this tile' that returns true or false rather than the actual maptile.
            try
            {
                //bool valueExists = cache.TryGetValue("gen" + plusCode + styleSet, out long genId);
                //if (valueExists)
                    //return genId;

                var db = new PraxisContext();
                long tileGenId = -1;
                var tile = db.MapTiles.FirstOrDefault(m => m.PlusCode == plusCode && m.StyleSet == styleSet);
                if (tile != null && tile.ExpireOn > DateTime.UtcNow)
                    tileGenId = tile.GenerationID;

                //cache.Set("gen" + plusCode + styleSet, tileGenId, new TimeSpan(0, 0, 30));
                return tileGenId;
            }
            catch (Exception ex)
            {
                ErrorLogger.LogError(ex);
                return -1; //negative answers will be treated as an expiration.
            }
        }

        [HttpGet]
        [Route("/[controller]/GetSlippyTileGenerationId/{x}/{y}/{zoom}/{styleSet}")]
        [Route("/[controller]/Generation/{zoom}/{x}/{y}/{styleSet}")]
        public long GetSlippyTileGenerationId(string x, string y, string zoom, string styleSet)
        {
            //Returns generationID on the tile on the server
            //if value is *more* than previous value, client should refresh it.
            //if value is equal to previous value, tile has not changed.
            try
            {
                var db = new PraxisContext();
                long tileGenId = -1;
                var tile = db.SlippyMapTiles.FirstOrDefault(m => m.Values == x + "|" + y + "|" + zoom && m.StyleSet == styleSet);
                if (tile != null && tile.ExpireOn > DateTime.UtcNow)
                    tileGenId = tile.GenerationID;
                return tileGenId;
            }
            catch (Exception ex)
            {
                ErrorLogger.LogError(ex);
                return -1; //negative answers will be treated as an expiration.
            }
        }

        [HttpGet]
        [Route("/[controller]/StyleTest/{styleSet}")]
        public ActionResult DrawAllStyleEntries(string styleSet)
        {
            var styleData = TagParser.allStyleGroups[styleSet].ToList();
            //Draw style as an X by X grid of circles, where X is square root of total sets
            int gridSize = (int)Math.Ceiling(Math.Sqrt(styleData.Count));

            //each circle is 25x25 pixels.

            ImageStats stats = new ImageStats("86"); //Constructor is ignored, all the values are overridden.
            stats.imageSizeX = gridSize * 30;
            stats.imageSizeY = gridSize * 30;
            stats.drawPoints = true;
            var circleSize = stats.degreesPerPixelX * 25;

            List<CompletePaintOp> testCircles = new List<CompletePaintOp>();

            for (int y = 0; y < gridSize; y++)
                for (int x = 0; x < gridSize; x++)
                {
                    var index = (x * gridSize) + y;
                    if (index < styleData.Count)
                    {
                        var circlePosX = stats.area.WestLongitude + ((stats.area.LongitudeWidth / gridSize) * x);
                        var circlePosY = stats.area.NorthLatitude - ((stats.area.LatitudeHeight / gridSize) * y);
                        var circle = new NetTopologySuite.Geometries.Point(circlePosX, circlePosY).Buffer(circleSize);
                        foreach (var op in styleData[index].Value.PaintOperations)
                        {
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
