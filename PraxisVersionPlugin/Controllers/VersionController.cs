using Microsoft.AspNetCore.Mvc;
using PraxisCore;

namespace PraxisVersionPlugin.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class VersionController : Controller
    {
        [HttpGet]
        [Route("[controller]")]
        public string GetVersion()
        {
            return GenericData.GetGlobalData("clientVersion").ToUTF8String();
        }
    }
}