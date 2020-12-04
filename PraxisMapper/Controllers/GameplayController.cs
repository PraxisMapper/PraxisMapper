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

namespace PraxisMapper.Controllers
{
    public class GameplayController : Controller
    {
        //GameplayController handles the basic gameplay interactions included with the server
        //this is AreaControl commands for players to take an area, get resources through an action, etc.
        //Note: to stick to our policy of not storing player data on the server side, Area-Control records stored here must be by faction, not individual.
        //(individual area control gameplay can store that info on the device)

        //TODO: 
        //function to get list of areas and their current owner
        //function to get current owner of single area
        //function to get total scores for each team.

        [HttpGet] //TODO this is a POST
        [Route("/[controller]/claimArea/{MapDataId}/{factionId}")]
        public bool ClaimArea(long MapDataId, int factionId)
        {
            PraxisContext db = new PraxisContext();
            var teamClaim = db.AreaControlTeams.Where(a => a.FactionId == factionId).FirstOrDefault();
            if (teamClaim == null)
            {
                var mapData = db.MapData.Where(md => md.MapDataId == MapDataId).FirstOrDefault();
                if (mapData == null)
                {
                    mapData = db.GeneratedMapData.Where(md => md.GeneratedMapDataId == MapDataId).Select(m => new MapData() { MapDataId = m.GeneratedMapDataId, place = m.place }).FirstOrDefault();
                    teamClaim.IsGeneratedArea = true;
                }
                teamClaim = new DbTables.AreaControlTeam();
                teamClaim.MapDataId = MapDataId;
                teamClaim.points = MapSupport.GetScoreForSingleArea(mapData.place);
            }
            teamClaim.FactionId = factionId;
            teamClaim.claimedAt = DateTime.Now;
            db.SaveChanges();
            return true;
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
        [Route("/[controller]/DrawFactionModeCell10HighRes")]
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
            baseImage.Mutate(b => b.DrawImage(controlImage, .5f));
            MemoryStream ms = new MemoryStream();
            baseImage.SaveAsPng(ms);
            
            pt.Stop(Cell10);
            return File(ms.ToArray(), "image/png");
        }
    }
}
