using Microsoft.AspNetCore.Mvc;
using PraxisCore.Support;

namespace PraxisMapper.Controllers {
    [Route("[controller]")]
    public class SlippyController : Controller, IPraxisPlugin {
        [HttpGet]
        [Route("/[controller]")]
        [Route("/[controller]/Index")]
        public IActionResult Index() {
            try {
                return View("Index");
            }
            catch (Exception ex) {
                return null;
            }
        }
    }
}
