using CoreComponents;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

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
        //function to get list of teams
        //function to get current owner of single area
        //function to get total scores for each team.

        [HttpGet]
        [Route("/[controller]/claimArea/{MapDataId}/{factionId}")]
        public bool ClaimArea(long MapDataId, int factionId)
        {
            PraxisContext db = new PraxisContext();
            var teamClaim = db.AreaControlTeams.Where(a => a.FactionId == factionId).FirstOrDefault();
            if (teamClaim == null)
            {
                var mapData = db.MapData.Where(md => md.MapDataId == MapDataId).FirstOrDefault();
                teamClaim = new DbTables.AreaControlTeam();
                teamClaim.MapDataId = MapDataId;
                teamClaim.points = MapSupport.GetScoreForSingleArea(mapData.place);
            }
            teamClaim.FactionId = factionId;
            return false;
        }
    }
}
