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
        //TODO: replace this with GenericData calls from the appropriate games as proper demonstration of how to do this.
        //This controller will be removed in the future then. Finally.

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
                var player = GenericData.GetPlayerData(deviceID, "DisplayName");
                var playerFaction = GenericData.GetPlayerData(deviceID, "FactionId");
                
                var factions = db.Factions.ToList(); //TODO: factions should become arbitrary data or hard-coded to the controller.
                if (factions.Any(f => f.FactionId.ToString() == playerFaction))
                {
                    pt.Stop();
                    return playerFaction.ToLong();
                }

                long smallestTeam = 0;
                List<Tuple<long, int>>teamSizes = new System.Collections.Generic.List<Tuple<long, int>>();
                foreach(var f in factions)
                {
                    var teamSize = db.PlayerData.Count(p => p.dataKey == "FactionId" && p.dataValue == f.FactionId.ToString());
                    teamSizes.Add(Tuple.Create(f.FactionId, teamSize));
                }

                smallestTeam = teamSizes.OrderBy(t => t.Item2).First().Item1;

                GenericData.SetPlayerData(deviceID, "FactionId", smallestTeam.ToString());
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
            GenericData.SetPlayerData(deviceID, "FactionId", factionID.ToString());
            //var db = new PraxisContext();
            //var player = db.PlayerData.Where(p => p.deviceID == deviceID).FirstOrDefault();
            //var teamEntry = GetTeamAssignment(deviceID, instanceID);
            //db.Attach(teamEntry);
            //player.FactionId = factionID;
            //db.SaveChanges();

            pt.Stop(deviceID + "|" + factionID);
            return factionID;
        }

    }
}
