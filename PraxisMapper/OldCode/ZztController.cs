using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Google.OpenLocationCode;
using CoreComponents;
using NetTopologySuite.Geometries;
using System.Text;
using PraxisMapper.Classes;

namespace PraxisMapper.Controllers
{
    //ZZT Controller is meant for players making their own, old school ZZT style games on the map. Real world terrains would let game pieces
    //change their own behavior, though I will probably allow players to add in their own terrain pieces on top of the real map.

    [Route("api/[controller]")]
    [ApiController]
    public class ZztController : Controller
    {
        private readonly IConfiguration Configuration;

        public ZztController(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        [HttpGet]
        [Route("/[controller]/GetGamesInArea/{Cell6}")]
        public string GetGamesInArea(string Cell6)
        {
            //Loads up all games in the given Cell6 area.
            PerformanceTracker pt = new PerformanceTracker("GetGamesInArea");

            var db = new PraxisContext();
            Geometry lookup = Converters.GeoAreaToPolygon(OpenLocationCode.DecodeValid(Cell6));
            var games = db.ZztGames.Where(z => lookup.Intersects(z.gameLocation)).ToList();

            //Game info format: 
            //id|sPoint|wPoint|nPoint|ePoint~

            StringBuilder sb = new StringBuilder();
            foreach(var game in games)
            {
                sb.Append(game.id + "|" + game.gameLocation.Coordinates.Min(c => c.Y) + "|" + game.gameLocation.Coordinates.Min(c => c.X) + "|" + game.gameLocation.Coordinates.Max(c => c.Y) + "|" + game.gameLocation.Coordinates.Max(c => c.X) + "~");
            }

            pt.Stop();
            return sb.ToString();
        }

        [HttpGet]
        [Route("/[controller]/GetGameData/{gameId}")]
        public string GetGameData(long gameId)
        {
            PerformanceTracker pt = new PerformanceTracker("GetGameData");
            var db = new PraxisContext();
            var game = db.ZztGames.Where(z => z.id == gameId).FirstOrDefault();
            pt.Stop();
            return game.gameData;
        }

        [HttpGet]
        [Route("/[controller]/RecordVictory/{playerId}/{gameId}")]
        public bool RecordVictory(long playerId, long gameId)
        {
            PerformanceTracker pt = new PerformanceTracker("RecordVictory");
            var db = new PraxisContext();
            var record = db.GamesBeaten.Where(g => g.UserId == playerId && g.ZztGameId == gameId).FirstOrDefault();
            if (record == null)
            {
                db.GamesBeaten.Add(new DbTables.GamesBeaten() { UserId = playerId, ZztGameId = gameId });
                db.SaveChanges();
                pt.Stop();
                return true;
            }
            pt.Stop();
            return false;
        }

        public string GetGameTerrainInfo(long gameId)
        {
            PerformanceTracker pt = new PerformanceTracker("GetGameTerrainInfo");
            //This is the same data format at LearnCell8, but for a game in an arbitrary location.
            var db = new PraxisContext();
            var game = db.ZztGames.Where(z => z.id == gameId).First();
            var geoArea = Converters.GeometryToGeoArea(game.gameLocation);
            var terrain = Place.GetPlaces(geoArea);
            var textData = AreaTypeInfo.SearchArea(ref geoArea, ref terrain);
            pt.Stop();
            return textData.ToString();

        }
    }
}
