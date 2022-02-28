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
                var a = ex;
                return null;
            }
        }

        //[HttpGet]
        //[Route("/[controller]/ZZT")]
        //public IActionResult ZZT()
        //{
        //    try
        //    {
        //        return View("ZZT");
        //    }
        //    catch (Exception ex)
        //    {
        //        var a = ex;
        //        return null;
        //    }
        //}
    }
}
