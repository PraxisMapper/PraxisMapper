using CoreComponents;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace PraxisMapper.Controllers
{

    [Route("[controller]")]
    [ApiController]
    public class AdminController : Controller

    {
        private readonly IConfiguration Configuration;

        public AdminController(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        //For stuff the admin would want to do but not allow anyone else to do.
        //TODO: A password needs to be provided somewhere to run these. 
        [HttpGet]
        [Route("/[controller]/PerfData/{password}")]
        public string PerfData(string password)
        {
            //if (password != Configuration.GetValue<string>("adminPwd"))
                //return "";

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
    }
}
