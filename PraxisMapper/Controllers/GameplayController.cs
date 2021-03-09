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
using System.Threading.Tasks;
using static CoreComponents.ConstantValues;
using static CoreComponents.Converters;
using static CoreComponents.DbTables;
using static CoreComponents.Place;
using static CoreComponents.ScoreData;
using static CoreComponents.Singletons;

namespace PraxisMapper.Controllers
{
    [Route("[controller]")]
    [ApiController]
    public class GameplayController : Controller //TODO: rename 'Gameplay' to 'AreaControl'
    {
        //AreaControl is meant to be a more typical baseline for a game. You have a core action (walk places to get point), 
        //which feeds a second action (spend points to claim an area for your team), which feeds a third (have more points than the other team).
        //It's a very simple framework, but it can be built upon.

        private static MemoryCache cache;
        private readonly IConfiguration Configuration;
        public GameplayController(IConfiguration configuration)
        {
            Configuration = configuration;

            if (cache == null && Configuration.GetValue<bool>("enableCaching") == true)
            {
                var options = new MemoryCacheOptions();
                options.SizeLimit = 1024; //1k entries. that's 2.5 4-digit plus code blocks without roads/buildings. If an entry is 300kb on average, this is 300MB of RAM for caching. T3.small has 2GB total, t3.medium has 4GB.
                cache = new MemoryCache(options);
            }
        }

        //GameplayController handles the basic gameplay interactions included with the server
        //this is AreaControl commands for players to take an area, get resources through an action, etc.
        //Note: to stick to our policy of not storing player data on the server side, Area-Control records stored here must be by faction, not individual.
        //(individual area control gameplay can store that info on the device)

        //TODO: 
        //Make sure that I don't delete map tiles, but force-update them. if i delete them, apps will have a much harder time knowing when to update their cached copies.

        [HttpGet] //TODO this is a POST
        [Route("/[controller]/test")]
        public string controllerterst()
        {
            return "Gameplay Endpoint up";
        }

        [HttpGet] //TODO this is a POST
        [Route("/[controller]/claimArea/{MapDataId}/{factionId}")]
        public bool ClaimArea(long MapDataId, int factionId)
        {
            PraxisContext db = new PraxisContext();
            var mapData = db.MapData.Where(md => md.MapDataId == MapDataId).FirstOrDefault();
            var teamClaim = db.AreaControlTeams.Where(a => a.MapDataId == MapDataId).FirstOrDefault();
            if (teamClaim == null)
            {
                teamClaim = new DbTables.AreaControlTeam();
                db.AreaControlTeams.Add(teamClaim);
                if (mapData == null)
                {
                    mapData = db.GeneratedMapData.Where(md => md.GeneratedMapDataId == MapDataId - 100000000).Select(m => new MapData() { MapDataId = MapDataId, place = m.place }).FirstOrDefault();
                    teamClaim.IsGeneratedArea = true;
                }
                teamClaim.MapDataId = MapDataId;
                teamClaim.points = GetScoreForSinglePlace(mapData.place);
            }
            teamClaim.FactionId = factionId;
            teamClaim.claimedAt = DateTime.Now;
            db.SaveChanges();
            Tuple<long, int> shortcut = new Tuple<long, int>(MapDataId, factionId); //tell the application later not to hit the DB on every tile for this entry.
            //Task.Factory.StartNew(() => RedoAreaControlMapTilesCell8(mapData, shortcut)); //I don't need this to finish to return, let it run in the background
            ExpireAreaControlMapTilesCell8(mapData, shortcut); //If this works correctly, i can set a much longer default expiration value on AreaControl tiles than 1 minute I currently use.
            
            return true;
        }

        public static void ExpireAreaControlMapTilesCell8(MapData md, Tuple<long, int> shortcut = null)
        {
            //This is a background task, but we want it to finish as fast as possible. 
            PerformanceTracker pt = new PerformanceTracker("ExpireAreaControlMapTilesCell8");
            var db = new PraxisContext();
            var space = md.place.Envelope;
            var geoAreaToRefresh = new GeoArea(new GeoPoint(space.Coordinates.Min().Y, space.Coordinates.Min().X), new GeoPoint(space.Coordinates.Max().Y, space.Coordinates.Max().X));
            var Cell8XTiles = geoAreaToRefresh.LongitudeWidth / resolutionCell8;
            var Cell8YTiles = geoAreaToRefresh.LatitudeHeight / resolutionCell8;

            double minx = geoAreaToRefresh.WestLongitude;
            double miny = geoAreaToRefresh.SouthLatitude;

            int xTiles = (int)Cell8XTiles + 1;
            IPreparedGeometry pg = pgf.Create(md.place); //this is supposed to be faster than regular geometry.
            Parallel.For(0, xTiles, (x) => //We don't usually want parallel actions on a web server, but this is a high priority and this does cut the runtime in about half.
            //for (var x = 0; x < Cell10XTiles; x++)
            {

                int yTiles = (int)(Cell8YTiles + 1);
                Parallel.For(0, yTiles, (y) => //We don't usually want parallel actions on a web server, but this is a high priority
                //for (var y = 0; y < Cell10YTiles; y++)
                {
                    var db2 = new PraxisContext();
                    var olc = new OpenLocationCode(new GeoPoint(miny + (resolutionCell8 * y), minx + (resolutionCell8 * x)));
                    var olc8 = olc.CodeDigits.Substring(0, 8);
                    var olcPoly = GeoAreaToPolygon(OpenLocationCode.DecodeValid(olc8));
                    //if (md.place.Intersects(olcPoly)) //If this intersects the original way, redraw that tile. Lets us minimize work for oddly-shaped areas.
                    if (pg.Intersects(olcPoly))
                    {
                        var maptiles8 = db2.MapTiles.Where(m => m.PlusCode == olc8 && m.resolutionScale == 11 && m.mode == 2).FirstOrDefault();
                    //old, redraw logic
                    //if (maptiles8 == null)
                    //{
                    //    maptiles8 = new MapTile() { mode = 2, PlusCode = olc8, resolutionScale = 11 };
                    //    db2.MapTiles.Add(maptiles8);
                    //}

                    //maptiles8.tileData = MapTiles.DrawMPControlAreaMapTile(olc.Decode(), 11, shortcut);
                    //maptiles8.CreatedOn = DateTime.Now;
                    //db2.SaveChanges();

                    //new, expire to redraw later logic
                        if (maptiles8 != null)
                        {
                            maptiles8.ExpireOn = DateTime.Now.AddDays(-1);
                        }    
                    }
                    db2.SaveChanges();
                });
            });

            pt.Stop(md.MapDataId + "|" + md.name);
        }

        [HttpGet]
        [Route("/[controller]/GetFactions")]
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

        //[HttpGet]
        //[Route("/[controller]/DrawFactionModeCell10HighRes/{Cell10}")]
        //public FileContentResult DrawFactionModeCell10HighRes(string Cell10)
        //{
        //    PerformanceTracker pt = new PerformanceTracker("DrawFactionModeCell10HighRes");
        //    //We will try to minimize rework done.
        //    var db = new PraxisContext();
        //    var baseMapTile = db.MapTiles.Where(mt => mt.PlusCode == Cell10 && mt.resolutionScale == 11 && mt.mode == 1).FirstOrDefault();
        //    if (baseMapTile == null)
        //    {
        //        //Create this map tile.
        //        GeoArea pluscode = OpenLocationCode.DecodeValid(Cell10);
        //        var places = GetPlaces(pluscode, null);
        //        var tile = MapTiles.DrawAreaMapTile(ref places, pluscode, 11);
        //        baseMapTile = new MapTile() { CreatedOn = DateTime.Now, mode = 1, PlusCode = Cell10, resolutionScale = 11, tileData = tile };
        //        db.MapTiles.Add(baseMapTile);
        //        db.SaveChanges();
        //    }

        //    var factionColorTile = db.MapTiles.Where(mt => mt.PlusCode == Cell10 && mt.resolutionScale == 11 && mt.mode == 2).FirstOrDefault();
        //    if (factionColorTile == null || factionColorTile.MapTileId == null)
        //    {
        //        //Create this entry
        //        //requires a list of colors to use, which might vary per app
        //        GeoArea TenCell = OpenLocationCode.DecodeValid(Cell10);
        //        var places = GetPlaces(TenCell);
        //        var results = MapTiles.DrawMPControlAreaMapTile(TenCell, 11);
        //        factionColorTile = new MapTile() { PlusCode = Cell10, CreatedOn = DateTime.Now, mode = 2, resolutionScale = 11, tileData = results };
        //        db.MapTiles.Add(factionColorTile);
        //        db.SaveChanges();
        //    }

        //    var baseImage = Image.Load(baseMapTile.tileData);
        //    var controlImage = Image.Load(factionColorTile.tileData);
        //    baseImage.Mutate(b => b.DrawImage(controlImage, .6f));
        //    MemoryStream ms = new MemoryStream();
        //    baseImage.SaveAsPng(ms);

        //    pt.Stop(Cell10);
        //    return File(ms.ToArray(), "image/png");
        //}

        //This code technically works on any Cell size, I haven't yet functionalized it corre.
        [HttpGet]
        [Route("/[controller]/DrawFactionModeCell8HighRes/{Cell8}")]
        public FileContentResult DrawFactionModeCell8HighRes(string Cell8)
        {
            PerformanceTracker pt = new PerformanceTracker("DrawFactionModeCell8HighRes");
            //We will try to minimize rework done.
            var db = new PraxisContext();
            var baseMapTile = db.MapTiles.Where(mt => mt.PlusCode == Cell8 && mt.resolutionScale == 11 && mt.mode == 1).FirstOrDefault();
            if (baseMapTile == null) //These don't expire, they should be cleared out on data change.
            {
                //Create this map tile.
                GeoArea pluscode = OpenLocationCode.DecodeValid(Cell8);
                var places = GetPlaces(pluscode);
                var tile = MapTiles.DrawAreaMapTileSkia(ref places, pluscode, 11);
                baseMapTile = new MapTile() { CreatedOn = DateTime.Now, mode = 1, PlusCode = Cell8, resolutionScale = 11, tileData = tile };
                db.MapTiles.Add(baseMapTile);
                db.SaveChanges();
            }

            var factionColorTile = db.MapTiles.Where(mt => mt.PlusCode == Cell8 && mt.resolutionScale == 11 && mt.mode == 2).FirstOrDefault();
            if (factionColorTile == null || factionColorTile.MapTileId == null || factionColorTile.ExpireOn < DateTime.Now)
            {
                //Draw this entry
                //requires a list of colors to use, which might vary per app
                GeoArea CellEightArea = OpenLocationCode.DecodeValid(Cell8);
                var places = GetPlaces(CellEightArea);
                var results = MapTiles.DrawMPControlAreaMapTileSkia(CellEightArea, 11);
                if (factionColorTile == null) //create a new entry
                {
                    factionColorTile = new MapTile() { PlusCode = Cell8, CreatedOn = DateTime.Now, mode = 2, resolutionScale = 11, tileData = results };
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

            //TODO: switch this out for SkiaSharp, should be faster.
            //var baseImage = Image.Load(baseMapTile.tileData);
            //var controlImage = Image.Load(factionColorTile.tileData);
            //baseImage.Mutate(b => b.DrawImage(controlImage, .6f));
            //MemoryStream ms = new MemoryStream();
            //baseImage.SaveAsPng(ms);

            //create a new image. TODO: test and confirm this functions as expected.
            //Some image items setup.
            //hard-coded, the size of a Cell8 with res11 is 80x100
            SkiaSharp.SKBitmap bitmap = new SkiaSharp.SKBitmap(80, 100, SkiaSharp.SKColorType.Rgba8888, SkiaSharp.SKAlphaType.Premul);
            //SkiaSharp.SKSurface surface = SkiaSharp.SKSurface.Create(bitmap);
            SkiaSharp.SKCanvas canvas = new SkiaSharp.SKCanvas(bitmap);
            //var bgColor = new SkiaSharp.SKColor();
            //canvas.Clear();
            //canvas.Scale(1, -1, imageSizeX / 2, imageSizeY / 2);
            SkiaSharp.SKPaint paint = new SkiaSharp.SKPaint();
            SkiaSharp.SKColor color = new SkiaSharp.SKColor();
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
        public string FindChangedMapTiles(long mapDataId)
        {
            PerformanceTracker pt = new PerformanceTracker("FindChangedMapTiles");
            string results = "";
            //return a separated list of maptiles that need updated.
            var db = new PraxisContext();
            var md = db.MapData.Where(m => m.MapDataId == mapDataId).FirstOrDefault();
            var space = md.place.Envelope;
            var geoAreaToRefresh = new GeoArea(new GeoPoint(space.Coordinates.Min().Y, space.Coordinates.Min().X), new GeoPoint(space.Coordinates.Max().Y, space.Coordinates.Max().X));
            var Cell8XTiles = geoAreaToRefresh.LongitudeWidth / resolutionCell8;
            var Cell8YTiles = geoAreaToRefresh.LatitudeHeight / resolutionCell8;
            for (var x = 0; x < Cell8XTiles; x++)
            {
                for (var y = 0; y < Cell8YTiles; y++)
                {
                    var olc = new OpenLocationCode(new GeoPoint(geoAreaToRefresh.SouthLatitude + (resolutionCell8 * y), geoAreaToRefresh.WestLongitude + (resolutionCell8 * x)));
                    var olcPoly = Converters.GeoAreaToPolygon(olc.Decode());
                    if (md.place.Intersects(olcPoly)) //If this intersects the original way, redraw that tile. Lets us minimize work for oddly-shaped areas.
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
        public string AreaOwners(long mapDataId)
        {
            //App asks to see which team owns this areas.
            //Return the area name (or type if unnamed), the team that owns it, and its point cost (pipe-separated)
            PerformanceTracker pt = new PerformanceTracker("AreaOwners");
            var db = new PraxisContext();
            string results = "";
            var owner = db.AreaControlTeams.Where(a => a.MapDataId == mapDataId).FirstOrDefault();
            var mapData = db.MapData.Where(m => m.MapDataId == mapDataId).FirstOrDefault();
            if (mapData == null && (Configuration.GetValue<bool>("generateAreas") == true))
                mapData = db.GeneratedMapData.Where(m => m.GeneratedMapDataId == mapDataId - 100000000).Select(g => new MapData() { MapDataId = g.GeneratedMapDataId + 100000000, place = g.place, type = g.type, name = g.name, AreaTypeId = g.AreaTypeId }).FirstOrDefault();
            if (mapData == null)
                //This is an error, probably from not clearing data between server instances.
                return "MissingArea|Nobody|0";

            if (string.IsNullOrWhiteSpace(mapData.name))
                mapData.name = areaIdReference[mapData.AreaTypeId].FirstOrDefault();

            if (owner != null)
            {
                var factionName = db.Factions.Where(f => f.FactionId == owner.FactionId).FirstOrDefault().Name;
                pt.Stop();
                return mapData.name + "|" + factionName + "|" + GetScoreForSinglePlace(mapData.place);
            }
            else
            {
                pt.Stop();
                return mapData.name + "|Nobody|" + GetScoreForSinglePlace(mapData.place);
            }
        }

        [HttpGet]
        [Route("/[controller]/FactionScores")]
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
