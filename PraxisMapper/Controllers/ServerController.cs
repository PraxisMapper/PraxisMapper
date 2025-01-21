using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using PraxisCore;
using PraxisMapper.Classes;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using static PraxisCore.DbTables;

namespace PraxisMapper.Controllers
{
    [Route("[controller]")]
    [ApiController]
    public class ServerController : Controller
    {
        //For endpoints that relay information about the server itself. not game data.
        private readonly IConfiguration Configuration;
        private static IMemoryCache cache;

        public ServerController(IConfiguration configuration, IMemoryCache memoryCacheSingleton)
        {
            Configuration = configuration;
            cache = memoryCacheSingleton;
        }

        [HttpGet]
        [Route("/[controller]/Test")]
        [EndpointSummary("Returns \"OK\" to prove the connection works")]
        [EndpointDescription("This is intended to let the dev/admin confirm the server is up and configured as expected. May return the maintenance message if one is set instead.")]
        public string Test()
        {
            return "OK";
        }

        [HttpGet]
        [Route("/[controller]/ServerBounds")]
        [EndpointSummary("Find the geometric area the server supports")]
        [EndpointDescription("Returns a string in \"minLat|minLon|maxLat|maxLon\" format, indicating the bounding box where the server will process requests. The server, by default, checks bounds on most area-based requests and rejects any that are outside of this box. It's set during initial bootstrapping after data is loaded, and can be adjusted manually by editing the values in the ServerSettings database table.")]
        public string GetServerBounds()
        {
            var bounds = cache.Get<ServerSetting>("settings");
            return bounds.SouthBound + "|" + bounds.WestBound + "|" + bounds.NorthBound + "|" + bounds.EastBound;
        }

        [HttpDelete]
        [Route("/[controller]/Account/")]
        [EndpointSummary("Deletes the requesting user's account.")]
        [EndpointDescription("Returns  the number of rows deleted from the database. Initially implemented as GDPR compliance, made available to all users. Expected to be called from the GDPR page, but could be exposed in a client. Determines user by checking headers, as logged in actions will send an AuthKey connected to the username and password.")]
        public int DeleteUser()
        {
            //GDPR compliance requires this to exist and be available to the user. 
            //Custom games that attach players to locations may need additional logic to fully meet legal requirements.
            PraxisAuthentication.GetAuthInfo(Response, out var accountId, out var password);

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
        [EndpointSummary("Incomplete, Do not use")]
        [EndpointDescription("The current AntiCheat logic is horribly incomplete, and cannot be recommended. Have the server do all the work if you want data to be verifiable.")]
        public bool UseAntiCheat()
        {
            return Configuration.GetValue<bool>("enableAntiCheat");
        }

        [HttpGet]
        [Route("/[controller]/MOTD")]
        [Route("/[controller]/Message")]
        [EndpointSummary("Get a Message Of The Day ")]
        [EndpointDescription("Some games may have a quick bit of info from the developer pop up somewhere that changes frequently, this is intended to enable that.")]
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
        [EndpointSummary("Incomplete, do not use.")]
        [EndpointDescription("This is a very early attempt an anti-cheat, where the client uploads a file and the server validates its the same, correct file. This isn't terribly solid anti-cheat, as a malicious client simply needs to upload the original code file and use its own modified version instead.")]
        public void AntiCheat([Description("a filename")]string filename)
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
        [EndpointSummary("Log in to the server")]
        [EndpointDescription("Put the username/password in the JSON body posted here, not in the query string parameters. That process works, but isnt't secure without SSL (and maybe even then)")]
        public AuthDataResponse Login([Description("DEPRECATED - The user's account id")]string accountId = null,
            [Description("DEPRECATED - The password for the account")] string password = null)
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
                if (Request.Headers.TryGetValue("AuthKey", out var auth))
                {
                    PraxisAuthentication.RemoveEntry(auth);
                }
                PraxisAuthentication.AddEntry(new AuthData(accountId, intPassword, token.ToString(), DateTime.UtcNow.AddSeconds(authTimeout), ignoreBan));
                return new AuthDataResponse(token, authTimeout);
            }
            return null;
        }

        [HttpGet]
        [HttpPut]
        [Route("/[controller]/Logout")]
        [EndpointSummary("Log a user out.")]
        [EndpointDescription("Removes the user's AuthKey from memory")]
        public void Logout()
        {
            if (!Request.Headers.TryGetValue("AuthKey", out var key))
                return;
            PraxisAuthentication.RemoveEntry(key);
        }

        [HttpPut]
        [Route("/[controller]/CreateAccount")]
        [Route("/[controller]/CreateAccount/{accountId}/{password}")]
        [EndpointSummary("Create a new account")]
        [EndpointDescription("Put the username/password in the JSON body posted here, not in the query string parameters. That process works, but isnt't secure without SSL (and maybe even then). This call IS SUPPOSED TO be slow. PraxisMapper uses BCrypt to secure account info, and the rough target time for solid security is about 250ms to create a password hash.")]
        public bool CreateAccount([Description("DEPRECATED - The user's account id")] string accountId = null,
            [Description("DEPRECATED - The password for the account")] string password = null)
        {
            Response.Headers.Add("X-noPerfTrack", "Server/CreateAccount/VARSREMOVED");

            if (accountId == null)
            {
                //read from JSON
                var data = Request.ReadBody();
                var decoded = GenericData.DeserializeAnonymousType(data, new { accountId = "", password = "" });
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

            var passwordMade = GenericData.EncryptPassword(accountId, password, Configuration.GetValue<int>("PasswordRounds"));

            foreach (var p in GlobalPlugins.plugins)
            {
                //We haven't logged in yet, so we shouldn't give plugins the login password. They can use the internal data password after this step.
                p.OnCreateAccount(accountId);
            }

            return passwordMade;
        }

        [HttpPut]
        [Route("/[controller]/ChangePassword")]
        [Route("/[controller]/ChangePassword/{accountId}/{passwordOld}/{passwordNew}")]
        [EndpointSummary("Change the password on an account.")]
        [EndpointDescription("Put the username/passwords in the JSON body posted here, not in the query string parameters. That process works, but isnt't secure without SSL (and maybe even then). This call IS SUPPOSED TO be slow. PraxisMapper uses BCrypt to secure account info, and the rough target time for solid security is about 250ms to create a password hash. This will be slower than creating an account, because this must update ALL secure data rows in the database for this account.")]
        public bool ChangePassword([Description("DEPRECATED - The user's account id")] string accountId = null,
            [Description("DEPRECATED - The user's current password")] string passwordOld = null, 
            [Description("DEPRECATED - The user's future/new password")] string passwordNew = null)
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
        [EndpointSummary("get a random point anywhere within the server boundaries")]
        [EndpointDescription("returns a random point anywhere within the server boundaries in \"lat|lon\" format. This does't save anyting to the server, so it's specific to whichever client requested this and may not be useful if the bounds are larger than a single city.")]
        public string RandomPoint()
        {
            var bounds = cache.Get<DbTables.ServerSetting>("settings");
            return PraxisCore.Place.RandomPoint(bounds);
        }

        [HttpGet]
        [Route("/[controller]/RandomValues/{plusCode}/{count}")]
        [EndpointSummary("Get random numbers based on a specific area")]
        [EndpointDescription("This was added as a way for the server to pass clients the same random numbers for the same area. This is probably better implemented on the client, but this remains available here.")]
        public List<int> GetRandomValuesForArea([Description("the PlusCode to get values for")] string plusCode,
            [Description("how many values to return")] int count)
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
        [EndpointSummary("Export all saved data on the user, per GDPR law")]
        [EndpointDescription("Put the username/password in the JSON body posted here, not in the query string parameters. That process works, but isnt't secure without SSL (and maybe even then). GDPR compliance requires users to be able to request all the data on them. This allows for this without manual admin effort. Since this is the user requesting their own data, encrypted data keys are DECRYPTED and returned to the user. The first line is the account data, in \"key1:value1|key2:value2\" format for the accountID, bannedUntil, bannedReason, and isAdmin columns. The remaining lines will be entries from the PlayerData table for the accountId, in \"key:value/n\" format. Lines that were encrypted in the database will have \"[DECRYPTED]\" after \"key\" in the response.")]
        public string Export([Description("DEPRECATED - the account id to pull data from")] string username = "",
            [Description("DEPRECATED - the password for the account in question")] string pwd = "")
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
        [EndpointSummary("Opens the GDPR self-service page.")]
        public IActionResult GDPR()
        {
            return View();
        }

        [HttpGet]
        [Route("/[controller]/PrivacyPolicy")]
        [EndpointSummary("See the PraxisMapper Privacy Policy")]
        [EndpointDescription("I initially added this because the Google Play Store requires a Privacy Policy from a live webserver. This will include the hard-coded PraxisMapper value, and then adds on any privacy policy addendums found in any of the plugins.")]
        public string GetAllPrivacyPolicyInfo()
        {
            //Default is hard-coded.
            //Addendum is loaded from DB.
            //Rest is loaded from plugins.

            StringBuilder fullPolicy = new StringBuilder();

            //Default text
            fullPolicy.AppendLine("<h1>PraxisMapper Core Privacy Policy</h1><br />");

            fullPolicy.AppendLine("<h2>Data Collected:</h2><br />");
            fullPolicy.AppendLine("As a locative game, PraxisMapper uses your location when and as provided by a client application. No other personal data is " +
                "required or used. Data is only collected during gameplay, when a client is open and the user is logged in to play. " +
                "Game-specific information may be created and relayed between client and server, and game-specific interactions may be visible in-game " +
                "to other players. It is possible for some data to be logged by other applications or infrastructure depending on setup and function calls used by " +
                "client. A good-faith effort is made to keep identification data and location data separate when possible to minimize the possibility of a server owner " +
                "identifying individual players when possible. All data that connects a user, a location, and a time is stored encrypted by a key only accessible for " +
                "that user while they are actively logged in.<br />" +
                "Data collected may include: IP address, precise location, other information captured by server logs such as User-Agent strings."
            );

            fullPolicy.AppendLine("<h2>Data Use:</h2><br />");
            fullPolicy.AppendLine("No data used by the server is shared with any outside person, company, or other entity. Analytics are not present in an " +
                "unmodified Praxismapper server. Data is used exclusively for gameplay purposes. You can request all data attached to your account for your viewing, and can request that data be deleted. If requested, the data " +
                "will be decrypted before being provided, since it's your data and not a secret to you.<br />");

            //TODO: server-specific addendum. Probably to be added in the database somewhere.

            foreach (var plugin in GlobalPlugins.plugins)
            {
                if (!string.IsNullOrWhiteSpace(plugin.PrivacyPolicy))
                {
                    fullPolicy.AppendLine("<br />");
                    fullPolicy.AppendLine(plugin.PrivacyPolicy);
                }
            }

            return fullPolicy.ToString();
        }
    }
}