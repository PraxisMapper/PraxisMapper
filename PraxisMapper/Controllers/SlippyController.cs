using Microsoft.AspNetCore.Mvc;
using System;

namespace PraxisMapper.Controllers
{
    [Route("[controller]")]
    public class SlippyController : Controller
    {
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
