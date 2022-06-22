using Microsoft.AspNetCore.Mvc;
using PraxisCore.Support;
using System;

namespace PraxisMapper.Controllers
{
    [Route("[controller]")]
    public class SlippyController : Controller, IPraxisPlugin
    {
        public void Startup()
        {
            //Slippy requires no initialization.
            return;
        }

        [HttpGet]
        [Route("/[controller]")]
        [Route("/[controller]/Index")]
        public IActionResult Index()
        {
            try
            {
                return View("Index");
            }
            catch(Exception ex)
            {
                return null;
            }
        }


    }
}
