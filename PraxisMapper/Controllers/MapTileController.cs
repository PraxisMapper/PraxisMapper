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

        [HttpGet]
        [Route("/[controller]/DrawSlippyTile/{styleSet}/{zoom}/{x}/{y}.png")] //slippy map conventions.
        public ActionResult DrawSlippyTile(int x, int y, int zoom, string styleSet)
        {
            //slippymaps don't use coords. They use a grid from -180W to 180E, 85.0511N to -85.0511S (they might also use radians, not degrees, for an additional conversion step)
            //Remember to invert Y to match PlusCodes going south to north.
            try
            {
                PerformanceTracker pt = new PerformanceTracker("DrawSlippyTile");
                string tileKey = x.ToString() + "|" + y.ToString() + "|" + zoom.ToString();
                var db = new PraxisContext();
                var existingResults = db.SlippyMapTiles.FirstOrDefault(mt => mt.Values == tileKey && mt.styleSet == styleSet);
                bool useCache = false;
                cache.TryGetValue("caching", out useCache);
                if (!useCache || existingResults == null || existingResults.SlippyMapTileId == null || existingResults.ExpireOn < DateTime.Now)
                {
                    //Create this entry
                    var info = new ImageStats(zoom, x, y, IMapTiles.MapTileSizeSquare);
                    if (!DataCheck.IsInBounds(cache.Get<IPreparedGeometry>("serverBounds"), info.area))
                        return StatusCode(500);

                    info.filterSize = info.degreesPerPixelX * 2; //I want this to apply to areas, and allow lines to be drawn regardless of length.
                    if (zoom >= 15) //Gameplay areas are ~15.
                    {
                        info.filterSize = 0;
                        info.drawPoints = true;
                    }

                    var dataLoadArea = new GeoArea(info.area.SouthLatitude - ConstantValues.resolutionCell10, info.area.WestLongitude - ConstantValues.resolutionCell10, info.area.NorthLatitude + ConstantValues.resolutionCell10, info.area.EastLongitude + ConstantValues.resolutionCell10);
                    DateTime expires = DateTime.Now;
                    byte[] results = null;
                    var places = GetPlaces(dataLoadArea, null, info.filterSize, styleSet, false, info.drawPoints);
                    //var places = GetPlacesForTile(info, null, styleSet); //Image area crops points near boundaries
                    var paintOps = MapTileSupport.GetPaintOpsForStoredElements(places, styleSet, info);
                    results = MapTiles.DrawAreaAtSize(info, paintOps); //, TagParser.GetStyleBgColor(styleSet));
                    expires = DateTime.Now.AddYears(10); //Assuming you are going to manually update/clear tiles when you reload base data
                    if (existingResults == null)
                        db.SlippyMapTiles.Add(new SlippyMapTile() { Values = tileKey, CreatedOn = DateTime.Now, styleSet = styleSet, tileData = results, ExpireOn = expires, areaCovered = Converters.GeoAreaToPolygon(dataLoadArea) });
                    else
                    {
                        existingResults.CreatedOn = DateTime.Now;
                        existingResults.ExpireOn = expires;
                        existingResults.tileData = results;
                        existingResults.generationID++;
                    }
                    if (useCache)
                        db.SaveChanges();
                    pt.Stop(tileKey + "|" + styleSet + "|" + Configuration.GetValue<string>("MapTilesEngine"));
                    return File(results, "image/png");
                }

                pt.Stop(tileKey + "|" + styleSet);
                return File(existingResults.tileData, "image/png");
            }
            catch (Exception ex)
            {
                ErrorLogger.LogError(ex);
                return StatusCode(500);
            }
        }

        [HttpGet]
        [Route("/[controller]/DrawSlippyTileCustomElements/{styleSet}/{dataKey}/{zoom}/{x}/{y}.png")] //slippy map conventions.

        public ActionResult DrawSlippyTileCustomElements(int x, int y, int zoom, string styleSet, string dataKey)
        {
            //slippymaps don't use coords. They use a grid from -180W to 180E, 85.0511N to -85.0511S (they might also use radians, not degrees, for an additional conversion step)
            //Remember to invert Y to match PlusCodes going south to north.
            try
            {
                PerformanceTracker pt = new PerformanceTracker("DrawSlippyTileCustomElements");
                string tileKey = x.ToString() + "|" + y.ToString() + "|" + zoom.ToString();
                var db = new PraxisContext();
                var existingResults = db.SlippyMapTiles.FirstOrDefault(mt => mt.Values == tileKey && mt.styleSet == styleSet);
                bool useCache = true;
                cache.TryGetValue("caching", out useCache);
                if (!useCache || existingResults == null || existingResults.SlippyMapTileId == null || existingResults.ExpireOn < DateTime.Now)
                {
                    //Create this entry
                    var info = new ImageStats(zoom, x, y, IMapTiles.MapTileSizeSquare);
                    if (!DataCheck.IsInBounds(cache.Get<IPreparedGeometry>("serverBounds"), info.area))
                        return StatusCode(400);
                    info.filterSize = info.degreesPerPixelX * 2; //I want this to apply to areas, and allow lines to be drawn regardless of length.
                    if (zoom >= 15) //Gameplay areas are ~15.
                    {
                        info.filterSize = 0;
                        info.drawPoints = true;
                    }

                    DateTime expires = DateTime.Now;
                    byte[] results = null;
                    var places = GetPlacesForTile(info, null, styleSet);
                    var paintOps = MapTileSupport.GetPaintOpsForCustomDataElements(Converters.GeoAreaToPolygon(info.area), dataKey, styleSet, info);
                    results = MapTiles.DrawAreaAtSize(info, paintOps); //, TagParser.GetStyleBgColor(styleSet));
                    expires = DateTime.Now.AddYears(10); //Assuming you are going to manually update/clear tiles when you reload base data
                    if (existingResults == null)
                        db.SlippyMapTiles.Add(new SlippyMapTile() { Values = tileKey, CreatedOn = DateTime.Now, styleSet = styleSet, tileData = results, ExpireOn = expires, areaCovered = Converters.GeoAreaToPolygon(info.area) });
                    else
                    {
                        existingResults.CreatedOn = DateTime.Now;
                        existingResults.ExpireOn = expires;
                        existingResults.tileData = results;
                        existingResults.generationID++;
                    }
                    if (useCache)
                        db.SaveChanges();
                    pt.Stop(tileKey + "|" + styleSet + "|" + Configuration.GetValue<string>("MapTilesEngine"));
                    return File(results, "image/png");
                }

                pt.Stop(tileKey + "|" + styleSet);
                return File(existingResults.tileData, "image/png");
            }
            catch (Exception ex)
            {
                ErrorLogger.LogError(ex);
                return StatusCode(500);
            }
        }

        [HttpGet]
        [Route("/[controller]/DrawSlippyTileCustomPlusCodes/{styleSet}/{dataKey}/{zoom}/{x}/{y}.png")] //slippy map conventions.

        public ActionResult DrawSlippyTileCustomPlusCodes(int x, int y, int zoom, string styleSet, string dataKey)
        {
            //slippymaps don't use coords. They use a grid from -180W to 180E, 85.0511N to -85.0511S (they might also use radians, not degrees, for an additional conversion step)
            //Remember to invert Y to match PlusCodes going south to north.
            try
            {
                PerformanceTracker pt = new PerformanceTracker("DrawSlippyTileCustomPlusCodes");
                string tileKey = x.ToString() + "|" + y.ToString() + "|" + zoom.ToString();
                var db = new PraxisContext();
                var existingResults = db.SlippyMapTiles.FirstOrDefault(mt => mt.Values == tileKey && mt.styleSet == styleSet);
                bool useCache = true;
                cache.TryGetValue("caching", out useCache);
                if (!useCache || existingResults == null || existingResults.SlippyMapTileId == null || existingResults.ExpireOn < DateTime.Now)
                {
                    //Create this entry
                    var info = new ImageStats(zoom, x, y, IMapTiles.MapTileSizeSquare);
                    if (!DataCheck.IsInBounds(cache.Get<IPreparedGeometry>("serverBounds"), info.area))
                        return StatusCode(400);
                    info.filterSize = info.degreesPerPixelX * 2; //I want this to apply to areas, and allow lines to be drawn regardless of length.
                    if (zoom >= 15) //Gameplay areas are ~15.
                        info.filterSize = 0;

                    //var dataLoadArea = new GeoArea(info.area.SouthLatitude - ConstantValues.resolutionCell10, info.area.WestLongitude - ConstantValues.resolutionCell10, info.area.NorthLatitude + ConstantValues.resolutionCell10, info.area.EastLongitude + ConstantValues.resolutionCell10);
                    DateTime expires = DateTime.Now;
                    byte[] results = null;
                    //var places = GetPlaces(dataLoadArea, filterSize: info.filterSize); //includeGenerated: false, filterSize: filterSize  //NOTE: in this case, we want generated areas to be their own slippy layer, so the config setting is ignored here.
                    //results = MapTiles.DrawAreaAtSize(info, places, styleSet, true);
                    var places = GetPlacesForTile(info, null, styleSet);
                    var paintOps = MapTileSupport.GetPaintOpsForCustomDataPlusCodes(Converters.GeoAreaToPolygon(info.area), dataKey, styleSet, info);
                    results = MapTiles.DrawAreaAtSize(info, paintOps); //, TagParser.GetStyleBgColor(styleSet));
                    expires = DateTime.Now.AddYears(10); //Assuming you are going to manually update/clear tiles when you reload base data
                    if (existingResults == null)
                        db.SlippyMapTiles.Add(new SlippyMapTile() { Values = tileKey, CreatedOn = DateTime.Now, styleSet = styleSet, tileData = results, ExpireOn = expires, areaCovered = Converters.GeoAreaToPolygon(info.area) });
                    else
                    {
                        existingResults.CreatedOn = DateTime.Now;
                        existingResults.ExpireOn = expires;
                        existingResults.tileData = results;
                        existingResults.generationID++;
                    }
                    if (useCache)
                        db.SaveChanges();
                    pt.Stop(tileKey + "|" + styleSet + "|" + Configuration.GetValue<string>("MapTilesEngine"));
                    return File(results, "image/png");
                }

                pt.Stop(tileKey + "|" + styleSet);
                return File(existingResults.tileData, "image/png");
            }
            catch (Exception ex)
            {
                ErrorLogger.LogError(ex);
                return StatusCode(500);
            }
        }

        [HttpGet]
        [Route("/[controller]/DrawSlippyTileCustomPlusCodesByTag/{styleSet}/{dataKey}/{zoom}/{x}/{y}.png")] //slippy map conventions.

        public ActionResult DrawSlippyTileCustomPlusCodesByTag(int x, int y, int zoom, string styleSet, string dataKey)
        {
            //slippymaps don't use coords. They use a grid from -180W to 180E, 85.0511N to -85.0511S (they might also use radians, not degrees, for an additional conversion step)
            //Remember to invert Y to match PlusCodes going south to north.
            try
            {
                PerformanceTracker pt = new PerformanceTracker("DrawSlippyTile");
                string tileKey = x.ToString() + "|" + y.ToString() + "|" + zoom.ToString();
                var db = new PraxisContext();
                var existingResults = db.SlippyMapTiles.FirstOrDefault(mt => mt.Values == tileKey && mt.styleSet == styleSet);
                bool useCache = true;
                cache.TryGetValue("caching", out useCache);
                if (!useCache || existingResults == null || existingResults.SlippyMapTileId == null || existingResults.ExpireOn < DateTime.Now)
                {
                    //Create this entry
                    var info = new ImageStats(zoom, x, y, IMapTiles.MapTileSizeSquare);
                    if (!DataCheck.IsInBounds(cache.Get<IPreparedGeometry>("serverBounds"), info.area))
                        return StatusCode(400);
                    info.filterSize = info.degreesPerPixelX * 2; //I want this to apply to areas, and allow lines to be drawn regardless of length.
                    if (zoom >= 15) //Gameplay areas are ~15.
                        info.filterSize = 0;

                    //var dataLoadArea = new GeoArea(info.area.SouthLatitude - ConstantValues.resolutionCell10, info.area.WestLongitude - ConstantValues.resolutionCell10, info.area.NorthLatitude + ConstantValues.resolutionCell10, info.area.EastLongitude + ConstantValues.resolutionCell10);
                    DateTime expires = DateTime.Now;
                    byte[] results = null;
                    //var places = GetPlaces(dataLoadArea, filterSize: info.filterSize); //includeGenerated: false, filterSize: filterSize  //NOTE: in this case, we want generated areas to be their own slippy layer, so the config setting is ignored here.
                    //results = MapTiles.DrawAreaAtSize(info, places, styleSet, true);
                    var places = GetPlacesForTile(info, null, styleSet);
                    var paintOps = MapTileSupport.GetPaintOpsForCustomDataPlusCodesFromTagValue(Converters.GeoAreaToPolygon(info.area), dataKey, styleSet, info);
                    results = MapTiles.DrawAreaAtSize(info, paintOps); //, TagParser.GetStyleBgColor(styleSet));
                    expires = DateTime.Now.AddYears(10); //Assuming you are going to manually update/clear tiles when you reload base data
                    if (existingResults == null)
                        db.SlippyMapTiles.Add(new SlippyMapTile() { Values = tileKey, CreatedOn = DateTime.Now, styleSet = styleSet, tileData = results, ExpireOn = expires, areaCovered = Converters.GeoAreaToPolygon(info.area) });
                    else
                    {
                        existingResults.CreatedOn = DateTime.Now;
                        existingResults.ExpireOn = expires;
                        existingResults.tileData = results;
                        existingResults.generationID++;
                    }
                    if (useCache)
                        db.SaveChanges();
                    pt.Stop(tileKey + "|" + styleSet + "|" + Configuration.GetValue<string>("MapTilesEngine"));
                    return File(results, "image/png");
                }

                pt.Stop(tileKey + "|" + styleSet);
                return File(existingResults.tileData, "image/png");
            }
            catch (Exception ex)
            {
                ErrorLogger.LogError(ex);
                return StatusCode(500);
            }
        }

        [HttpGet]
        [Route("/[controller]/CheckTileExpiration/{PlusCode}/{styleSet}")]
        public string CheckTileExpiration(string PlusCode, string styleSet) //For simplicity, maptiles expire after the Date part of a DateTime. Intended for base tiles.
        {
            //I pondered making this a boolean, but the client needs the expiration date to know if it's newer or older than it's version. Not if the server needs to redraw the tile. That happens on load.
            //I think, what I actually need, is the CreatedOn, and if it's newer than the client's tile, replace it.
            PerformanceTracker pt = new PerformanceTracker("CheckTileExpiration");
            var db = new PraxisContext();
            var mapTileExp = db.MapTiles.FirstOrDefault(m => m.PlusCode == PlusCode && m.styleSet == styleSet).ExpireOn;
            pt.Stop();
            return mapTileExp.ToShortDateString();
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
        public ActionResult DrawPlusCode(string code, string styleSet = "mapTiles")
        {
            try
            {
                PerformanceTracker pt = new PerformanceTracker("DrawPlusCode");
                if (!DataCheck.IsInBounds(cache.Get<IPreparedGeometry>("serverBounds"), OpenLocationCode.DecodeValid(code)))
                    return StatusCode(400);
                code = code.ToUpper();
                string tileKey = code + "|" + styleSet;
                var db = new PraxisContext();
                var existingResults = db.MapTiles.FirstOrDefault(mt => mt.PlusCode == code && mt.styleSet == styleSet);
                bool useCache = true;
                cache.TryGetValue("caching", out useCache);
                if (!useCache || existingResults == null || existingResults.MapTileId == null || existingResults.ExpireOn < DateTime.Now)
                {
                    //Create this entry
                    var results = MapTileSupport.DrawPlusCode(code, styleSet);
                    var expires = DateTime.Now.AddYears(10); //Assuming tile expiration occurs only when needed.
                    var dataLoadArea = OpenLocationCode.DecodeValid(code);
                    if (existingResults == null)
                        db.MapTiles.Add(new MapTile() { PlusCode = code, CreatedOn = DateTime.Now, resolutionScale = 11, styleSet = styleSet, tileData = results, ExpireOn = expires, areaCovered = Converters.GeoAreaToPolygon(dataLoadArea) });
                    else
                    {
                        existingResults.CreatedOn = DateTime.Now;
                        existingResults.ExpireOn = expires;
                        existingResults.tileData = results;
                        existingResults.generationID++;
                    }
                    db.SaveChanges();
                    pt.Stop(code + "|" + styleSet + "|" + Configuration.GetValue<string>("MapTilesEngine"));
                    return File(results, "image/png");
                }

                pt.Stop(code + "|" + styleSet);
                return File(existingResults.tileData, "image/png");
            }
            catch (Exception ex)
            {
                ErrorLogger.LogError(ex);
                return StatusCode(500);
            }
        }

        [HttpGet]
        [Route("/[controller]/DrawPlusCodeCustomData/{code}/{styleSet}/{dataKey}")]
        public ActionResult DrawPlusCodeCustomData(string code, string styleSet, string dataKey)
        {
            try
            {
                PerformanceTracker pt = new PerformanceTracker("DrawPlusCodeCustomData");
                if (!DataCheck.IsInBounds(cache.Get<IPreparedGeometry>("serverBounds"), OpenLocationCode.DecodeValid(code)))
                    return StatusCode(400);
                code = code.ToUpper();
                string tileKey = code + "|" + styleSet + "|" + dataKey;
                var db = new PraxisContext();
                var existingResults = db.MapTiles.FirstOrDefault(mt => mt.PlusCode == code && mt.styleSet == styleSet);
                bool useCache = true;
                cache.TryGetValue("caching", out useCache);
                if (!useCache || existingResults == null || existingResults.MapTileId == null || existingResults.ExpireOn < DateTime.Now)
                {
                    //Create this entry
                    var area = OpenLocationCode.DecodeValid(code);
                    var poly = Converters.GeoAreaToPolygon(area);
                    int imgX = 0, imgY = 0;
                    MapTileSupport.GetPlusCodeImagePixelSize(code, out imgX, out imgY);
                    ImageStats stats = new ImageStats(area, imgX, imgY);
                    var paintOps = MapTileSupport.GetPaintOpsForCustomDataPlusCodes(poly, dataKey, styleSet, stats);
                    var results = MapTiles.DrawAreaAtSize(stats, paintOps); //, TagParser.GetStyleBgColor(styleSet));
                    var expires = DateTime.Now.AddYears(10); //Assuming tile expiration occurs only when needed.
                    var dataLoadArea = OpenLocationCode.DecodeValid(code);
                    if (existingResults == null)
                        db.MapTiles.Add(new MapTile() { PlusCode = code, CreatedOn = DateTime.Now, resolutionScale = 11, styleSet = styleSet, tileData = results, ExpireOn = expires, areaCovered = Converters.GeoAreaToPolygon(dataLoadArea) });
                    else
                    {
                        existingResults.CreatedOn = DateTime.Now;
                        existingResults.ExpireOn = expires;
                        existingResults.tileData = results;
                        existingResults.generationID++;
                    }
                    db.SaveChanges();
                    pt.Stop(code + "|" + styleSet + "|" + Configuration.GetValue<string>("MapTilesEngine"));
                    return File(results, "image/png");
                }

                pt.Stop(code + "|" + styleSet);
                return File(existingResults.tileData, "image/png");
            }
            catch (Exception ex)
            {
                ErrorLogger.LogError(ex);
                return StatusCode(500);
            }
        }

        [HttpGet]
        [Route("/[controller]/DrawPlusCodeCustomDataByTag/{code}/{styleSet}/{dataKey}")]
        public ActionResult DrawPlusCodeCustomDataByTag(string code, string styleSet, string dataKey)
        {
            try
            {
                PerformanceTracker pt = new PerformanceTracker("DrawPlusCodeCustomData");
                if (!DataCheck.IsInBounds(cache.Get<IPreparedGeometry>("serverBounds"), OpenLocationCode.DecodeValid(code)))
                    return StatusCode(400);
                code = code.ToUpper();
                string tileKey = code + "|" + styleSet + "|" + dataKey;
                var db = new PraxisContext();
                var existingResults = db.MapTiles.FirstOrDefault(mt => mt.PlusCode == code && mt.styleSet == styleSet);
                bool useCache = true;
                cache.TryGetValue("caching", out useCache);
                if (!useCache || existingResults == null || existingResults.MapTileId == null || existingResults.ExpireOn < DateTime.Now)
                {
                    //Create this entry
                    var area = OpenLocationCode.DecodeValid(code);
                    var dataLoadArea = new GeoArea(area.SouthLatitude - ConstantValues.resolutionCell10, area.WestLongitude - ConstantValues.resolutionCell10, area.NorthLatitude + ConstantValues.resolutionCell10, area.EastLongitude + ConstantValues.resolutionCell10);
                    var poly = Converters.GeoAreaToPolygon(area);
                    int imgX = 0, imgY = 0;
                    MapTileSupport.GetPlusCodeImagePixelSize(code, out imgX, out imgY);
                    ImageStats stats = new ImageStats(area, imgX, imgY);
                    var paintOps = MapTileSupport.GetPaintOpsForCustomDataPlusCodesFromTagValue(poly, dataKey, styleSet, stats);
                    var results = MapTiles.DrawAreaAtSize(stats, paintOps); //, TagParser.GetStyleBgColor(styleSet));
                    var expires = DateTime.Now.AddYears(10); //Assuming tile expiration occurs only when needed.
                    //var dataLoadArea = OpenLocationCode.DecodeValid(code);
                    if (existingResults == null)
                        db.MapTiles.Add(new MapTile() { PlusCode = code, resolutionScale = 11, CreatedOn = DateTime.Now, styleSet = styleSet, tileData = results, ExpireOn = expires, areaCovered = Converters.GeoAreaToPolygon(dataLoadArea) });
                    else
                    {
                        existingResults.CreatedOn = DateTime.Now;
                        existingResults.ExpireOn = expires;
                        existingResults.tileData = results;
                        existingResults.generationID++;
                    }
                    db.SaveChanges();
                    pt.Stop(code + "|" + styleSet + "|" + Configuration.GetValue<string>("MapTilesEngine"));
                    return File(results, "image/png");
                }

                pt.Stop(code + "|" + styleSet);
                return File(existingResults.tileData, "image/png");
            }
            catch (Exception ex)
            {
                ErrorLogger.LogError(ex);
                return StatusCode(500);
            }
        }

        [HttpGet]
        [Route("/[controller]/DrawPlusCodeCustomElements/{code}/{styleSet}/{dataKey}")]
        public ActionResult DrawPlusCodeCustomElements(string code, string styleSet, string dataKey)
        {
            try
            {
                PerformanceTracker pt = new PerformanceTracker("DrawPlusCodeCustomElements");
                if (!DataCheck.IsInBounds(cache.Get<IPreparedGeometry>("serverBounds"), OpenLocationCode.DecodeValid(code)))
                    return StatusCode(400);
                code = code.ToUpper();
                string tileKey = code + "|" + styleSet + "|" + dataKey;
                var db = new PraxisContext();
                var existingResults = db.MapTiles.FirstOrDefault(mt => mt.PlusCode == code && mt.styleSet == styleSet);
                bool useCache = true;
                cache.TryGetValue("caching", out useCache);
                if (!useCache || existingResults == null || existingResults.MapTileId == null || existingResults.ExpireOn < DateTime.Now)
                {
                    //Create this entry
                    var area = OpenLocationCode.DecodeValid(code);
                    var poly = Converters.GeoAreaToPolygon(area);
                    int imgX = 0, imgY = 0;
                    MapTileSupport.GetPlusCodeImagePixelSize(code, out imgX, out imgY);
                    ImageStats stats = new ImageStats(area, imgX, imgY);
                    var paintOps = MapTileSupport.GetPaintOpsForCustomDataElements(poly, dataKey, styleSet, stats);
                    var results = MapTiles.DrawAreaAtSize(stats, paintOps); //, TagParser.GetStyleBgColor(styleSet));
                    var expires = DateTime.Now.AddYears(10); //Assuming tile expiration occurs only when needed.
                    var dataLoadArea = OpenLocationCode.DecodeValid(code);
                    if (existingResults == null)
                        db.MapTiles.Add(new MapTile() { PlusCode = code, resolutionScale = 11, CreatedOn = DateTime.Now, styleSet = styleSet, tileData = results, ExpireOn = expires, areaCovered = Converters.GeoAreaToPolygon(dataLoadArea) });
                    else
                    {
                        existingResults.CreatedOn = DateTime.Now;
                        existingResults.ExpireOn = expires;
                        existingResults.tileData = results;
                        existingResults.generationID++;
                    }
                    db.SaveChanges();
                    pt.Stop(code + "|" + styleSet + "|" + Configuration.GetValue<string>("MapTilesEngine"));
                    return File(results, "image/png");
                }

                pt.Stop(code + "|" + styleSet);
                return File(existingResults.tileData, "image/png");
            }
            catch (Exception ex)
            {
                ErrorLogger.LogError(ex);
                return StatusCode(500);
            }
        }

        [HttpGet]
        [Route("/[controller]/ExpireTiles/{elementId}/{styleSet}")]
        public void ExpireTiles(Guid elementId, string styleSet)
        {
            var db = new PraxisContext();
            db.ExpireMapTiles(elementId, styleSet);
        }

        [HttpGet]
        [Route("/[controller]/GetTileExpiration/{plusCode}/{styleSet}")]
        public static long GetTileExpiration(string plusCode, string styleSet)
        {
            //Returns seconds remaining on this tile's lifetime in the server.
            //if value is *more* than previous value, client should refres it.
            //if value is less than previous value, tile has not changed and ticks forward.
            try
            {
                PerformanceTracker pt = new PerformanceTracker("GetTileExpiration");
                var db = new PraxisContext();
                var tileDate = db.MapTiles
                    .Where(m => m.PlusCode == plusCode && m.styleSet == styleSet)
                    .Select(m => m.ExpireOn)
                    .FirstOrDefault();

                var results = (long)(tileDate - DateTime.Now).TotalSeconds;
                pt.Stop();
                return results;
            }
            catch (Exception ex)
            {
                ErrorLogger.LogError(ex);
                return -1; //negative answers will be treated as an expiration.
            }
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
                var tileGenId = db.MapTiles
                    .FirstOrDefault(m => m.PlusCode == plusCode && m.styleSet == styleSet)
                    .generationID; 
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
                var tileGenId = db.SlippyMapTiles
                    .FirstOrDefault(m => m.Values == x + "|" + y + "|" + zoom && m.styleSet == styleSet)
                    .generationID;
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
