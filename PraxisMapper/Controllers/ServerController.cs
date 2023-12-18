using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion.Internal;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Identity.Client;
using PraxisCore;
using PraxisMapper.Classes;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
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

            //TODO: use the getauthinfo call here.
            var accountId = Response.Headers["X-account"].ToString();
            var password = Response.Headers["X-internalPwd"].ToString();


            using var db = new PraxisContext();
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            var removing = db.PlayerData.Where(p => p.accountId == accountId).ToArray();
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
            if (cache.TryGetValue<string>("MOTD", out var cached))
                return cached;

            //NOTE: not using the cached ServerSettings table, since this might change without a reboot, but I do cache the results for 15 minutes to minimize DB calls.
            using var db = new PraxisContext();
            db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            var message = db.ServerSettings.First().MessageOfTheDay;
            cache.Set("MOTD", message, DateTimeOffset.UtcNow.AddMinutes(15));
            return message;
        }

        [HttpPut]
        [Route("/[controller]/AntiCheat/{filename}")]
        public void AntiCheat(string filename)
        {
            var endData = GenericData.ReadBody(Request.BodyReader, (int)Request.ContentLength);

            using var db = new PraxisContext();
            db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            var IP = Request.HttpContext.Connection.RemoteIpAddress.ToString();

            if (!PraxisAntiCheat.antiCheatStatus.ContainsKey(IP))
                PraxisAntiCheat.antiCheatStatus.TryAdd(IP, new Classes.AntiCheatInfo());

            if (db.AntiCheatEntries.Any(a => a.filename == filename && a.data == endData))
            {
                var entry = PraxisAntiCheat.antiCheatStatus[IP];
                if (!entry.entriesValidated.Contains(filename))
                    entry.entriesValidated.Add(filename);

                if (entry.entriesValidated.Count == PraxisAntiCheat.expectedCount)
                    entry.validUntil = DateTime.Now.AddHours(24);
            }
        }

        [HttpGet]
        [HttpPut]
        [Route("/[controller]/Login")]
        [Route("/[controller]/Login/{accountId}/{password}")]
        public AuthDataResponse Login(string accountId = null, string password = null)
        {
            Response.Headers.Add("X-noPerfTrack", "Server/Login/VARSREMOVED");
            bool ignoreBan = false;
            if (accountId == null)
            {
                //read from JSON
                var data = Request.ReadBody();
                var decoded = GenericData.DeserializeAnonymousType(data, new { accountId = "", password = "", isGDPR = false });
                accountId = decoded.accountId;
                password = decoded.password;
                ignoreBan = decoded.isGDPR; //If this is set, this login is only good for the GDPR page. Ban will still lock you out of game.
            }
            else
            {
                PraxisPerformanceTracker.LogInfoToPerfData("Login-LegacyPath", "HEY ADMIN - update the client to pass account/password in the body for /Server/Login. ");
            }

            if (GenericData.CheckPassword(accountId, password, ignoreBan))
            {
                int authTimeout = Configuration["authTimeoutSeconds"].ToInt();
                Guid token = Guid.NewGuid();
                var intPassword = GenericData.GetInternalPassword(accountId, password);
                PraxisAuthentication.RemoveEntry(accountId);
                PraxisAuthentication.AddEntry(new AuthData(accountId, intPassword, token.ToString(), DateTime.UtcNow.AddSeconds(authTimeout), ignoreBan));
                return new AuthDataResponse(token, authTimeout);
            }
            return null;
        }

        [HttpPut]
        [Route("/[controller]/CreateAccount")]
        [Route("/[controller]/CreateAccount/{accountId}/{password}")]
        public bool CreateAccount(string accountId = null, string password = null)
        {
            Response.Headers.Add("X-noPerfTrack", "Server/CreateAccount/VARSREMOVED");

            if (accountId == null)
            {
                //read from JSON
                var data = Request.ReadBody();
                var decoded = GenericData.DeserializeAnonymousType(data, new { accountId = "", password = ""});
                accountId = decoded.accountId;
                password = decoded.password;
            }
            else
            {
                PraxisPerformanceTracker.LogInfoToPerfData("CreateAccount-LegacyPath", "HEY ADMIN - update the client to pass account/password in the body for /Server/CreateAccount.");
            }

            using var db = new PraxisContext();
            db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            var exists = db.AuthenticationData.Any(a => a.accountId == accountId);
            if (exists)
                return false;

            return GenericData.EncryptPassword(accountId, password, Configuration.GetValue<int>("PasswordRounds"));
        }

        [HttpPut]
        [Route("/[controller]/ChangePassword")]
        [Route("/[controller]/ChangePassword/{accountId}/{passwordOld}/{passwordNew}")]
        public bool ChangePassword(string accountId = null, string passwordOld = null, string passwordNew = null)
        {
            Response.Headers.Add("X-noPerfTrack", "Server/ChangePassword/VARSREMOVED");
            if (accountId == null)
            {
                //read from JSON
                var data = Request.ReadBody();
                var decoded = GenericData.DeserializeAnonymousType(data, new { accountId = "", passwordOld = "", passwordNew = "" });
                accountId = decoded.accountId;
                passwordOld = decoded.passwordOld;
                passwordNew = decoded.passwordNew;
            }
            else
            {
                PraxisPerformanceTracker.LogInfoToPerfData("ChangePassword-LegacyPath", "HEY ADMIN - update the client to pass account/passwordOld/passwordNew in the body for /Server/ChangePassword.");
            }

            if (GenericData.CheckPassword(accountId, passwordOld))
            {
                GenericData.EncryptPassword(accountId, passwordNew, Configuration.GetValue<int>("PasswordRounds"));

                using var db = new PraxisContext();
                var entries = db.PlayerData.Where(p => p.accountId == accountId && p.IvData != null).ToList();
                foreach (var entry in entries)
                {
                    try
                    {
                        var data = GenericData.DecryptValue(entry.IvData, entry.DataValue, passwordOld);
                        entry.DataValue = GenericData.EncryptValue(data, passwordNew, out var newIVs);
                        entry.IvData = newIVs;
                    }
                    catch
                    {
                        //skip this one, its not using this password.
                    }
                }
                db.SaveChanges();
                return true;
            }

            return false;
        }

        [HttpGet]
        [Route("/[controller]/RandomPoint")]
        public string RandomPoint()
        {
            var bounds = cache.Get<DbTables.ServerSetting>("settings");
            return PraxisCore.Place.RandomPoint(bounds);
        }

        [HttpGet]
        [Route("/[controller]/RandomValues/{plusCode}/{count}")]
        public List<int> GetRandomValuesForArea(string plusCode, int count)
        {
            List<int> values = new List<int>(count);
            var random = plusCode.GetSeededRandom();
            for (int i = 0; i < count; i++)
                values.Add(random.Next());

            return values;
        }

        [HttpGet]
        [Route("/[controller]/GdprExport")]
        [Route("/[controller]/GdprExport/{username}/{pwd}")]
        public string Export(string username = "", string pwd = "")
        {
            Response.Headers.Add("X-noPerfTrack", "Server/GdprExport");
            string accountId = "";
            string password = "";
            //GDPR Compliance Endpoint. Allows a user to decrypt(!) and receive all the data associated with them in the system. All means all, so they get the password strings encrypted.
            if (username == "" && pwd == "")
            {
                PraxisAuthentication.GetAuthInfo(Response, out accountId, out password);
            }
            else
            {
                PraxisPerformanceTracker.LogInfoToPerfData("GdprExport-LegacyPath", "HEY ADMIN - Use one of the login tokens instead of username/password for GdprExport");
                if (!GenericData.CheckPassword(username, pwd))
                {
                    System.Threading.Thread.Sleep(3500);
                    return "Invalid account credentials";
                }

                accountId = username;
                var innerPwd = GenericData.GetInternalPassword(accountId, password);
                password = innerPwd;
            }            

            StringBuilder sb = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(accountId))
            {
                using var db = new PraxisContext();
                var authInfo = db.AuthenticationData.First(a => a.accountId == accountId);
                sb.AppendLine("accountID: " + authInfo.accountId + " | bannedUntil: " + authInfo.bannedUntil + " | bannedReason: " + authInfo.bannedReason + " | isAdmin: " + authInfo.isAdmin);

                var entries = db.PlayerData.Where(p => p.accountId == accountId).ToList();

                foreach (var entry in entries)
                {
                    if (entry.IvData == null)
                        sb.AppendLine(entry.DataKey + " : " + entry.DataValue.ToUTF8String());
                    else
                        sb.AppendLine(entry.DataKey + " [Decrypted] : " + GenericData.DecryptValue(entry.IvData, entry.DataValue, password).ToUTF8String());
                }
            }

            return sb.ToString();
        }

        [HttpGet]
        [Route("/[controller]/Gdpr")]
        public IActionResult GDPR()
        {
            return View();
        }
    }
}

