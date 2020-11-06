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

        [HttpGet]
        [Route("/[controller]/claimArea/{MapDataId}/{factionId}")]
        public bool ClaimArea(long MapDataId, long factionId)
        {
            PraxisContext db = new PraxisContext();

            return false;
        }
    }
}
