using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Linq;
using System.Threading.Tasks;
using GPSExploreServerAPI.Database;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace GPSExploreServerAPI.Controllers
{
    [Route("[controller]")]
    [ApiController]
    public class GPSExploreController : ControllerBase
    {
        /* functions needed
         * update player stats (on login, client attempts to send playerData row, plus a couple of Count() calls, and their device ID (or google games id or something)
         * get leaderboards
         * -subboards: Most small cells, most big cells, highest score, most distance, most time, fastest avg speed (distance/time), most coffees purchased, time to all trophies, 
         * Ties should be broken by date (so, who most recently did the thing that set the score), which means tracking more data client-side.
        */

        //Session is not enabled by default on API projects, which is correct.

        [HttpGet]
        [Route("/GPSExplore/UploadData")] //use this to tag endpoints correctly.
        public string UploadData(string allData)
        {
            if (allData == null)
                return "Error";

            //TODO: take several int parameters instead of converting all these as strings.
            //Take data from a client, save it to the DB
            string[] components = allData.Split("|");
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
            data.distance = components[3].ToInt();
            data.maxSpeed = components[4].ToInt();
            data.score = components[5].ToInt();
            data.t10Cells = components[6].ToInt();
            data.t8Cells = components[7].ToInt();
            data.timePlayed = components[8].ToInt();
            data.totalSpeed = components[9].ToInt();

            if (insert)
                db.PlayerData.Add(data);

            db.SaveChanges();

            return "OK";
        }

        [HttpGet]
        [Route("/GPSExplore/test")]
        public string TestDummyEndpoint()
        {
            //For debug purposes to confirm the server is running and reachable.
            return "OK";
        }

        [HttpGet]
        [Route("/[controller]/10CellLeaderboard")]
        public string Get10CellLeaderboards()
        {
            //take in the device ID, return the top 10 players for this leaderboard, and the user's current rank.
            //Make into a template for other leaderboards.
            return "OK10";
        }


    }
}
