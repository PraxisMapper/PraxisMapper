using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PraxisMapper.Controllers
{
    [Route("[controller]")]
    public class AdminViewController : Controller
    {
        [HttpGet]
        [Route("/[controller]")]
        [Route("/[controller]/Index")]
        public IActionResult Index()
        {
            return View();
        }
    }
}
