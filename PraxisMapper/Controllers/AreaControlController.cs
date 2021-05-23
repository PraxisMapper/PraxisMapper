using CoreComponents;
using Google.OpenLocationCode;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using NetTopologySuite.Geometries.Prepared;
using PraxisMapper.Classes;
using System;
using System.IO;
using System.Linq;
using static CoreComponents.ConstantValues;
using static CoreComponents.Converters;
using static CoreComponents.DbTables;
using static CoreComponents.Place;
using static CoreComponents.ScoreData;
using static CoreComponents.Singletons;

namespace PraxisMapper.Controllers
{
    [Route("[controller]")]
    [Route("Gameplay")] //backward compatibility with deployed version of apps.
    [ApiController]
    public class AreaControlController : Controller
    {
        //AreaControl is meant to be a more typical baseline for a game. You have a core action (walk places to get point), 
        //which feeds a second action (spend points to claim an area for your team), which feeds a third (have more points than the other team).
        //It's a very simple framework, but it can be built upon.

        private static MemoryCache cache;
        private readonly IConfiguration Configuration;
        public AreaControlController(IConfiguration configuration)
        {
            Configuration = configuration;

            if (cache == null && Configuration.GetValue<bool>("enableCaching") == true)
            {
                var options = new MemoryCacheOptions();
                options.SizeLimit = 1024; //1k entries. that's 2.5 4-digit plus code blocks without roads/buildings. If an entry is 300kb on average, this is 300MB of RAM for caching. T3.small has 2GB total, t3.medium has 4GB.
                cache = new MemoryCache(options);
            }
        }

        //AreaControlController handles the basic gameplay interactions included with the server
        //this is AreaControl commands for players to take an area, get resources through an action, etc.
        //Note: to stick to our policy of not storing player data on the server side, Area-Control records stored here must be by faction, not individual.
        //(individual area control gameplay can store that info on the device)

        [HttpGet]
        [Route("/[controller]/test")]
        [Route("/Gameplay/test")]
        public string controllerterst()
        {
            return "Gameplay Endpoint up";
        }

        [HttpGet] //TODO technically this is a post.
        [Route("/[controller]/claimArea/{MapDataId}/{factionId}")]
        [Route("/Gameplay/claimArea/{MapDataId}/{factionId}")]
        public bool ClaimArea(long MapDataId, int factionId)
        {
            PraxisContext db = new PraxisContext();
            var mapData = db.StoredWays.Where(md => md.id == MapDataId).FirstOrDefault();
            var teamClaim = db.AreaControlTeams.Where(a => a.MapDataId == MapDataId).FirstOrDefault();
            if (teamClaim == null)
            {
                teamClaim = new DbTables.AreaControlTeam();
                db.AreaControlTeams.Add(teamClaim);
                if (mapData == null)
                {
                    //TODO: restore this feature.
                    //mapData = db.GeneratedMapData.Where(md => md.GeneratedMapDataId == MapDataId - 100000000).Select(m => new MapData() { MapDataId = MapDataId, place = m.place }).FirstOrDefault();
                    //teamClaim.IsGeneratedArea = true;
                }
                teamClaim.MapDataId = MapDataId;
                teamClaim.points = GetScoreForSinglePlace(mapData.wayGeometry);
            }
            teamClaim.FactionId = factionId;
            teamClaim.claimedAt = DateTime.Now;
            db.SaveChanges();
            //Tuple<long, int> shortcut = new Tuple<long, int>(MapDataId, factionId); //tell the application later not to hit the DB on every tile for this entry.
            //ExpireAreaControlMapTilesCell8(mapData); //If this works correctly, i can set a much longer default expiration value on AreaControl tiles than 1 minute I currently use.
            MapTiles.ExpireMapTiles(mapData.wayGeometry, 2);
            MapTiles.ExpireSlippyMapTiles(mapData.wayGeometry, 2);
            
            return true;
        }

        //public static void ExpireAreaControlMapTilesCell8(MapData md)
        //{
        //    PerformanceTracker pt = new PerformanceTracker("ExpireAreaControlMapTilesCell8");
        //    var db = new PraxisContext();
        //    var space = md.place.Envelope;
        //    var geoAreaToRefresh = new GeoArea(new GeoPoint(space.Coordinates.Min().Y, space.Coordinates.Min().X), new GeoPoint(space.Coordinates.Max().Y, space.Coordinates.Max().X));
        //    var Cell8XTiles = geoAreaToRefresh.LongitudeWidth / resolutionCell8;
        //    var Cell8YTiles = geoAreaToRefresh.LatitudeHeight / resolutionCell8;

        //    double minx = geoAreaToRefresh.WestLongitude;
        //    double miny = geoAreaToRefresh.SouthLatitude;

        //    var expiringTiles = db.MapTiles.Where(m => m.mode == 2 && m.areaCovered.Intersects(md.place)).ToList();
        //    foreach(var et in expiringTiles)
        //    {
        //        et.ExpireOn = DateTime.Now; //The player is waiting for their claim to process. Mark them done and move on, redraw the map tile on next request to load it.
        //    }
        //    db.SaveChanges();

        //    //int xTiles = (int)Cell8XTiles + 1;
        //    //IPreparedGeometry pg = pgf.Create(md.place); //this is supposed to be faster than regular geometry.
        //    //for (var x = 0; x < xTiles; x++)
        //    //{

        //    //    int yTiles = (int)(Cell8YTiles + 1);
        //    //    for (var y = 0; y < Cell8YTiles; y++)
        //    //    {
        //    //        var db2 = new PraxisContext();
        //    //        var olc = new OpenLocationCode(new GeoPoint(miny + (resolutionCell8 * y), minx + (resolutionCell8 * x)));
        //    //        var olc8 = olc.CodeDigits.Substring(0, 8);
        //    //        var olcPoly = GeoAreaToPolygon(OpenLocationCode.DecodeValid(olc8));
        //    //        if (pg.Intersects(olcPoly))
        //    //        {
        //    //            var maptiles8 = db2.MapTiles.Where(m => m.PlusCode == olc8 && m.resolutionScale == 11 && m.mode == 2).FirstOrDefault();
        //    //            if (maptiles8 != null)
        //    //            {
        //    //                maptiles8.ExpireOn = DateTime.Now.AddDays(-1);
        //    //            }    
        //    //        }
        //    //        db2.SaveChanges();
        //    //    }
        //    //}
        //    pt.Stop(md.MapDataId + "|" + md.name);
        //}

        [HttpGet]
        [Route("/[controller]/GetFactions")]
        [Route("/Gameplay/GetFactions")]
        public string GetFactionInfo()
        {
            //TODO: move this to a general controller, since factions apply to multiple game modes?
            PraxisContext db = new PraxisContext();
            var factions = db.Factions.ToList();
            string results = "";
            foreach (var f in factions)
                results += f.FactionId + "|" + f.Name + Environment.NewLine;

            return results;
        }

        //This code technically works on any Cell size, I haven't yet functionalized it correctly yet.
        [HttpGet]
        [Route("/[controller]/DrawFactionModeCell8HighRes/{Cell8}")]
        [Route("/Gameplay/DrawFactionModeCell8HighRes/{Cell8}")]
        public FileContentResult DrawFactionModeCell8HighRes(string Cell8)
        {
            PerformanceTracker pt = new PerformanceTracker("DrawFactionModeCell8HighRes");
            //We will try to minimize rework done.
            var db = new PraxisContext();
            var baseMapTile = db.MapTiles.Where(mt => mt.PlusCode == Cell8 && mt.resolutionScale == 11 && mt.mode == 1).FirstOrDefault();
            if (baseMapTile == null) //These don't expire, they should be cleared out on data change, or should I check expiration anyways?
            {
                //Create this map tile.
                GeoArea pluscode = OpenLocationCode.DecodeValid(Cell8);
                var places = GetPlaces(pluscode); //, includeGenerated: Configuration.GetValue<bool>("generateAreas") //TODO restore generated area logic.
                var tile = MapTiles.DrawCell8V4(pluscode, places); //MapTiles.DrawAreaMapTileSkia(ref places, pluscode, 11);
                baseMapTile = new MapTile() { CreatedOn = DateTime.Now, mode = 1, PlusCode = Cell8, resolutionScale = 11, tileData = tile, areaCovered = Converters.GeoAreaToPolygon(pluscode) };
                db.MapTiles.Add(baseMapTile);
                db.SaveChanges();
            }

            var factionColorTile = db.MapTiles.Where(mt => mt.PlusCode == Cell8 && mt.resolutionScale == 11 && mt.mode == 2).FirstOrDefault();
            if (factionColorTile == null || factionColorTile.MapTileId == null || factionColorTile.ExpireOn < DateTime.Now)
            {
                //Draw this entry
                //requires a list of colors to use, which might vary per app
                GeoArea CellEightArea = OpenLocationCode.DecodeValid(Cell8);
                var places = GetPlaces(CellEightArea); // , includeGenerated: Configuration.GetValue<bool>("generateAreas") TODO restore generated area logic.
                var results = MapTiles.DrawAreaAtSizeV4(CellEightArea, 80, 100, places, TagParser.teams); //MapTiles.DrawMPControlAreaMapTileSkia(CellEightArea, 11); //TODO replacing this one requires multiple style list support.
                if (factionColorTile == null) //create a new entry
                {
                    factionColorTile = new MapTile() { PlusCode = Cell8, CreatedOn = DateTime.Now, mode = 2, resolutionScale = 11, tileData = results, areaCovered = Converters.GeoAreaToPolygon(CellEightArea) };
                    db.MapTiles.Add(factionColorTile);
                }
                else //update the existing entry.
                {
                    factionColorTile.tileData = results;
                    factionColorTile.ExpireOn = DateTime.Now.AddMinutes(1);
                    factionColorTile.CreatedOn = DateTime.Now;
                }
                db.SaveChanges();
            }

            //Some image items setup.
            //hard-coded, the size of a Cell8 with res11 is 80x100
            SkiaSharp.SKBitmap bitmap = new SkiaSharp.SKBitmap(80, 100, SkiaSharp.SKColorType.Rgba8888, SkiaSharp.SKAlphaType.Premul);
            SkiaSharp.SKCanvas canvas = new SkiaSharp.SKCanvas(bitmap);
            SkiaSharp.SKPaint paint = new SkiaSharp.SKPaint();
            paint.IsAntialias = true;

            var baseBmp = SkiaSharp.SKBitmap.Decode(baseMapTile.tileData);
            var areaControlOverlay = SkiaSharp.SKBitmap.Decode(factionColorTile.tileData);
            canvas.DrawBitmap(baseBmp, 0, 0);
            canvas.DrawBitmap(areaControlOverlay, 0, 0);
            var ms = new MemoryStream();
            var skms = new SkiaSharp.SKManagedWStream(ms);
            bitmap.Encode(skms, SkiaSharp.SKEncodedImageFormat.Png, 100);
            var output = ms.ToArray();
            skms.Dispose(); ms.Close(); ms.Dispose();

            pt.Stop(Cell8);
            return File(output, "image/png");
        }

        [HttpGet]
        [Route("/[controller]/FindChangedMapTiles/{mapDataId}")]
        [Route("/Gameplay/FindChangedMapTiles/{mapDataId}")]
        public string FindChangedMapTiles(long mapDataId)
        {
            PerformanceTracker pt = new PerformanceTracker("FindChangedMapTiles");
            string results = "";
            //return a separated list of maptiles that need updated.
            var db = new PraxisContext();
            var md = db.StoredWays.Where(m => m.id == mapDataId).FirstOrDefault();
            var space = md.wayGeometry.Envelope;
            var geoAreaToRefresh = new GeoArea(new GeoPoint(space.Coordinates.Min().Y, space.Coordinates.Min().X), new GeoPoint(space.Coordinates.Max().Y, space.Coordinates.Max().X));
            var Cell8XTiles = geoAreaToRefresh.LongitudeWidth / resolutionCell8;
            var Cell8YTiles = geoAreaToRefresh.LatitudeHeight / resolutionCell8;
            for (var x = 0; x < Cell8XTiles; x++)
            {
                for (var y = 0; y < Cell8YTiles; y++)
                {
                    var olc = new OpenLocationCode(new GeoPoint(geoAreaToRefresh.SouthLatitude + (resolutionCell8 * y), geoAreaToRefresh.WestLongitude + (resolutionCell8 * x)));
                    var olcPoly = Converters.GeoAreaToPolygon(olc.Decode());
                    if (md.wayGeometry.Intersects(olcPoly)) //If this intersects the original way, redraw that tile. Lets us minimize work for oddly-shaped areas.
                    {
                        results += olc.CodeDigits + "|";
                    }
                }
            }
            pt.Stop();
            return results;
        }

        [HttpGet]
        [Route("/[controller]/AreaOwners/{mapDataId}")]
        [Route("/Gameplay/AreaOwners/{mapDataId}")]
        public string AreaOwners(long mapDataId)
        {
            //App asks to see which team owns this areas.
            //Return the area name (or type if unnamed), the team that owns it, and its point cost (pipe-separated)
            PerformanceTracker pt = new PerformanceTracker("AreaOwners");
            var db = new PraxisContext();
            var owner = db.AreaControlTeams.Where(a => a.MapDataId == mapDataId).FirstOrDefault();
            var mapData = db.StoredWays.Where(m => m.id == mapDataId).FirstOrDefault();
            //if (mapData == null && (Configuration.GetValue<bool>("generateAreas") == true)) //TODO restore this feature.
                //mapData = db.GeneratedMapData.Where(m => m.GeneratedMapDataId == mapDataId - 100000000).Select(g => new MapData() { MapDataId = g.GeneratedMapDataId + 100000000, place = g.place, type = g.type, name = g.name, AreaTypeId = g.AreaTypeId }).FirstOrDefault();
            if (mapData == null)
                //This is an error, probably from not clearing data between server instances.
                return "MissingArea|Nobody|0";

            //if (string.IsNullOrWhiteSpace(mapData.name))
                //mapData.name = areaIdReference[mapData.AreaTypeId].FirstOrDefault();

            if (owner != null)
            {
                var factionName = db.Factions.Where(f => f.FactionId == owner.FactionId).FirstOrDefault().Name;
                pt.Stop();
                return mapData.name + "|" + factionName + "|" + GetScoreForSinglePlace(mapData.wayGeometry);
            }
            else
            {
                pt.Stop();
                return mapData.name + "|Nobody|" + GetScoreForSinglePlace(mapData.wayGeometry);
            }
        }

        [HttpGet]
        [Route("/[controller]/FactionScores")]
        [Route("/Gameplay/FactionScores")]
        public string FactionScores()
        {
            PerformanceTracker pt = new PerformanceTracker("FactionScores");
            var db = new PraxisContext();
            var teamNames = db.Factions.ToLookup(k => k.FactionId, v => v.Name);
            var scores = db.AreaControlTeams.GroupBy(a => a.FactionId).Select(a => new { team = a.Key, score = a.Sum(aa => aa.points), teamName = teamNames[a.Key] }).ToList();

            var results = string.Join(Environment.NewLine, scores.Select(s => s.team + "|" + s.teamName + "|" + s.score));
            pt.Stop();
            return results;
        }
    }
}
