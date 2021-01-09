using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CoreComponents;

namespace PraxisMapper.Controllers
{ 
    [Route("[controller]")]
    [ApiController]
    public class TurfWarController : Controller
    {
        public static bool isResetting = false;
        //TurfWar is a simplified version of AreaControl.
        //1) It operates on a per-Cell basis instead of a per-MapData entry basis.
        //2) The 'earn points to spend points' part is removed in favor of auto-claiming areas you walk into. (A lockout timer is applied to stop 2 people from constantly flipping one area ever half-second)
        //3) No direct interaction with the device is required. Game needs to be open, thats all.
        //The leaderboards for TurfWar reset regularly (weekly? Hourly? ), and could be set to reset very quickly and restart (3 minutes of gameplay, 1 paused for reset). 
        //Default config is a weekly run Sat-Fri, with a 30 second lockout on cells.
        //TODO: allow pre-configured teams for specific events? this is difficult if I don't want to track users on the server, since this would have to be by deviceID

        public TurfWarController()
        {
            try
            {
                Classes.PerformanceTracker pt = new Classes.PerformanceTracker("TurfWarConstructor");
                var db = new PraxisContext();
                var instances = db.TurfWarConfigs.ToList();
                foreach (var i in instances)
                    if (i.Repeating) //Don't check this on non-repeating instances.
                        CheckForReset(i.TurfWarConfigId); //Do this on every call so we don't have to have an external app handle these, and we don't miss one.
                pt.Stop();
            }
            catch(Exception ex)
            {
                Classes.ErrorLogger.LogError(ex);
            }
        }

        [HttpGet]
        [Route("/[controller]/LearnCell8/{instanceID}/{Cell8}")]
        public string LearnCell8(int instanceID, string Cell8)
        {
            try
            {
                Classes.PerformanceTracker pt = new Classes.PerformanceTracker("LearnCell8");
                //Which factions own which Cell10s nearby?
                var db = new PraxisContext();
                var cellData = db.TurfWarEntries.Where(t => t.TurfWarConfigId == instanceID && t.Cell8 == Cell8).ToList();
                string results = ""; //Cell8 + "|";
                foreach (var cell in cellData)
                    results += cell.Cell10 + "=" + cell.FactionId + "|";
                pt.Stop(Cell8);
                return results;
            }
            catch(Exception ex)
            {
                Classes.ErrorLogger.LogError(ex);
                return "";
            }
        }

        [HttpGet]
        [Route("/[controller]/ClaimCell10/{factionId}/{Cell10}")]
        public void ClaimCell10(int factionId, string Cell10)
        {
            Classes.PerformanceTracker pt = new Classes.PerformanceTracker("ClaimCell10");
            //TODO: this could take a deviceID and work out which factions per instance, but then we have an entry with a player and a location. we try not to process or store those.
            //Mark this cell10 as belonging to this faction, update the lockout timer.
            var db = new PraxisContext();
            //run all the instances at once.
            foreach (var config in db.TurfWarConfigs.Where(t => t.Repeating || (t.StartTime < DateTime.Now && t.TurfWarNextReset > DateTime.Now)).ToList())
            {
                var entry = db.TurfWarEntries.Where(t => t.TurfWarConfigId == config.TurfWarConfigId && t.FactionId == factionId && t.Cell10 == Cell10).FirstOrDefault();
                if (entry == null)
                {
                    entry = new DbTables.TurfWarEntry() { Cell10 = Cell10, TurfWarConfigId = config.TurfWarConfigId, Cell8 = Cell10.Substring(0, 8), CanFlipFactionAt = DateTime.Now.AddSeconds(-1) };
                    db.TurfWarEntries.Add(entry);
                }
                if (DateTime.Now > entry.CanFlipFactionAt)
                {
                    entry.FactionId = factionId;
                    entry.CanFlipFactionAt = DateTime.Now.AddSeconds(config.Cell10LockoutTimer);
                    entry.ClaimedAt = DateTime.Now;
                }
            }
            db.SaveChanges();
            pt.Stop(Cell10);
        }

        [HttpGet]
        [Route("/[controller]/Scoreboard/{instanceID}")]
        public string Scoreboard(int instanceID)
        {
            Classes.PerformanceTracker pt = new Classes.PerformanceTracker("Scoreboard");
            //which faction has the most cell10s?
            //also, report time, primarily for recordkeeping 
            var db = new PraxisContext();
            var teams = db.Factions.ToLookup(k => k.FactionId, v => v.Name);
            var data = db.TurfWarEntries.Where(t => t.TurfWarConfigId == instanceID).GroupBy(g => g.FactionId).Select(t => new { instanceID = instanceID,  team = t.Key, score = t.Count()}).OrderByDescending(t => t.score).ToList();
            var modeName = db.TurfWarConfigs.Where(t => t.TurfWarConfigId == instanceID).FirstOrDefault().Name;
            //TODO: data to string of some kind.
            string results = modeName + "#" + DateTime.Now + "|";
            foreach (var d in data)
            {
                results += teams[d.team].FirstOrDefault() + "=" + d.score +"|";
            }
            pt.Stop(instanceID.ToString());
            return results;
        }

        public static string PastScoreboards()
        {
            Classes.PerformanceTracker pt = new Classes.PerformanceTracker("PastScoreboards");
            var db = new PraxisContext();
            var results = db.TurfWarScoreRecords.OrderByDescending(t => t.RecordedAt).ToList();
            //Scoreboard already uses # and | as separators, we will use \n now.
            var parsedResults = String.Join("\n", results);
            pt.Stop();
            return parsedResults;
        }

        [HttpGet] //TODO this is a POST
        [Route("/[controller]/ModeTime/{instanceID}")]
        public TimeSpan ModeTime(int instanceID)
        {
            Classes.PerformanceTracker pt = new Classes.PerformanceTracker("ModeTime");
            //how much time remains in the current session. Might be 3 minute rounds, might be week long rounds.
            var db = new PraxisContext();
            var time = db.TurfWarConfigs.Where(t => t.TurfWarConfigId == instanceID).Select(t => t.TurfWarNextReset).FirstOrDefault();
            pt.Stop(instanceID.ToString());
            return DateTime.Now - time;
        }

        public void ResetGame(int instanceID, bool manaulReset = false, DateTime? nextEndTime = null)
        {
            Classes.PerformanceTracker pt = new Classes.PerformanceTracker("ResetGame");

            isResetting = true;
            //TODO: determine if any of these commands needs to be raw SQL for performance reasons.
            //Clear out any stored data and fire the game mode off again.
            //Fire off a reset.
            var db = new PraxisContext();
            var twConfig = db.TurfWarConfigs.Where(t => t.TurfWarConfigId == instanceID).FirstOrDefault();
            var nextTime = twConfig.TurfWarNextReset.AddHours(twConfig.TurfWarDurationHours);
            if (manaulReset && nextEndTime.HasValue)
                nextTime = nextEndTime.Value;
            twConfig.TurfWarNextReset = nextTime;
            
            db.TurfWarEntries.RemoveRange(db.TurfWarEntries.Where(tw => tw.TurfWarConfigId == instanceID));
            //Do these need removed?... Yes. The alternative is indexing ExpiresAt and checking that column on Assignment to only include current rows. This option should be better performing in general.
            db.TurfWarTeamAssignments.RemoveRange(db.TurfWarTeamAssignments.Where(ta => ta.TurfWarConfigId == instanceID || ta.ExpiresAt < DateTime.Now));

            //record score results.
            var score = new DbTables.TurfWarScoreRecord();
            score.Results = Scoreboard(instanceID);
            score.RecordedAt = DateTime.Now;
            db.TurfWarScoreRecords.Add(score);
            db.SaveChanges();
            isResetting = false;
            pt.Stop(instanceID.ToString());
        }

        public void CheckForReset(int instanceID)
        {
            Classes.PerformanceTracker pt = new Classes.PerformanceTracker("CheckForReset");

            //TODO: cache these results into memory so I can skip a DB lookup every single call.
            var db = new PraxisContext();
            var twConfig = db.TurfWarConfigs.Where(t => t.TurfWarConfigId == instanceID).FirstOrDefault();
            if (twConfig.TurfWarDurationHours == -1) //This is a permanent instance.
                return;
            
            if (DateTime.Now < twConfig.TurfWarNextReset)
                ResetGame(instanceID);
            pt.Stop();
        }

        [HttpGet]
        [Route("/[controller]/GetInstances/")]
        public string GetInstances()
        {
            Classes.PerformanceTracker pt = new Classes.PerformanceTracker("GetInstances");

            var db = new PraxisContext();
            var instances = db.TurfWarConfigs.ToList();
            string results = "";
            foreach (var i in instances)
            {
                results += i.TurfWarConfigId + "|" + i.TurfWarNextReset.ToString() + "|" + i.Name + Environment.NewLine;
            }
            pt.Stop();
            return results;
        }
        public int AssignTeam(int instanceID, string deviceID)
        {
            //Which team are you on per instance?
            Classes.PerformanceTracker pt = new Classes.PerformanceTracker("AssignTeam");

            var db = new PraxisContext();
            var config = db.TurfWarConfigs.Where(c => c.TurfWarConfigId == instanceID).FirstOrDefault();
            var teamEntry = db.TurfWarTeamAssignments.Where(ta => ta.deviceID == deviceID && ta.TurfWarConfigId == instanceID).FirstOrDefault();
            if (teamEntry == null)
            {
                teamEntry = new DbTables.TurfWarTeamAssignment();
                teamEntry.deviceID = deviceID;
                teamEntry.TurfWarConfigId = instanceID;
                db.TurfWarTeamAssignments.Add(teamEntry);
            }

            //Sanity check - if we're mid-run and have an assignment, keep it.
            if (teamEntry.ExpiresAt > DateTime.Now)
            {
                pt.Stop(deviceID + "|" + instanceID.ToString());
                return teamEntry.FactionId;
            }

            var smallestTeam = db.TurfWarTeamAssignments
                .Where(ta => ta.TurfWarConfigId == instanceID)
                .GroupBy(ta => ta.FactionId)
                .Select(ta => new { team = ta.Key, members = ta.Count() })
                .OrderBy(ta => ta.members)
                .First().team;

            teamEntry.FactionId = smallestTeam;
            teamEntry.ExpiresAt = config.TurfWarNextReset;
            db.SaveChanges();
            pt.Stop(deviceID + "|" + instanceID.ToString());
            return teamEntry.FactionId;
        }
    }
}
