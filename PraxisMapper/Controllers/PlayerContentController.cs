using CoreComponents;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Linq;

namespace PraxisMapper.Controllers
{
    [Route("[controller]")]
    [ApiController]
    public class PlayerContentController : Controller
    {

        //A future concept
        //Have endpoints designed to handle things the player can edit on the map, for other players to interact with.

        //Out of scope intially. First game or two will not need this.
        //Making the file to remember this idea.

        //Will need db tables for this?
        //this probably stores some baseline data, and perhaps an ID for an object for another program to pull from a DB.
        //Yeah, this might be too game-specific to set up as a baseline past that.
        //Are these separate from the GenereatedMapData entries? I think so.

        [HttpGet]
        [Route("/[controller]/AssignTeam/{deviceID}")]
        public long AssignTeam(string deviceID)
        {
            try
            {
                //Which team are you on per instance? Assigns new players to the team with the smallest membership at the time.
                Classes.PerformanceTracker pt = new Classes.PerformanceTracker("AssignTeam");

                var db = new PraxisContext();
                var player = db.PlayerData.Where(p => p.deviceID == deviceID).FirstOrDefault();
                if (player == null)
                {
                    player = new DbTables.PlayerData();
                    player.deviceID = deviceID;
                    player.DisplayName = "";
                    player.FactionId = 0;
                    db.PlayerData.Add(player);
                }
                var factions = db.Factions.ToList();
                if (factions.Any(f => f.FactionId == player.FactionId))
                {
                    pt.Stop();
                    return player.FactionId;
                }

                var smallestTeam = db.PlayerData
                    .GroupBy(ta => ta.FactionId)
                    .Select(ta => new { team = ta.Key, members = ta.Count() })
                    .OrderBy(ta => ta.members)
                    .First().team;

                if (smallestTeam == null || smallestTeam == 0)
                    smallestTeam = factions.First().FactionId;

                player.FactionId = smallestTeam;
                db.SaveChanges();
                pt.Stop(deviceID + "|" + smallestTeam);
                return smallestTeam;
            }
            catch(Exception ex)
            {
                Classes.ErrorLogger.LogError(ex);
                return 0;
            }
        }

        [HttpGet]
        [Route("/[controller]/SetTeam/{deviceID}/{factionID}")]
        public int SetTeam(string deviceID, int factionID)
        {
            //An alternative config option, to let players pick teams and have it stored on the server. Provided for testing and alternative play models.
            Classes.PerformanceTracker pt = new Classes.PerformanceTracker("AssignTeam");
            var db = new PraxisContext();
            var player = db.PlayerData.Where(p => p.deviceID == deviceID).FirstOrDefault();
            //var teamEntry = GetTeamAssignment(deviceID, instanceID);
            //db.Attach(teamEntry);
            player.FactionId = factionID;
            db.SaveChanges();

            pt.Stop(deviceID + "|" + factionID);
            return factionID;
        }

    }
}
