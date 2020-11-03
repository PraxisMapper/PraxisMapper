using DatabaseAccess;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace GPSExploreServerAPI.Controllers
{
    public class GameplayController : Controller
    {
        //GameplayController handles the basic gameplay interactions included with the server
        //this is AreaControl commands for players to take an area, get resources through an action, etc.

        [HttpGet]
        [Route("/[controller]/claimArea/{MapDataId}")]
        public bool ClaimArea(long MapDataId)
        {
            GpsExploreContext db = new GpsExploreContext();

            return false;
        }
    }
}
