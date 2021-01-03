using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PraxisMapper.Controllers
{
    [Route("[controller]")]
    [ApiController]
    public class MonsterFinderController : Controller
    {
        //MonsterFinder will be an example gameplay mode where you can interact with each thing once a day.
        //You will walk up to an interactible area, do something on the client side, and get some rewards.
        //Each interactible area will have additional properties that affect the rewards.
        //Will areas have types pre-assigned, or will they be assigned on demand?
        //The game itself needs to do the battle/catch/whatever, this just handles the server side of things.

        //Needs a few tables
        //one for things to find in gameplay and their baseline definition
        //a second to define how often those things show up or whatnot.
        //a third settings table for basic stuff?
        public IActionResult Index()
        {
            return View();
        }
    }
}
