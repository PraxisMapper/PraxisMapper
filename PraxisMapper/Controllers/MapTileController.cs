using CoreComponents;
using Google.OpenLocationCode;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using PraxisMapper.Classes;
using System;
using System.Linq;
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

        //TODO: consider playing with the SKSVGCanvas to see if SVG maptiles are faster/smaller/better in any ways

        public MapTileController(IConfiguration configuration)
        {
            Configuration = configuration;

            if (cache == null && Configuration.GetValue<bool>("enableCaching") == true)
            {
                var options = new MemoryCacheOptions();
                options.SizeLimit = 1024;
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

                    //MapTiles.GetSlippyResolutions(x, y, zoom, ou)
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
                            //add some padding so we don't clip off points at the edge of a tile
                            var dataLoadArea = new GeoArea(relevantArea.SouthLatitude - ConstantValues.resolutionCell10, relevantArea.WestLongitude - ConstantValues.resolutionCell10, relevantArea.NorthLatitude + ConstantValues.resolutionCell10, relevantArea.EastLongitude + ConstantValues.resolutionCell10);
                            var places = GetPlaces(dataLoadArea, includeGenerated: false);
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
                            results = MapTiles.DrawMPAreaMapTileSlippySkia(relevantArea, areaHeightDegrees, areaWidthDegrees);
                            expires = DateTime.Now.AddMinutes(1); //We want this to be live-ish, but not overwhelming, so we cache this for 60 seconds.
                            break;
                        case 4: //GeneratedMapData areas. Should be ready to test as an overlay.
                            var dataLoadArea2 = new GeoArea(relevantArea.SouthLatitude - ConstantValues.resolutionCell10, relevantArea.WestLongitude - ConstantValues.resolutionCell10, relevantArea.NorthLatitude + ConstantValues.resolutionCell10, relevantArea.EastLongitude + ConstantValues.resolutionCell10);
                            var places2 = GetGeneratedPlaces(dataLoadArea2);
                            results = MapTiles.DrawAreaMapTileSlippySkia(ref places2, relevantArea, areaHeightDegrees, areaWidthDegrees, true);
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

        [HttpGet]
        [Route("/[controller]/CheckTileExpiration/{PlusCode}/{mode}")]
        public string CheckTileExpiration(string PlusCode, int mode) //For simplicity, maptiles expire after the Date part of a DateTime. Intended for base tiles.
        {
            //I pondered making this a boolean, but the client needs the expiration date to know if it's newer or older than it's version. Not if the server needs to redraw the tile. That happens on load.
            PerformanceTracker pt = new PerformanceTracker("CheckTileExpiration");
            var db = new PraxisContext();
            var mapTileExp = db.MapTiles.Where(m => m.PlusCode == PlusCode && m.mode == mode).Select(m => m.ExpireOn).FirstOrDefault();
            pt.Stop();
            return mapTileExp.ToShortDateString();
        }
    }
}
