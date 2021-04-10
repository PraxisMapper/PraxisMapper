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

namespace PraxisMapper.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ZztController : Controller
    {
        private readonly IConfiguration Configuration;

        public ZztController(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public string GetGamesInArea(string Cell6)
        {
            //Loads up all games in the given Cell6 area.

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
            
            return sb.ToString();
        }

        public string GetGameData(long gameId)
        {
            var db = new PraxisContext();
            var game = db.ZztGames.Where(z => z.id == gameId).FirstOrDefault();
            return game.gameData;
        }

        public bool RecordVictory(long playerId, long gameId)
        {
            var db = new PraxisContext();
            var record = db.GamesBeaten.Where(g => g.UserId == playerId && g.ZztGameId == gameId).FirstOrDefault();
            if (record == null)
            {
                db.GamesBeaten.Add(new DbTables.GamesBeaten() { UserId = playerId, ZztGameId = gameId });
                db.SaveChanges();
                return true;
            }
            return false;
        }
    }
}
