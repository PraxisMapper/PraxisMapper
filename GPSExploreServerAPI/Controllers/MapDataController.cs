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
    public class MapDataController : ControllerBase
    {
        //This is where I do my calls to get which cells are which area type.
        [HttpGet]
        [Route("/[controller]/test")]
        public string TestDummyEndpoint()
        {
            //For debug purposes to confirm the server is running and reachable.
            return "OK";
        }

        [HttpGet]
        [Route("/[controller]/cellData/{pluscode8}")]
        public string CellData(string pluscode8)
        {
            //For debug purposes to confirm the server is running and reachable.
            return "OK";
        }

    }
}
