using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using PraxisCore;
using PraxisMapper.Classes;
using System;
using System.Linq;

namespace PraxisMapper.Controllers {
    //This is the API controller.
    //View stuff happens in AdminView, Pulling/sending data happens here.

    [Route("[controller]")]
    [ApiController]
    public class AdminController : Controller {
        //For stuff the admin would want to do but not allow anyone else to do.
        private readonly IConfiguration Configuration;
        private readonly IMemoryCache cache;

        public AdminController(IConfiguration configuration, IMemoryCache _cache) {
            Configuration = configuration;
            cache = _cache;
        }

        public override void OnActionExecuting(ActionExecutingContext context) {
            base.OnActionExecuting(context);
            PraxisAuthentication.GetAuthInfo(Response, out var accountId, out var password);
            if (!PraxisAuthentication.IsAdmin(accountId) && !HttpContext.Request.Host.IsLocalIpAddress())
                HttpContext.Abort();
        }


        /// <summary>
        /// Returns a weeks worth of summarized performance data
        /// </summary>
        /// <param name="password">The admin password, as set in the config file.</param>
        /// <returns>A pre-formatted string with the Average, Maximum, and CallCount values for functions within the last week.</returns>
        /// <remarks>A quick way to get an idea of which calls are slow or frequently used while debugging.</remarks>
        [HttpGet]
        [Route("/[controller]/PerfData/{password}")]
        public string PerfData(string password) {
            if (password != Configuration.GetValue<string>("adminPwd"))
                return "";

            using var db = new PraxisContext();
            db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            var groups = db.PerformanceInfo.Where(p => p.CalledAt > DateTime.UtcNow.AddDays(-7)).AsEnumerable().GroupBy(g => g.FunctionName).OrderBy(g => g.Key).ToList();
            var avgs = groups.Select(g => new { name = g.Key, avg = g.Average(gg => gg.RunTime) }).ToList();

            string results = "Performance Info:" + Environment.NewLine;
            results = "Averages:" + Environment.NewLine;
            foreach (var a in avgs) {
                results += a.name + ":" + a.avg + Environment.NewLine;
            }
            results += Environment.NewLine;

            results += "Maximums:" + Environment.NewLine;
            foreach (var g in groups) {
                results += g.Key + ":" + g.Max(gg => gg.RunTime) + Environment.NewLine;
            }
            results += Environment.NewLine;

            results += "Call Counts:" + Environment.NewLine;
            foreach (var g in groups) {
                results += g.Key + ":" + g.Count() + Environment.NewLine;
            }

            return results;
        }

        /// <summary>
        /// Sets the server maintenance message.
        /// </summary>
        /// <param name="message">The message to return to all calls instead of their normal result.</param>
        /// <returns>true if the message was set, false if the request was not by an admin</returns>
        /// <remarks>This is used to keep the server up while some database work is being done.
        /// Set the message to a non-empty string to lock out non-admin users from calling endpoints. 
        /// Set it to an empty string or whitespace to restore access.
        /// </remarks>
        [HttpPut]
        [Route("/[controller]/MaintMessage/{message}")]
        public bool SetMaintenanceMessage(string message) {
            PraxisAuthentication.GetAuthInfo(Response, out var accountId, out _);
            if (!PraxisAuthentication.IsAdmin(accountId) && !Request.Host.IsLocalIpAddress()) {
                return false;
            }
            PraxisMaintenanceMessage.outputMessage = message;
            return true;
        }
    }
}
