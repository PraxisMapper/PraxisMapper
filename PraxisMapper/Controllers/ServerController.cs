using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using PraxisCore;
using System;
using System.Buffers;
using System.Linq;
using static PraxisCore.DbTables;

namespace PraxisMapper.Controllers
{
    [Route("[controller]")]
    [ApiController]
    public class ServerController : Controller
    {
        //For endpoints that relay information about the server itself. not game data.
        //
        private readonly IConfiguration Configuration;
        private static IMemoryCache cache;

        public ServerController(IConfiguration configuration, IMemoryCache memoryCacheSingleton)
        {
            Configuration = configuration;
            cache = memoryCacheSingleton;
        }

        [HttpGet]
        [Route("/[controller]/ServerBounds")]
        public string GetServerBounds()
        {
            var bounds = cache.Get<ServerSetting>("settings");
            return bounds.SouthBound + "|" + bounds.WestBound + "|" + bounds.NorthBound + "|" + bounds.EastBound;
        }

        [HttpDelete]
        [Route("/[controller]/Player/{deviceId}")]
        public int DeleteUser(string deviceId)
        {
            //GDPR compliance requires this to exist and be available to the user. 
            //Custom games that attach players to locations may need additional logic to fully meet legal requirements.
            var db = new PraxisContext();
            var removing = db.PlayerData.Where(p => p.DeviceID == deviceId).ToArray();
            db.PlayerData.RemoveRange(removing);
            return db.SaveChanges();
        }

        [HttpGet]
        [Route("/[controller]/UseAntiCheat")]
        public bool UseAntiCheat()
        {
            //This may belong on a different endpoint? Possibly Admin? Or should I make a new Server endpoint for things like that?
            return Configuration.GetValue<bool>("enableAntiCheat");
        }

        [HttpPut]
        [Route("/[controller]/AntiCheat/{filename}")]
        public void AntiCheat(string filename)
        {
            var endData = GenericData.ReadBody(Request.BodyReader, (int)Request.ContentLength);

            var db = new PraxisContext();
            var IP = Request.HttpContext.Connection.RemoteIpAddress.ToString(); //NOTE: may become deviceID after testing if that's better.

            if (!Classes.PraxisAntiCheat.antiCheatStatus.ContainsKey(IP))
                Classes.PraxisAntiCheat.antiCheatStatus.TryAdd(IP, new Classes.AntiCheatInfo());

            if (db.AntiCheatEntries.Any(a => a.filename == filename && a.data == endData))
            {
                var entry = Classes.PraxisAntiCheat.antiCheatStatus[IP];
                if (!entry.entriesValidated.Contains(filename))
                    entry.entriesValidated.Add(filename);

                if (entry.entriesValidated.Count == Classes.PraxisAntiCheat.expectedCount)
                    entry.validUntil = DateTime.Now.AddHours(24);
            }
        }
    }
}
