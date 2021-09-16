using PraxisCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using System;
using System.Linq;
using static PraxisCore.DbTables;

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

        
        [HttpGet]
        [Route("/[controller]/PerfData/{password}")]
        public string PerfData(string password)
        {
            if (password != Configuration.GetValue<string>("adminPwd"))
                return "";

            var db = new PraxisContext();
            var groups = db.PerformanceInfo.Where(p => p.calledAt > DateTime.Now.AddDays(-7)).AsEnumerable().GroupBy(g => g.functionName).OrderBy(g => g.Key).ToList();
            var avgs = groups.Select(g => new { name = g.Key, avg = g.Average(gg => gg.runTime) }).ToList();

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
                results += g.Key+ ":" + g.Max(gg => gg.runTime) + Environment.NewLine;
            }
            results += Environment.NewLine;

            results += "Call Counts:" + Environment.NewLine;
            foreach (var g in groups)
            {
                results += g.Key + ":" + g.Count() + Environment.NewLine;
            }

            return results;
        }

        [HttpGet]
        [Route("/[controller]/GetServerBounds/{password}")]
        public string GetServerBounds(string password)
        {
            //NOTE: this is duplicated in DataController without the admin password check.
            if (password != Configuration.GetValue<string>("adminPwd"))
                return "";

            var results = cache.Get<ServerSetting>("settings");
            return results.SouthBound + "," + results.WestBound + "|" + results.NorthBound + "," + results.EastBound;
        }

        [HttpGet]
        [Route("/[controller]/test")]
        public string TestDummyEndpoint()
        {
            //For debug purposes to confirm the server is running and reachable.
            string results = "Function OK";
            try
            {
                var DB = new PraxisContext();
                var check = DB.PlayerData.FirstOrDefault();
                results += "|Database OK";
            }
            catch (Exception ex)
            {
                results += "|" + ex.Message + "|" + ex.StackTrace;
            }

            return results;
        }
    }
}
