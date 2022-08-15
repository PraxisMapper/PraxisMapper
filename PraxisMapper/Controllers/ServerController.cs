using Azure;
using Azure.Core;
using Google.OpenLocationCode;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using PraxisCore;
using PraxisMapper.Classes;
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
        [Route("/[controller]/Test")]
        public string Test()
        {
            //Used for clients to test if server is alive. Returns OK normally, clients should check for non-OK results to display as a maintenance message.
            return "OK";
        }

        [HttpGet]
        [Route("/[controller]/ServerBounds")]
        public string GetServerBounds()
        {
            var bounds = cache.Get<ServerSetting>("settings");
            return bounds.SouthBound + "|" + bounds.WestBound + "|" + bounds.NorthBound + "|" + bounds.EastBound;
        }

        [HttpDelete]
        [Route("/[controller]/Account/")]
        public int DeleteUser()
        {
            //GDPR compliance requires this to exist and be available to the user. 
            //Custom games that attach players to locations may need additional logic to fully meet legal requirements.
            //These 2 lines require PraxisAuth enabled, which you should have on anyways if you're using accounts.
            var accountId = Response.Headers["X-account"].ToString();
            var password = Response.Headers["X-internalPwd"].ToString();

            if (!GenericData.CheckPassword(accountId, password))
                return 0;

            var db = new PraxisContext();
            var removing = db.PlayerData.Where(p => p.DeviceID == accountId).ToArray();
            db.PlayerData.RemoveRange(removing);

            var removeAccount = db.AuthenticationData.Where(p => p.accountId == accountId).ToList();
            db.AuthenticationData.RemoveRange(removeAccount);
            return db.SaveChanges();
        }

        [HttpGet]
        [Route("/[controller]/UseAntiCheat")]
        public bool UseAntiCheat()
        {
            return Configuration.GetValue<bool>("enableAntiCheat");
        }

        [HttpGet]
        [Route("/[controller]/MOTD")]
        [Route("/[controller]/Message")]
        public string MessageOfTheDay()
        {
            var db = new PraxisContext(); //NOTE: not using the cached ServerSettings table, since this might change on the fly.
            var message = db.ServerSettings.First().MessageOfTheDay;
            return message;
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

        [HttpGet]
        [Route("/[controller]/Login/{accountId}/{password}")]
        public AuthDataResponse Login(string accountId, string password)
        {
            Response.Headers.Add("X-noPerfTrack", "Server/Login/VARSREMOVED");
            var db = new PraxisContext();
            if (GenericData.CheckPassword(accountId, password))
            {
                Guid token = Guid.NewGuid();
                var intPassword = GenericData.GetInternalPassword(accountId, password);
                PraxisAuthentication.RemoveEntry(accountId);
                PraxisAuthentication.AddEntry(new AuthData(accountId, intPassword, token.ToString(), DateTime.UtcNow.AddSeconds(1800)));
                return new AuthDataResponse(token, 1800);
            }
            return null;
        }

        [HttpPut]
        [Route("/[controller]/CreateAccount/{accountId}/{password}")]
        public bool CreateAccount(string accountId, string password)
        {
            Response.Headers.Add("X-noPerfTrack", "Server/CreateAccount/VARSREMOVED");

            var db = new PraxisContext();
            var exists = db.AuthenticationData.Any(a => a.accountId == accountId);
            if (exists)
                return false;

            GenericData.SetSecurePlayerData(accountId, "password", Guid.NewGuid().ToString().ToByteArrayASCII(), password);
            return GenericData.EncryptPassword(accountId, password, Configuration.GetValue<int>("PasswordRounds"));
        }

        [HttpPut]
        [Route("/[controller]/ChangePassword/{accountId}/{passwordOld}/{passwordNew}")]
        public bool ChangePassword(string accountId, string passwordOld, string passwordNew)
        {
            Response.Headers.Add("X-noPerfTrack", "Server/ChangePassword/VARSREMOVED");
            if (GenericData.CheckPassword(accountId, passwordOld))
                return GenericData.EncryptPassword(accountId, passwordNew, Configuration.GetValue<int>("PasswordRounds"));

            return false;
        }

        [HttpPut]
        [Route("/[controller]/RandomPoint")]
        public string RandomPoint()
        {
            var bounds = cache.Get<DbTables.ServerSetting>("settings");
            return PraxisCore.Place.RandomPoint(bounds);
        }
    }
}

