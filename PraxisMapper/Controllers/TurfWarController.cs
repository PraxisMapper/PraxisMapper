using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PraxisMapper.Controllers
{ 
    [Route("[controller]")]
    [ApiController]
    public class TurfWarController : Controller
    {
        public void LearnCell8TurfWar()
        {
            //Which factions own which Cell10s nearby?
        }

        public void ClaimCell10TurfWar(int factionId, string Cell10)
        {
            //Mark this cell10 as belonging to this faction, update the lockout timer.
        }

        public void Scoreboard()
        {
            //which faction has the most cell10s?
            //also, report time 
        }

        public void ModeTime()
        {
            //how much time remains in the current session. Might be 3 minute rounds, might be week long rounds.
        }


    }
}
