using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web;

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
                var a = ex;
                return null;
            }
        }
    }
}
