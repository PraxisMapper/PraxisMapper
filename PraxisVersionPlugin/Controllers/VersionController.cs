using Microsoft.AspNetCore.Mvc;
using PraxisCore;
using PraxisCore.Support;

namespace PraxisVersionPlugin.Controllers {
    [ApiController]
    [Route("[controller]")]
    public class VersionController : Controller, IPraxisPlugin {
        [HttpGet]
        [Route("/[controller]")]
        [Route("/[controller]/Index")]
        public string GetVersion() {
            return GenericData.GetGlobalData("clientVersion").ToUTF8String();
        }
    }
}