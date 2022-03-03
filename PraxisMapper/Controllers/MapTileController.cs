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

        private bool SaveMapTiles()
        {
            bool saveMapTiles = true;
            cache.TryGetValue("saveMapTiles", out saveMapTiles);
            return saveMapTiles;
        }

        public byte[] getExistingSlippyTile(string tileKey, string styleSet)
        {
            if (!SaveMapTiles())
                return null;

            var db = new PraxisContext();
            var existingResults = db.SlippyMapTiles.FirstOrDefault(mt => mt.Values == tileKey && mt.StyleSet == styleSet);
            if (existingResults == null || existingResults.ExpireOn < DateTime.Now)
                return null;

            return existingResults.TileData;
        }

        public byte[] getExistingTile(string code, string styleSet)
        {
            if (!SaveMapTiles())
                return null;

            var db = new PraxisContext();
            var existingResults = db.MapTiles.FirstOrDefault(mt => mt.PlusCode == code && mt.StyleSet == styleSet);
            if (existingResults == null || existingResults.ExpireOn < DateTime.Now)
                return null;

            return existingResults.TileData;
        }

        private byte[] FinishSlippyMapTile(ImageStats info, List<CompletePaintOp> paintOps, string tileKey, string styleSet)
        {
            byte[] results = null;
            results = MapTiles.DrawAreaAtSize(info, paintOps);

            if (!SaveMapTiles())
            {
                var db = new PraxisContext();
                var existingResults = db.SlippyMapTiles.FirstOrDefault(mt => mt.Values == tileKey && mt.StyleSet == styleSet);
                if (existingResults == null)
                {
                    existingResults = new SlippyMapTile() { Values = tileKey, StyleSet = styleSet, AreaCovered = Converters.GeoAreaToPolygon(GeometrySupport.MakeBufferedGeoArea(info.area)) };
                    db.SlippyMapTiles.Add(existingResults);
                }

                existingResults.ExpireOn = DateTime.Now.AddYears(10);
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

            if (!SaveMapTiles())
            {
                var db = new PraxisContext();
                var existingResults = db.MapTiles.FirstOrDefault(mt => mt.PlusCode == code && mt.StyleSet == styleSet);
                if (existingResults == null)
                {
                    existingResults = new MapTile() { PlusCode = code, StyleSet = styleSet, AreaCovered = Converters.GeoAreaToPolygon(GeometrySupport.MakeBufferedGeoArea(info.area)) };
                    db.MapTiles.Add(existingResults);
                }

                existingResults.ExpireOn = DateTime.Now.AddYears(10);
                existingResults.TileData = results;
                existingResults.GenerationID++;
                db.SaveChanges();
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
                PerformanceTracker pt = new PerformanceTracker("DrawSlippyTile");
                string tileKey = x.ToString() + "|" + y.ToString() + "|" + zoom.ToString();
                var info = new ImageStats(zoom, x, y, IMapTiles.SlippyTileSizeSquare);

                if (!DataCheck.IsInBounds(cache.Get<IPreparedGeometry>("serverBounds"), info.area))
                {
                    pt.Stop("OOB");
                    return StatusCode(500);
                }

                byte[] tileData = getExistingSlippyTile(tileKey, styleSet);
                if (tileData != null)
                {
                    pt.Stop(tileKey + "|" + styleSet);
                    return File(tileData, "image/png");
                }

                //Make tile
                var places = GetPlacesForTile(info, null, styleSet, false);
                var paintOps = MapTileSupport.GetPaintOpsForStoredElements(places, styleSet, info);
                tileData = FinishSlippyMapTile(info, paintOps, tileKey, styleSet);

                pt.Stop(tileKey + "|" + styleSet + "|" + Configuration.GetValue<string>("MapTilesEngine"));
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
                PerformanceTracker pt = new PerformanceTracker("DrawSlippyTileCustomElements");
                string tileKey = x.ToString() + "|" + y.ToString() + "|" + zoom.ToString();
                var info = new ImageStats(zoom, x, y, IMapTiles.SlippyTileSizeSquare);

                if (!DataCheck.IsInBounds(cache.Get<IPreparedGeometry>("serverBounds"), info.area))
                {
                    pt.Stop("OOB");
                    return StatusCode(500);
                }

                byte[] tileData = getExistingSlippyTile(tileKey, styleSet);
                if (tileData != null)
                {
                    pt.Stop(tileKey + "|" + styleSet);
                    return File(tileData, "image/png");
                }

                //Make tile
                var places = GetPlacesForTile(info, null, styleSet);
                var paintOps = MapTileSupport.GetPaintOpsForCustomDataElements(dataKey, styleSet, info);
                tileData = FinishSlippyMapTile(info, paintOps, tileKey, styleSet);

                pt.Stop(tileKey + "|" + styleSet + "|" + Configuration.GetValue<string>("MapTilesEngine"));
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
                PerformanceTracker pt = new PerformanceTracker("DrawSlippyTileCustomPlusCodes");
                string tileKey = x.ToString() + "|" + y.ToString() + "|" + zoom.ToString();
                var info = new ImageStats(zoom, x, y, IMapTiles.SlippyTileSizeSquare);

                if (!DataCheck.IsInBounds(cache.Get<IPreparedGeometry>("serverBounds"), info.area))
                {
                    pt.Stop("OOB");
                    return StatusCode(500);
                }

                byte[] tileData = getExistingSlippyTile(tileKey, styleSet);
                if (tileData != null)
                {
                    pt.Stop(tileKey + "|" + styleSet);
                    return File(tileData, "image/png");
                }

                //Make tile
                var places = GetPlacesForTile(info, null, styleSet);
                var paintOps = MapTileSupport.GetPaintOpsForCustomDataPlusCodes(dataKey, styleSet, info);
                tileData = FinishSlippyMapTile(info, paintOps, tileKey, styleSet);

                pt.Stop(tileKey + "|" + styleSet + "|" + Configuration.GetValue<string>("MapTilesEngine"));
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
                PerformanceTracker pt = new PerformanceTracker("DrawTile");
                MapTileSupport.GetPlusCodeImagePixelSize(code, out var imgX, out var imgY);
                var info = new ImageStats(OpenLocationCode.DecodeValid(code), imgX, imgY);

                if (!DataCheck.IsInBounds(cache.Get<IPreparedGeometry>("serverBounds"), info.area))
                {
                    pt.Stop("OOB");
                    return StatusCode(500);
                }

                byte[] tileData = getExistingSlippyTile(code, styleSet);
                if (tileData != null)
                {
                    pt.Stop(code + "|" + styleSet);
                    return File(tileData, "image/png");
                }

                //Make tile
                var places = GetPlacesForTile(info, null, styleSet, false);
                var paintOps = MapTileSupport.GetPaintOpsForStoredElements(places, styleSet, info);
                tileData = FinishMapTile(info, paintOps, code, styleSet);

                pt.Stop(code + "|" + styleSet + "|" + Configuration.GetValue<string>("MapTilesEngine"));
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
                PerformanceTracker pt = new PerformanceTracker("DrawTileData");
                MapTileSupport.GetPlusCodeImagePixelSize(code, out var imgX, out var imgY);
                var info = new ImageStats(OpenLocationCode.DecodeValid(code), imgX, imgY);

                if (!DataCheck.IsInBounds(cache.Get<IPreparedGeometry>("serverBounds"), info.area))
                {
                    pt.Stop("OOB");
                    return StatusCode(500);
                }

                byte[] tileData = getExistingSlippyTile(code, styleSet);
                if (tileData != null)
                {
                    pt.Stop(code + "|" + styleSet);
                    return File(tileData, "image/png");
                }

                //Make tile
                var places = GetPlacesForTile(info, null, styleSet, false);
                var paintOps = MapTileSupport.GetPaintOpsForCustomDataPlusCodes(dataKey, styleSet, info);
                tileData = FinishMapTile(info, paintOps, code, styleSet);

                pt.Stop(code + "|" + styleSet + "|" + Configuration.GetValue<string>("MapTilesEngine"));
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
                PerformanceTracker pt = new PerformanceTracker("DrawTilePlace");
                MapTileSupport.GetPlusCodeImagePixelSize(code, out var imgX, out var imgY);
                var info = new ImageStats(OpenLocationCode.DecodeValid(code), imgX, imgY);

                if (!DataCheck.IsInBounds(cache.Get<IPreparedGeometry>("serverBounds"), info.area))
                {
                    pt.Stop("OOB");
                    return StatusCode(500);
                }

                byte[] tileData = getExistingSlippyTile(code, styleSet);
                if (tileData != null)
                {
                    pt.Stop(code + "|" + styleSet);
                    return File(tileData, "image/png");
                }

                //Make tile
                var places = GetPlacesForTile(info, null, styleSet, false);
                var paintOps = MapTileSupport.GetPaintOpsForCustomDataElements(dataKey, styleSet, info);
                tileData = FinishMapTile(info, paintOps, code, styleSet);

                pt.Stop(code + "|" + styleSet + "|" + Configuration.GetValue<string>("MapTilesEngine"));
                return File(tileData, "image/png");
            }
            catch (Exception ex)
            {
                ErrorLogger.LogError(ex);
                return StatusCode(500);
            }
        }

        [HttpGet]
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
            try
            {
                PerformanceTracker pt = new PerformanceTracker("GetTileGenerationId");
                var db = new PraxisContext();
                var tile = db.MapTiles.FirstOrDefault(m => m.PlusCode == plusCode && m.StyleSet == styleSet);
                long tileGenId = -1;
                if (tile != null)
                    tileGenId = tile.GenerationID;
                pt.Stop();
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
                PerformanceTracker pt = new PerformanceTracker("GetTileGenerationId");
                var db = new PraxisContext();
                long tileGenId = -1;
                var tile = db.SlippyMapTiles.FirstOrDefault(m => m.Values == x + "|" + y + "|" + zoom && m.StyleSet == styleSet);
                if (tile != null)
                    tileGenId = tile.GenerationID;
                pt.Stop();
                return tileGenId;
            }
            catch (Exception ex)
            {
                ErrorLogger.LogError(ex);
                return -1; //negative answers will be treated as an expiration.
            }
        }
    }
}
