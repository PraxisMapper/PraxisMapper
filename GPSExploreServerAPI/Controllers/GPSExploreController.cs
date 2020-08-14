using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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

        [HttpGet]
        [Route("/GPSExplore/UploadData")] //use this to tag endpoints correctly.
        public string UploadData()
        {
            //TAke data from a client, save it to the DB
            return "OK";
        }

        [HttpGet]
        [Route("[controller]/10CellLeaderboard")]
        public string Get10CellLeaderboards()
        {
            //take in the device ID, return the top 10 players for this leaderboard, and the user's current rank.
            //Make into a template for other leaderboards.
            return "OK10";
        }


    }
}
