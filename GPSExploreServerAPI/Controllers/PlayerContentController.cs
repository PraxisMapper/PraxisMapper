using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace GPSExploreServerAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class PlayerContentController : ControllerBase
    {

        //A future concept
        //Have endpoints designed to handle things the player can edit on the map, for other players to interact with.

        //Out of scope intially. First game or two will not need this.
        //Making the file to remember this idea.

        //Will need db tables for this?
        //this probably stores some baseline data, and perhaps an ID for an object for another program to pull from a DB.
        //Yeah, this might be too game-specific to set up as a baseline past that.
    }
}
