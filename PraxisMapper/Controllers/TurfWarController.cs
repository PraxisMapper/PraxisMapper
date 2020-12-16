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
        //TurfWar is a simplified version of AreaControl.
        //1) It operates on a per-Cell basis instead of a per-MapData entry basis.
        //2) The 'earn points to spend points' part is removed in favor of auto-claiming areas you walk into. (A lockout timer is applied to stop 2 people from constantly flipping one area ever half-second)
        //3) No direct interaction with the device is required. 
        //The leaderboards for TurfWar reset regularly (weekly), and could be set to reset very quickly and restart (3 minutes of gameplay, 1 paused for reset).
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
