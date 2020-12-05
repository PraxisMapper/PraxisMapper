using CoreComponents;
using Google.OpenLocationCode;
using Microsoft.AspNetCore.Mvc;
using PraxisMapper.Classes;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using static CoreComponents.DbTables;
using SixLabors.ImageSharp.Processing;
using System.IO;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;

namespace PraxisMapper.Controllers
{
    [Route("[controller]")]
    [ApiController]
    public class GameplayController : Controller
    {
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
        //function to get list of areas and their current owner
        //function to get current owner of single area
        //function to get total scores for each team.
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
                    mapData = db.GeneratedMapData.Where(md => md.GeneratedMapDataId == MapDataId).Select(m => new MapData() { MapDataId = m.GeneratedMapDataId, place = m.place }).FirstOrDefault();
                    teamClaim.IsGeneratedArea = true;
                }
                teamClaim.MapDataId = MapDataId;
                teamClaim.points = MapSupport.GetScoreForSingleArea(mapData.place);
            }
            teamClaim.FactionId = factionId;
            teamClaim.claimedAt = DateTime.Now;
            db.SaveChanges();
            //TODO: async up some new map tiles for areas that contain this?
            //How to identify all plus codes that contain an area?
            //should be a new function, but rules probably look like:
            //get envelope of a geometry item.
            //check width/height of envelope against resolution values to see how big it is.
            //(remember, if its less than 1 cell resolution wide, it may still cross 2 of that resolution cell if its by the boundaries.
            //ask for redraw of all map tiles within those cells, and all sub-cells?
            Task.Factory.StartNew(() => RedoAreaControlMapTiles(mapData)); //I don't need this to finish to return, should let it run in the background
            return true;
        }

        public static void RedoAreaControlMapTiles(MapData md)
        {
            var db = new PraxisContext();
            var space = md.place.Envelope;
            var geoAreaToRefresh = new GeoArea(new GeoPoint(space.Coordinates.Min().Y, space.Coordinates.Min().X), new GeoPoint(space.Coordinates.Max().Y, space.Coordinates.Max().X));
            var Cell10XTiles = geoAreaToRefresh.LongitudeWidth / MapSupport.resolutionCell10;
            var Cell10YTiles = geoAreaToRefresh.LatitudeHeight / MapSupport.resolutionCell10;
            for (var x = 0; x < Cell10XTiles; x++)
            {
                for (var y = 0; y < Cell10YTiles; y++)
                {
                    var olc = new OpenLocationCode(new GeoPoint(geoAreaToRefresh.SouthLatitude + (MapSupport.resolutionCell10 * y), geoAreaToRefresh.WestLongitude + (MapSupport.resolutionCell10 * x)));
                    var olcPoly = MapSupport.GeoAreaToPolygon(olc.Decode());
                    if (md.place.Intersects(olcPoly)) //If this intersects the original way, redraw that tile. Lets us minimize work for oddly-shaped areas.
                    {
                        var maptiles10 = db.MapTiles.Where(m => m.PlusCode == olc.CodeDigits && m.resolutionScale == 11 && m.mode == 2).FirstOrDefault();
                        if (maptiles10 == null)
                        {
                            maptiles10 = new MapTile() { mode = 2, PlusCode = olc.CodeDigits, resolutionScale = 11 };
                            db.MapTiles.Add(maptiles10);
                        }

                        maptiles10.tileData = MapSupport.DrawMPControlAreaMapTile11(olc.Decode());
                        maptiles10.CreatedOn = DateTime.Now;
                    }
                }
            }
            db.SaveChanges();
        }

        [HttpGet]
        [Route("/[controller]/GetFactions")]
        public string GetFactionInfo()
        {
            PraxisContext db = new PraxisContext();
            var factions = db.Factions.ToList();
            string results = "";
            foreach (var f in factions)
                results += f.FactionId + "|" + f.Name + Environment.NewLine;

            return results;
        }

        //This code technically works on any Cell size.
        [HttpGet]
        [Route("/[controller]/DrawFactionModeCell10HighRes/{Cell10}")]
        public FileContentResult DrawFactionModeCell10HighRes(string Cell10)
        {
            PerformanceTracker pt = new PerformanceTracker("DrawFactionModeCell10HighRes");
            //We will try to minimize rework done.
            var db = new PraxisContext();
            var baseMapTile = db.MapTiles.Where(mt => mt.PlusCode == Cell10 && mt.resolutionScale == 11 && mt.mode == 1).FirstOrDefault();
            if (baseMapTile == null)
            {
                //Create this map tile.
                GeoArea pluscode = OpenLocationCode.DecodeValid(Cell10);
                var places = MapSupport.GetPlaces(pluscode);
                var tile = MapSupport.DrawAreaMapTile11(ref places, pluscode);
                baseMapTile = new MapTile() { CreatedOn = DateTime.Now, mode = 1, PlusCode = Cell10, resolutionScale = 11, tileData = tile };
                db.MapTiles.Add(baseMapTile);
                db.SaveChanges();
            }

            var factionColorTile = db.MapTiles.Where(mt => mt.PlusCode == Cell10 && mt.resolutionScale == 11 && mt.mode == 2).FirstOrDefault();
            if (factionColorTile == null || factionColorTile.MapTileId == null)
            {
                //Create this entry
                //requires a list of colors to use, which might vary per app
                GeoArea TenCell = OpenLocationCode.DecodeValid(Cell10);
                var places = MapSupport.GetPlaces(TenCell);
                var results = MapSupport.DrawMPControlAreaMapTile11(TenCell);
                factionColorTile = new MapTile() { PlusCode = Cell10, CreatedOn = DateTime.Now, mode = 2, resolutionScale = 11, tileData = results };
                db.MapTiles.Add(factionColorTile);
                db.SaveChanges();
            }

            var baseImage = Image.Load(baseMapTile.tileData);
            var controlImage = Image.Load(factionColorTile.tileData);
            baseImage.Mutate(b => b.DrawImage(controlImage, .6f));
            MemoryStream ms = new MemoryStream();
            baseImage.SaveAsPng(ms);
            
            pt.Stop(Cell10);
            return File(ms.ToArray(), "image/png");
        }

        public static void FindChangedMapTiles(DateTime sinceThisTime, string plusCodeToSearch)
        {
            //Ask what tiles I need to refresh, tell the server when the last time I asked was.
            //Does this include tiles i deleted? I might need to pass in a pluscode to limit the search too.

        }

        [HttpGet]
        [Route("/[controller]/AreaOwners/{mapDataId}")]
        public string AreaOwners(long mapDataId)
        {
            //App asks to see which team owns this areas.
            //Return the area name (or type if unnamed), the team that owns it, and its point cost (pipe-separated)
            var db = new PraxisContext();
            string results = "";
            var owner = db.AreaControlTeams.Where(a => a.MapDataId == mapDataId).FirstOrDefault();
            var mapData = db.MapData.Where(m => m.MapDataId == mapDataId).FirstOrDefault();
            if (string.IsNullOrWhiteSpace(mapData.name))
                mapData.name = MapSupport.areaIdReference[mapData.AreaTypeId].FirstOrDefault();

            if (owner != null)
            {
                var factionName = db.Factions.Where(f => f.FactionId == owner.FactionId).FirstOrDefault().Name;
                return mapData.name + "|" + factionName + "|" + MapSupport.GetScoreForSingleArea(mapData.place);
            }
            else
            {
                return mapData.name + "|Nobody|" + MapSupport.GetScoreForSingleArea(mapData.place);
            }
        }
    }
}
