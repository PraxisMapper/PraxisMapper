using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Linq;
using System.Threading.Tasks;
using DatabaseAccess;
using Google.OpenLocationCode;
using GPSExploreServerAPI.Classes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using static DatabaseAccess.DbTables;

namespace GPSExploreServerAPI.Controllers
{
    [Route("[controller]")]
    [ApiController]
    public class GPSExploreController : ControllerBase
    {
        /* functions needed
         * Ties should be broken by date (so, who most recently did the thing that set the score), which means tracking more data client-side. 
         * --Tiebreaker calc: .Where(p => p.value > my.value && p.dateLastUpdated > my.dateLastUpdated? 
         * 
         * TODO:
         * rename this controller to be more of a GameData endpoint, since its all leaderboards.
        */
        //Session is not enabled by default on API projects, which is correct.

        [HttpGet]
        [Route("/[controller]/test")]
        public string TestDummyEndpoint()
        {
            //For debug purposes to confirm the server is running and reachable.
            string results = "Function OK";

            try
            {
                var DB = new GpsExploreContext();
                //var DB = (GpsExploreContext)new ServiceContainer().GetService(typeof(GpsExploreContext)); //returns null. Need an existing ServiceContainer
                var check = DB.PlayerData.FirstOrDefault();
                results += "|Database OK";
            }
            catch(Exception ex)
            {
                results += "|" + ex.Message + "|" + ex.StackTrace;
            }

            return results;
        }

        [HttpPost]
        [Route("/[controller]/UploadData")] //use this to tag endpoints correctly.
        public string UploadData() 
        {
            PerformanceTracker pt = new PerformanceTracker("UploadData");
            byte[] inputStream = new byte[(int)HttpContext.Request.ContentLength];
            HttpContext.Request.Body.ReadAsync(inputStream, 0, (int)HttpContext.Request.ContentLength - 1);
            string allData = System.Text.Encoding.Default.GetString(inputStream);

            if (allData == null)
                return "Error-Null";

            //TODO: take several int parameters instead of converting all these as strings.
            //Take data from a client, save it to the DB
            string[] components = allData.Split("|");

            if (components.Length != 11)
                return "Error-Length";

            GpsExploreContext db = new GpsExploreContext();
            var data = db.PlayerData.Where(p => p.deviceID == components[0]).FirstOrDefault();
            bool insert = false;
            if (data == null || data.deviceID == null)
            {
                data = new PlayerData();
                insert = true;
            }

            data.deviceID = components[0];
            data.cellVisits = components[1].ToInt();
            data.DateLastTrophyBought = components[2].ToInt();
            data.distance = components[3].ToDouble();
            data.maxSpeed = components[4].ToDouble();
            data.score = components[5].ToInt();
            data.t10Cells = components[6].ToInt();
            data.t8Cells = components[7].ToInt();
            data.timePlayed = (int)components[8].ToDouble(); //This isn't being calculated right, i dont think
            data.totalSpeed = components[9].ToDouble();
            data.altitudeSpread = components[10].ToInt();
            data.lastSyncTime = DateTime.Now;

            if (insert)
                db.PlayerData.Add(data);
            
            bool anticheat = false; //off for testing while I fix some saved values.
            if (anticheat)
            {
                //Anti-cheat 1: if score is less than the minimum for cell count, reject the value
                var minScore = (data.t10Cells * 16) + (data.t8Cells * 100); //If you only enter every cell once this would be your minimum score.
                if (data.score < minScore)
                    return "Error-Cheat";

                //Anti-cheat 2: If your altitude spread is higher than (jet-plane cruising height of 10500 plus dead sea shore of -400), i'm rejecting your results.
                if (data.altitudeSpread > 11000)
                    return "Error-Cheat";

                //Anti-cheat 4: you can't have more cells discovered than 2x playtime seconds (location events can fire off multiple times a second, but i can only catch 2 of them.)
                if (data.t10Cells > data.timePlayed * 2)
                    return "Error-Cheat";
            }

            db.SaveChanges();

            pt.Stop();
            return "OK";
        }

        [HttpGet]
        [Route("/[controller]/10CellLeaderboard/{deviceID}")]
        public string Get10CellLeaderboards(string deviceID)
        {
            //take in the device ID, return the top 10 players for this leaderboard, and the user's current rank.
            PerformanceTracker pt = new PerformanceTracker("Get10CellLeaderboard");
            GpsExploreContext db = new GpsExploreContext();
            
            List<int> results = db.PlayerData.OrderByDescending(p => p.t10Cells).Take(10).Select(p => p.t10Cells).ToList();
            int playerScore = db.PlayerData.Where(p => p.deviceID == deviceID).Select(p => p.t10Cells).FirstOrDefault();
            int playerRank = db.PlayerData.Where(p => p.t10Cells >= playerScore).Count();
            results.Add(playerRank);
            
            pt.Stop();
            return string.Join("|", results);
        }

        [HttpGet]
        [Route("/[controller]/8CellLeaderboard/{deviceID}")]
        public string Get8CellLeaderboards(string deviceID)
        {
            //take in the device ID, return the top 10 players for this leaderboard, and the user's current rank.
            PerformanceTracker pt = new PerformanceTracker("Get8CellLeaderboard");
            GpsExploreContext db = new GpsExploreContext();

            List<int> results = db.PlayerData.OrderByDescending(p => p.t8Cells).Take(10).Select(p => p.t8Cells).ToList();
            int playerScore = db.PlayerData.Where(p => p.deviceID == deviceID).Select(p => p.t8Cells).FirstOrDefault();
            int playerRank = db.PlayerData.Where(p => p.t8Cells >= playerScore).Count();
            results.Add(playerRank);

            pt.Stop();
            return string.Join("|", results);
        }

        [HttpGet]
        [Route("/[controller]/ScoreLeaderboard/{deviceID}")]
        public string GetScoreLeaderboards(string deviceID)
        {
            //take in the device ID, return the top 10 players for this leaderboard, and the user's current rank.
            PerformanceTracker pt = new PerformanceTracker("GetScoreLeaderboard");
            GpsExploreContext db = new GpsExploreContext();
            List<int> results = db.PlayerData.OrderByDescending(p => p.score).Take(10).Select(p => p.score).ToList();
            int playerScore = db.PlayerData.Where(p => p.deviceID == deviceID).Select(p => p.score).FirstOrDefault();
            int playerRank = db.PlayerData.Where(p => p.score >= playerScore).Count();
            results.Add(playerRank);

            pt.Stop();
            return string.Join("|", results);
        }

        [HttpGet]
        [Route("/[controller]/DistanceLeaderboard/{deviceID}")]
        public string GetDistanceLeaderboards(string deviceID)
        {
            //take in the device ID, return the top 10 players for this leaderboard, and the user's current rank.
            PerformanceTracker pt = new PerformanceTracker("GetDistanceLeaderboard");
            GpsExploreContext db = new GpsExploreContext();
            List<double> results = db.PlayerData.OrderByDescending(p => p.distance).Take(10).Select(p => p.distance).ToList();
            double playerScore = db.PlayerData.Where(p => p.deviceID == deviceID).Select(p => p.distance).FirstOrDefault();
            int playerRank = db.PlayerData.Where(p => p.distance >= playerScore).Count();
            results.Add(playerRank);

            pt.Stop();
            return string.Join("|", results);
        }

        [HttpGet]
        [Route("/[controller]/TimeLeaderboard/{deviceID}")]
        public string GetTimeLeaderboards(string deviceID)
        {
            //take in the device ID, return the top 10 players for this leaderboard, and the user's current rank.
            PerformanceTracker pt = new PerformanceTracker("GetTimeLeaderboard");
            GpsExploreContext db = new GpsExploreContext();
            List<int> results = db.PlayerData.OrderByDescending(p => p.timePlayed).Take(10).Select(p => p.timePlayed).ToList();
            int playerScore = db.PlayerData.Where(p => p.deviceID == deviceID).Select(p => p.timePlayed).FirstOrDefault();
            int playerRank = db.PlayerData.Where(p => p.timePlayed >= playerScore).Count();
            results.Add(playerRank);

            pt.Stop();
            return string.Join("|", results);
        }

        [HttpGet]
        [Route("/[controller]/AvgSpeedLeaderboard/{deviceID}")]
        public string GetAvgSpeedLeaderboards(string deviceID)
        {
            //TODO: might calculate this on the device, send it over here, save it in its own column instead of calculating on all users each call.
            //take in the device ID, return the top 10 players for this leaderboard, and the user's current rank.
            PerformanceTracker pt = new PerformanceTracker("GetAvgSpeedLeaderboard");
            GpsExploreContext db = new GpsExploreContext();
            //This one does a calculation, will take a bit longer.
            List<double> results = db.PlayerData.Where(p => p.timePlayed > 0).Select(p => p.distance / p.timePlayed).OrderByDescending(p => p).ToList(); //divide by zero error 
            double playerScore = db.PlayerData.Where(p => p.deviceID == deviceID).Select(p => p.timePlayed > 0 ? (double)(p.distance / p.timePlayed) : (double)0.0).FirstOrDefault();
            int playerRank = results.Where(p => p >= playerScore).Count();
            results = results.Take(10).ToList();
            results.Add(playerRank);

            pt.Stop();
            return string.Join("|", results);
        }

        [HttpGet]
        [Route("/[controller]/TrophiesLeaderboard/{deviceID}")]
        public string GetTrophiesLeaderboards(string deviceID)
        {
            //take in the device ID, return the top 10 players for this leaderboard, and the user's current rank.
            PerformanceTracker pt = new PerformanceTracker("GetTrophiesLeaderboard");
            GpsExploreContext db = new GpsExploreContext();
            List<int> results = db.PlayerData.OrderByDescending(p => p.DateLastTrophyBought).Take(10).Select(p => p.DateLastTrophyBought).ToList();
            int playerScore = db.PlayerData.Where(p => p.deviceID == deviceID).Select(p => p.DateLastTrophyBought > 0 ? p.DateLastTrophyBought : int.MaxValue).FirstOrDefault();
            int playerRank = db.PlayerData.Where(p => p.DateLastTrophyBought <= playerScore).Count(); //This one is a time, so lower is better.
            results.Add(playerRank);

            pt.Stop();
            return string.Join("|", results);
        }

        [HttpGet]
        [Route("/[controller]/AltitudeLeaderboard/{deviceID}")]
        public string GetAltitudeLeaderboards(string deviceID)
        {
            //take in the device ID, return the top 10 players for this leaderboard, and the user's current rank.
            PerformanceTracker pt = new PerformanceTracker("GetAltitudeLeaderboard");
            GpsExploreContext db = new GpsExploreContext();

            List<int> results = db.PlayerData.OrderByDescending(p => p.altitudeSpread).Take(10).Select(p => p.altitudeSpread).ToList();
            int playerScore = db.PlayerData.Where(p => p.deviceID == deviceID).Select(p => p.altitudeSpread).FirstOrDefault();
            int playerRank = db.PlayerData.Where(p => p.t10Cells >= playerScore).Count();
            results.Add(playerRank);

            pt.Stop();
            return string.Join("|", results);
        }
    }
}
