using CoreComponents;
using Google.OpenLocationCode;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using PraxisMapper.Classes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using static CoreComponents.DbTables;
using static CoreComponents.Place;

namespace PraxisMapper.Controllers
{
    [Route("[controller]")]
    [ApiController]
    public class MapTileController : Controller
    {
        private readonly IConfiguration Configuration;
        private static MemoryCache cache;

        public MapTileController(IConfiguration configuration)
        {
            Configuration = configuration;

            if (cache == null && Configuration.GetValue<bool>("enableCaching") == true)
            {
                var options = new MemoryCacheOptions();
                options.SizeLimit = 1024; //1k entries. that's 2.5 4-digit plus code blocks without roads/buildings. If an entry is 300kb on average, this is 300MB of RAM for caching. T3.small has 2GB total, t3.medium has 4GB.
                cache = new MemoryCache(options);
            }
        }

        [HttpGet]
        [Route("/[controller]/DrawSlippyTile/{x}/{y}/{zoom}/{layer}")]
        public FileContentResult DrawSlippyTile(int x, int y, int zoom, int layer)
        {
            //slippymaps don't use coords. They use a grid from -180W to 180E, 85.0511N to -85.0511S (they might also use radians, not degrees, for an additional conversion step)
            //with 2^zoom level tiles in place. so, i need to do some math to get a coordinate
            //X: -180 + ((360 / 2^zoom) * X)
            //Y: 8
            //Remember to invert Y to match PlusCodes going south to north.
            //BUT Also, PlusCodes have 20^(zoom/2) tiles, and Slippy maps have 2^zoom tiles, this doesn't even line up nicely.
            //Slippy Map tiles might just have to be their own thing.
            //I will also say these are 512x512 images.
            //TODO: add padding for image cropping. Maybe 10 pixels on each edge?
            //TODO: should I set a longer timeout for these webtiles, and expire them when something in them gets updated?
            //This is much harder to detect for slippy maps, since I have to re-caculate the X and Y on a bunch of zoom levels for it.

            try
            {
                PerformanceTracker pt = new PerformanceTracker("DrawSlippyTile");
                string tileKey = x.ToString() + "|" + y.ToString() + "|" + zoom.ToString();
                var db = new PraxisContext();
                var existingResults = db.SlippyMapTiles.Where(mt => mt.Values == tileKey && mt.mode == layer).FirstOrDefault(); //NOTE: PTT and AC shouldn't be cached for real long. Maybe a minute if at all.
                if (existingResults == null || existingResults.SlippyMapTileId == null || existingResults.ExpireOn < DateTime.Now)
                {
                    //Create this entry
                    //requires a list of colors to use, which might vary per app
                    var n = Math.Pow(2, zoom);

                    var lon_degree_w = x / n * 360 - 180;
                    var lon_degree_e = (x + 1) / n * 360 - 180;
                    var lat_rads_n = Math.Atan(Math.Sinh(Math.PI * (1 - 2 * y / n)));
                    var lat_degree_n = lat_rads_n * 180 / Math.PI;
                    var lat_rads_s = Math.Atan(Math.Sinh(Math.PI * (1 - 2 * (y + 1) / n)));
                    var lat_degree_s = lat_rads_s * 180 / Math.PI;

                    var relevantArea = new GeoArea(lat_degree_s, lon_degree_w, lat_degree_n, lon_degree_e);
                    var areaHeightDegrees = lat_degree_n - lat_degree_s;
                    var areaWidthDegrees = 360 / n;

                    DateTime expires = DateTime.Now;
                    byte[] results = null;
                    switch (layer)
                    {
                        case 1: //Base map tile
                            var places = GetPlaces(relevantArea, includeGenerated: false);
                            //results = MapTiles.DrawAreaMapTileSlippy(ref places, relevantArea, areaHeightDegrees, areaWidthDegrees);
                            results = MapTiles.DrawAreaMapTileSlippySkia(ref places, relevantArea, areaHeightDegrees, areaWidthDegrees);
                            expires = DateTime.Now.AddYears(10); //Assuming you are going to manually update/clear tiles when you reload base data
                            break;
                        case 2: //PaintTheTown overlay. Not yet done.
                            //I have to convert the area into PlusCode coordinates, then translate the found claims into rectangles to cover the right area.
                            //check what Cell8s are covered in the requested tile. Call LearnCell8 for all of those to find rectangles to draw.
                            //Might need to limit this down to a certain zoom level?
                            //This will need multiple entries, one per instance running. I will use the All-Time entries for now.
                            //results = MapTiles.DrawPaintTownSlippyTile(relevantArea, 2);
                            results = MapTiles.DrawPaintTownSlippyTileSkia(relevantArea, 2);
                            expires = DateTime.Now.AddMinutes(1); //We want this to be live-ish, but not overwhelming, so we cache this for 60 seconds.
                            break;
                        case 3: //MultiplayerAreaControl overlay. Ready to test as an overlay.
                            results = MapTiles.DrawMPAreaMapTileSlippy(relevantArea, areaHeightDegrees, areaWidthDegrees);
                            expires = DateTime.Now.AddMinutes(1); //We want this to be live-ish, but not overwhelming, so we cache this for 60 seconds.
                            break;
                        case 4: //GeneratedMapData areas. Should be ready to test as an overlay.
                            var places2 = GetGeneratedPlaces(relevantArea);
                            results = MapTiles.DrawAreaMapTileSlippy(ref places2, relevantArea, areaHeightDegrees, areaWidthDegrees, true);
                            expires = DateTime.Now.AddYears(10); //again, assuming these don't change unless you manually updated entries.
                            break;
                        case 5: //Custom objects (scavenger hunt). Should be points loaded up, not an overlay?
                            //this isnt supported yet as a game mode.
                            break;
                    }
                    if (existingResults == null)
                        db.SlippyMapTiles.Add(new SlippyMapTile() { Values = tileKey, CreatedOn = DateTime.Now, mode = layer, tileData = results, ExpireOn = expires });
                    else
                    {
                        existingResults.CreatedOn = DateTime.Now;
                        existingResults.ExpireOn = expires;
                        existingResults.tileData = results;
                    }
                    db.SaveChanges();
                    pt.Stop(tileKey);
                    return File(results, "image/png");
                }

                pt.Stop(tileKey + "|" + layer);
                return File(existingResults.tileData, "image/png");
            }
            catch(Exception ex)
            {
                var a = ex;
                return null;
            }
        }

        
        //ignore this one for a minute, Slippy Maps need their own tiles.
        //Might be ready to remove this one.

        [HttpGet]
        [Route("/[controller]/DrawCell/{lat}/{lon}/{cellSizeForPixel}/{cellTileSize}/{layer}")]
        public FileContentResult DrawCellByCoords(float lat, float lon, int cellSizeForPixel, int cellTileSize, int layer)
        {
            //lat and lon are geo coords (or, were expecting them to be.) THey're actually X and Y slippy map coords.
            //BUT slippymaps don't use coords. They use a grid from -180W to 180E, 85.0511N to -85.0511S (they might also use radians, not degrees, for an additional conversion step)
            //with 2^zoom level tiles in place. so, i need to do some math to get a coordinate
            //X: -180 + ((360 / 2^zoom) * X)
            //Y: 8
            //Remember to invert Y to match PlusCodes going south to north.
            //BUT Also, PlusCodes have 20^(zoom/2) tiles, and Slippy maps have 2^zoom tiles, this doesn't even line up nicely.
            //Slippy Map tiles might just have to be their own thing.

            //cellsizeForPixel is how big 1 pixel is (EX: 11 means it's a Cell11 per pixel and a rectangular image). Might support 12 now.
            //cellTileSize is how big of an area the resulting tile covers. (EX: 8 is Cell8, with 20 Cell10s and 400 Cell11s contained) Can't go below 11.
            //Default PraxisMApper tiles are 11 and 8.
            //layer is which set of content are we drawing?
            //map data, generated map data, multiplayer area-claim data, others? merging?
            PerformanceTracker pt = new PerformanceTracker("DrawCellByCoords");

            var minPlusCode = new OpenLocationCode(lat, lon, 11);
            var relevantPlusCode = minPlusCode.CodeDigits.Substring(0, cellTileSize);

            var db = new PraxisContext();
            var existingResults = db.MapTiles.Where(mt => mt.PlusCode == relevantPlusCode && mt.resolutionScale == cellTileSize && mt.mode == layer).FirstOrDefault();
            if (existingResults == null || existingResults.MapTileId == null)
            {
                //Create this entry
                //requires a list of colors to use, which might vary per app
                //GeoArea eightCell = OpenLocationCode.DecodeValid(plusCode8);
                var relevantArea = OpenLocationCode.DecodeValid(relevantPlusCode);
                var places = GetPlaces(relevantArea);
                var results = MapTiles.DrawAreaMapTile(ref places, relevantArea, cellSizeForPixel);
                db.MapTiles.Add(new MapTile() { PlusCode = relevantPlusCode, CreatedOn = DateTime.Now, mode = layer, resolutionScale = cellSizeForPixel, tileData = results });
                db.SaveChanges();
                pt.Stop(relevantPlusCode);
                return File(results, "image/png");
            }

            pt.Stop(relevantPlusCode);
            return File(existingResults.tileData, "image/png");
        }
    }
}
