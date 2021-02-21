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
        public FileContentResult DrawSlippyTile(int x, int y, int zoom, int layer) //assuming layer 1 for this right now.
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

            try
            {
                PerformanceTracker pt = new PerformanceTracker("DrawSlippyTile");
                string tileKey = x.ToString() + "|" + y.ToString() + "|" + zoom.ToString();
                var db = new PraxisContext();
                var existingResults = db.SlippyMapTiles.Where(mt => mt.Values == tileKey && mt.mode == layer).FirstOrDefault();
                if (existingResults == null || existingResults.SlippyMapTileId == null)
                {
                    //Create this entry
                    //requires a list of colors to use, which might vary per app
                    var n = Math.Pow(2, zoom);
                    
                    var lon_degree_w = x / n * 360 - 180; 
                    var lon_degree_e = (x+1) / n * 360 - 180; 
                    var lat_rads_n = Math.Atan(Math.Sinh(Math.PI * (1 - 2 * y / n)));
                    var lat_degree_n = lat_rads_n * 180 / Math.PI;
                    var lat_rads_s = Math.Atan(Math.Sinh(Math.PI * (1 - 2 * (y+1) / n)));
                    var lat_degree_s = lat_rads_s * 180 / Math.PI;

                    var relevantArea = new GeoArea(lat_degree_s, lon_degree_w, lat_degree_n, lon_degree_e);
                    var areaHeightDegrees = lat_degree_n - lat_degree_s;
                    var areaWidthDegrees = 360 / n;

                    var places = GetPlaces(relevantArea, includeGenerated: false);
                    var results = MapTiles.DrawAreaMapTileSlippy(ref places, relevantArea, areaHeightDegrees, areaWidthDegrees);
                    db.SlippyMapTiles.Add(new SlippyMapTile() { Values = tileKey, CreatedOn = DateTime.Now, mode = layer, tileData = results });
                    db.SaveChanges();
                    pt.Stop(tileKey);
                    return File(results, "image/png");
                }

                pt.Stop(tileKey);
                return File(existingResults.tileData, "image/png");
            }
            catch(Exception ex)
            {
                var a = ex;
                return null;
            }
        }

        
        //ignore this one for a minute, Slippy Maps need their own tiles.

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
