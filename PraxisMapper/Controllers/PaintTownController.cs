using CoreComponents;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using static CoreComponents.DbTables;

namespace PraxisMapper.Controllers
{
    [Route("[controller]")]
    [ApiController]
    public class PaintTownController : Controller
    {
        private readonly IConfiguration Configuration;
        private IMemoryCache cache; //Using this cache to pass around some values instead of making them DB lookups each time.
        public static bool isResetting = false;
        //PaintTown is a simplified version of AreaControl.
        //1) It operates on a per-Cell basis instead of a per-MapData entry basis.
        //2) The 'earn points to spend points' part is removed in favor of auto-claiming areas you walk into. (A lockout timer is applied to stop 2 people from constantly flipping one area ever half-second)
        //3) No direct interaction with the device is required. Game needs to be open, thats all.
        //The leaderboards for PaintTown reset regularly (weekly? Hourly? ), and could be set to reset very quickly and restart (3 minutes of gameplay, 1 paused for reset). 
        //Default config is a weekly run Sat-Fri, with a 30 second lockout on cells.
        //TODO: allow pre-configured teams for specific events? this is difficult if I don't want to track users on the server, since this would have to be by deviceID
        //TODO: allow an option to let you choose to join a team. Yes, i wanted to avoid this. Yes, there's still good cases for it.

        public PaintTownController(IConfiguration configuration, IMemoryCache cacheSingleton)
        {
            try
            {
                if (isResetting)
                    return;

                Classes.PerformanceTracker pt = new Classes.PerformanceTracker("PaintTownConstructor");
                var db = new PraxisContext();
                Configuration = configuration;
                cache = cacheSingleton; //Fallback code on actual functions if this is null for some reason.

                if (cache != null)
                {
                    //reset check.
                    List<PaintTownConfig> cachedConfigs = null;
                    if (cache.TryGetValue("PTTConfigs", out cachedConfigs))
                        foreach (var t in cachedConfigs)
                            CheckForReset(t);
                }
                else
                {
                    var instances = db.PaintTownConfigs.ToList();
                    foreach (var i in instances)
                        //if (i.Repeating) //Don't check this on non-repeating instances.
                            CheckForReset(i); //Do this on every call so we don't have to have an external app handle these, and we don't miss one.
                }
                pt.Stop();
            }
            catch (Exception ex)
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
                Classes.PerformanceTracker pt = new Classes.PerformanceTracker("LearnCell8PaintTown");
                var cellData = PaintTown.LearnCell8(instanceID, Cell8);
                string results = "";
                foreach (var cell in cellData)
                    results += cell.Cell10 + "=" + cell.FactionId + "|";
                pt.Stop();
                return results;
            }
            catch (Exception ex)
            {
                Classes.ErrorLogger.LogError(ex);
                return "";
            }
        }

        [HttpGet]
        [Route("/[controller]/LearnCell8Recent/{instanceID}/{Cell8}")]
        public string LearnCell8Recent(int instanceID, string Cell8)
        {
            try
            {
                Classes.PerformanceTracker pt = new Classes.PerformanceTracker("LearnCell8PaintTownRecent");
                var cellData = PaintTown.LearnCell8(instanceID, Cell8, true);
                string results = "";
                foreach (var cell in cellData)
                    results += cell.Cell10 + "=" + cell.FactionId + "|";

                pt.Stop();
                return results;
            }
            catch (Exception ex)
            {
                Classes.ErrorLogger.LogError(ex);
                return "";
            }
        }

        [HttpGet]
        [Route("/[controller]/ClaimCell10/{factionId}/{Cell10}")]
        public int ClaimCell10(int factionId, string Cell10)
        {
            try
            {
                Classes.PerformanceTracker pt = new Classes.PerformanceTracker("ClaimCell10PaintTown");
                //Mark this cell10 as belonging to this faction, update the lockout timer.
                var db = new PraxisContext();
                //run all the instances at once.
                List<long> factions = null;
                ServerSetting settings = null;
                List<PaintTownConfig> configs = null;
                if (cache != null)
                {
                    factions = (List<long>)cache.Get("Factions");
                    settings = (ServerSetting)cache.Get("ServerSettings");
                    configs = (List<PaintTownConfig>)cache.Get("PTTConfigs");
                }
                else
                {
                    factions = db.Factions.Select(f => f.FactionId).ToList();
                    settings = db.ServerSettings.FirstOrDefault();
                    configs = db.PaintTownConfigs.ToList();
                }

                if (!factions.Any(f => f == factionId))
                {
                    pt.Stop("NoFaction:" + factionId);
                    return 0; //We got a claim for an invalid team, don't save anything.
                } 

                if (!Place.IsInBounds(Cell10, settings))
                {
                    pt.Stop("OOB:" + Cell10);
                    return 0;
                }
                int claimed = 0;

                //User will get a point if any of the configs get flipped from this claim.
                foreach (var config in configs.Where(t => t.Repeating || (t.StartTime < DateTime.Now && t.NextReset > DateTime.Now)).ToList())
                {
                    var entry = db.PaintTownEntries.Where(t => t.PaintTownConfigId == config.PaintTownConfigId && t.Cell10 == Cell10).FirstOrDefault();
                    if (entry == null)
                    {
                        entry = new DbTables.PaintTownEntry() { Cell10 = Cell10, PaintTownConfigId = config.PaintTownConfigId, Cell8 = Cell10.Substring(0, 8), CanFlipFactionAt = DateTime.Now.AddSeconds(-1) };
                        db.PaintTownEntries.Add(entry);
                    }
                    if (DateTime.Now > entry.CanFlipFactionAt)
                    {
                        if (entry.FactionId != factionId)
                        {
                            claimed = 1;
                            entry.FactionId = factionId;
                            entry.ClaimedAt = DateTime.Now;
                        }
                        entry.CanFlipFactionAt = DateTime.Now.AddSeconds(config.Cell10LockoutTimer);
                    }
                }

                db.SaveChanges();
                pt.Stop(Cell10 + claimed);
                return claimed;
            }
            catch (Exception ex)
            {
                Classes.ErrorLogger.LogError(ex);
                return 0;
            }
        }

        [HttpGet]
        [Route("/[controller]/Scoreboard/{instanceID}")]
        public string Scoreboard(int instanceID)
        {
            Classes.PerformanceTracker pt = new Classes.PerformanceTracker("ScoreboardPaintTown");
            //which faction has the most cell10s?
            //also, report time, primarily for recordkeeping 
            var db = new PraxisContext();
            List<Faction> factions = null;
            List<PaintTownConfig> configs = null;
            if (cache != null)
            {
                factions = (List<Faction>)cache.Get("Factions");
                configs = (List<PaintTownConfig>)cache.Get("PTTConfigs");
            }
            else
            {
                factions = db.Factions.ToList();
                configs = db.PaintTownConfigs.ToList();
            }
            var teams = factions.ToLookup(k => k.FactionId, v => v.Name);
            var data = db.PaintTownEntries.Where(t => t.PaintTownConfigId == instanceID).GroupBy(g => g.FactionId).Select(t => new { instanceID = instanceID, team = t.Key, score = t.Count() }).OrderByDescending(t => t.score).ToList();
            var modeName = configs.Where(t => t.PaintTownConfigId == instanceID).FirstOrDefault().Name;
            string results = modeName + "#" + DateTime.Now + "|";
            foreach (var d in data)
            {
                results += teams[d.team].FirstOrDefault() + "=" + d.score + "|";
            }
            pt.Stop(instanceID.ToString());
            return results;
        }

        [HttpGet]
        [Route("/[controller]/PastScoreboards")]
        public static string PastScoreboards()
        {
            Classes.PerformanceTracker pt = new Classes.PerformanceTracker("PastScoreboards");
            var db = new PraxisContext();
            var results = db.PaintTownScoreRecords.OrderByDescending(t => t.RecordedAt).ToList();
            //Scoreboard already uses # and | as separators, we will use \n now.
            var parsedResults = String.Join("\n", results);
            pt.Stop();
            return parsedResults;
        }

        [HttpGet]
        [Route("/[controller]/ModeTime/{instanceID}")]
        public TimeSpan ModeTime(int instanceID)
        {
            Classes.PerformanceTracker pt = new Classes.PerformanceTracker("ModeTime");
            //how much time remains in the current session. Might be 3 minute rounds, might be week long rounds.
            DateTime time = new DateTime();
            if (cache != null)
            {
                var configs = (List<PaintTownConfig>)cache.Get("PTTConfigs");
                time = configs.Where(t => t.PaintTownConfigId == instanceID).Select(t => t.NextReset).FirstOrDefault();
            }
            else
            {
                var db = new PraxisContext();
                time = db.PaintTownConfigs.Where(t => t.PaintTownConfigId == instanceID).Select(t => t.NextReset).FirstOrDefault();
            }
            
            pt.Stop(instanceID.ToString());
            return DateTime.Now - time;
        }

        public void ResetGame(int instanceID, bool manaulReset = false, DateTime? nextEndTime = null)
        {
            //It's possible that this function might be best served being an external console app.
            //Doing the best I can here to set it up to make that unnecessary, but it may not be enough.
            Classes.PerformanceTracker pt = new Classes.PerformanceTracker("ResetGame");
            if (isResetting)
                return;

            isResetting = true;
            var db = new PraxisContext();
            PaintTownConfig twConfig = new PaintTownConfig();
            if (cache != null)
            {
                List<PaintTownConfig> cachedConfigs = new List<PaintTownConfig>();
                if (cache.TryGetValue("PTTConfigs", out cachedConfigs))
                {
                    twConfig = cachedConfigs.Where(c => c.PaintTownConfigId == instanceID).FirstOrDefault();
                    db.Attach<PaintTownConfig>(twConfig);
                }
            }
            else
            {
                twConfig = db.PaintTownConfigs.Where(t => t.PaintTownConfigId == instanceID).FirstOrDefault();
            }
            var nextTime = twConfig.NextReset.AddHours(twConfig.DurationHours);
            if (manaulReset && nextEndTime.HasValue) //Manual reset expire at the usual time, so they're shorter, not longer, than a regular loop.
                nextTime = nextEndTime.Value;
            twConfig.NextReset = nextTime;
            db.PaintTownEntries.RemoveRange(db.PaintTownEntries.Where(tw => tw.PaintTownConfigId == instanceID));

            //record score results.
            var score = new DbTables.PaintTownScoreRecord();
            score.Results = Scoreboard(instanceID);
            score.RecordedAt = DateTime.Now;
            score.PaintTownConfigId = instanceID;
            db.PaintTownScoreRecords.Add(score);
            db.SaveChanges();
            isResetting = false;
            pt.Stop(instanceID.ToString());
        }

        [HttpGet]
        [Route("/[controller]/GetEndDate/{instanceID}")]
        public string GetEndDate(int instanceID)
        {
            Classes.PerformanceTracker pt = new Classes.PerformanceTracker("GetEndDate");
            PaintTownConfig twConfig = new PaintTownConfig();
            string results;
            if (cache != null)
            {
                List<PaintTownConfig> cachedConfigs = new List<PaintTownConfig>();
                if (cache.TryGetValue("PTTConfigs", out cachedConfigs))
                {
                    twConfig = cachedConfigs.Where(c => c.PaintTownConfigId == instanceID).FirstOrDefault();
                    results = twConfig.NextReset.ToString();
                    pt.Stop();
                    return results;
                }
            }

            var db = new PraxisContext();
            twConfig = db.PaintTownConfigs.Where(t => t.PaintTownConfigId == instanceID).FirstOrDefault();
            results = twConfig.NextReset.ToString();
            pt.Stop();
            return results;
        }

        public void CheckForReset(PaintTownConfig twConfig)
        {
            if (isResetting)
                return;

            Classes.PerformanceTracker pt = new Classes.PerformanceTracker("CheckForReset");

            if (twConfig.DurationHours == -1) //This is a permanent instance.
                return;

            if (DateTime.Now > twConfig.NextReset)
                ResetGame(twConfig.PaintTownConfigId);

            pt.Stop();
        }

        [HttpGet]
        [Route("/[controller]/GetInstances/")]
        public string GetInstances()
        {
            Classes.PerformanceTracker pt = new Classes.PerformanceTracker("GetInstances");
            var db = new PraxisContext();
            string results = "";
            if (cache != null)
            {
                List<PaintTownConfig> cachedConfigs = new List<PaintTownConfig>();
                results = GetInstanceInfo(cachedConfigs);
                pt.Stop();
                return results;
            }

            var instances = db.PaintTownConfigs.ToList();
            results = GetInstanceInfo(instances);
            pt.Stop();
            return results;
        }

        private string GetInstanceInfo(List<PaintTownConfig> instances)
        {
            string results = "";
            foreach (var i in instances)
                results += i.PaintTownConfigId + "|" + i.NextReset.ToString() + "|" + i.Name + Environment.NewLine;
            
            return results;
        }

        [HttpGet]
        [Route("/[controller]/ManualReset/{instanceID}")]
        public string ManualReset(int instanceID)
        {
            //TODO: should be an admin command and require hte password.
            ResetGame(instanceID, true);
            return "OK";
        }
    }
}
