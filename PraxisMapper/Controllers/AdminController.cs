using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using PraxisCore;
using PraxisMapper.Classes;
using System;
using System.Linq;

namespace PraxisMapper.Controllers
{
    //This is the API controller.
    //View stuff happens in AdminView, Pulling/sending data happens here.

    [Route("[controller]")]
    [ApiController]
    public class AdminController : Controller
    {
        //For stuff the admin would want to do but not allow anyone else to do.
        private readonly IConfiguration Configuration;
        private readonly IMemoryCache cache;

        public AdminController(IConfiguration configuration, IMemoryCache _cache)
        {
            Configuration = configuration;
            cache = _cache;
        }

        public override void OnActionExecuting(ActionExecutingContext context)
        {
            base.OnActionExecuting(context);
            PraxisAuthentication.GetAuthInfo(Response, out var accountId, out var password);
            if (!PraxisAuthentication.IsAdmin(accountId) && !HttpContext.Request.Host.IsLocalIpAddress())
                HttpContext.Abort();
        }


        [HttpGet]
        [Route("/[controller]/PerfData/{password}")]
        public string PerfData(string password)
        {
            if (password != Configuration.GetValue<string>("adminPwd"))
                return "";

            var db = new PraxisContext();
            var groups = db.PerformanceInfo.Where(p => p.CalledAt > DateTime.UtcNow.AddDays(-7)).AsEnumerable().GroupBy(g => g.FunctionName).OrderBy(g => g.Key).ToList();
            var avgs = groups.Select(g => new { name = g.Key, avg = g.Average(gg => gg.RunTime) }).ToList();

            string results = "Performance Info:" + Environment.NewLine;
            results = "Averages:" + Environment.NewLine;
            foreach (var a in avgs)
            {
                results += a.name + ":" + a.avg + Environment.NewLine;
            }
            results += Environment.NewLine;

            results += "Maximums:" + Environment.NewLine;
            foreach (var g in groups)
            {
                results += g.Key+ ":" + g.Max(gg => gg.RunTime) + Environment.NewLine;
            }
            results += Environment.NewLine;

            results += "Call Counts:" + Environment.NewLine;
            foreach (var g in groups)
            {
                results += g.Key + ":" + g.Count() + Environment.NewLine;
            }

            return results;
        }
    }
}
