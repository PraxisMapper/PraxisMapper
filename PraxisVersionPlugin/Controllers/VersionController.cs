using Microsoft.AspNetCore.Mvc;
using PraxisCore;
using PraxisCore.Support;

namespace PraxisVersionPlugin.Controllers {
    [ApiController]
    [Route("[controller]")]
    public class VersionController : Controller, IPraxisPlugin {
        public static string PrivacyPolicy = "";

        [HttpGet]
        [Route("/[controller]")]
        [Route("/[controller]/Index")]
        [EndpointSummary("Returns the expected client version")]
        [EndpointDescription("This does not enforce the minimum client version, this only tells a client the expected version (and it can determine that a newer version is available).")]
        public string GetVersion() {
            return GenericData.GetGlobalData("clientVersion").ToUTF8String();
        }
    }
}