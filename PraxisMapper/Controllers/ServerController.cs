using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using PraxisCore;
using PraxisMapper.Classes;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
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

        /// <summary>
        /// Returns "OK" to prove the connection works
        /// </summary>
        /// <returns>The string "OK"</returns>
        /// <remarks>This is intended to let the dev/admin confirm the server is up and configured as expected.</remarks>
        [HttpGet]
        [Route("/[controller]/Test")]
        public string Test()
        {
            //Used for clients to test if server is alive. Returns OK normally, clients should check for non-OK results to display as a maintenance message.
            return "OK";
        }

        /// <summary>
        /// Find the geometric area the server supports
        /// </summary>
        /// <returns>A string in "minLat|minLon|maxLat|maxLon" format, indicating the bounding box where the server will process requests.</returns>
        /// <remarks>The server, by default, checks bounds on most area-based requests and rejects any that are outside of this box. 
        /// It's set during initial bootstrapping after data is loaded, and can be adjusted manually by editing the values in the ServerSettings database table.</remarks>
        [HttpGet]
        [Route("/[controller]/ServerBounds")]
        public string GetServerBounds()
        {
            var bounds = cache.Get<ServerSetting>("settings");
            return bounds.SouthBound + "|" + bounds.WestBound + "|" + bounds.NorthBound + "|" + bounds.EastBound;
        }

        /// <summary>
        /// Deletes the requesting user's account.
        /// </summary>
        /// <returns>the number of rows deleted from the database</returns>
        /// <remarks>Initially implemented as GDPR compliance, made available to all users. 
        /// Expected to be called from the GDPR page, but could be exposed in a client.
        /// Determines user by checking headers, as logged in actions will send an AuthKey connected to the username and password.</remarks>
        [HttpDelete]
        [Route("/[controller]/Account/")]
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

        /// <summary>
        /// Incomplete, Do not use
        /// </summary>
        /// <returns>a bool indicating if anti-cheat is enabled</returns>
        /// <remarks>The current AntiCheat logic is horribly incomplete, and cannot be recommended. Have the server do all the work if you want data to be verifiable.</remarks>
        [HttpGet]
        [Route("/[controller]/UseAntiCheat")]
        public bool UseAntiCheat()
        {
            return Configuration.GetValue<bool>("enableAntiCheat");
        }

        /// <summary>
        /// Get a Message Of The Day 
        /// </summary>
        /// <returns>A string to use as the Message of the Day</returns>
        /// <remarks>Some games may have a quick bit of info from the developer pop up somewhere that changes frequently, this is intended to enable that.</remarks>
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

        /// <summary>
        /// Incomplete, do not use.
        /// </summary>
        /// <param name="filename">a filename</param>
        /// <remarks>This is a very early attempt an anti-cheat, where the client uploads a file and the server validates its the same, correct file.
        /// This isn't terribly solid anti-cheat, as a malicious client simply needs to upload the original code file and use its own modified version instead.</remarks>
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

        /// <summary>
        /// Log in to the server
        /// </summary>
        /// <param name="accountId">DEPRECATED - The user's account id</param>
        /// <param name="password">DEPRECATED - The password for the account</param>
        /// <returns>an AuthDataResponse if the username and password are valid and match, or nothing if they don't match, don't exist, or the user is banned.  </returns>
        /// <remarks>Put the username/password in the JSON body posted here, not in the query string parameters. That process works, but isnt't secure without SSL (and maybe even then)</remarks>
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
                if (Request.Headers.TryGetValue("AuthKey", out var auth))
                {
                    PraxisAuthentication.RemoveEntry(auth);
                }
                PraxisAuthentication.AddEntry(new AuthData(accountId, intPassword, token.ToString(), DateTime.UtcNow.AddSeconds(authTimeout), ignoreBan));
                return new AuthDataResponse(token, authTimeout);
            }
            return null;
        }

        /// <summary>
        /// Log a user out.
        /// </summary>
        /// <remarks>Removes the user's AuthKey from memory </remarks>
        [HttpGet]
        [HttpPut]
        [Route("/[controller]/Logout")]
        public void Logout()
        {
            if (!Request.Headers.TryGetValue("AuthKey", out var key))
                return;
            PraxisAuthentication.RemoveEntry(key);
        }

        /// <summary>
        /// Create a new account
        /// </summary>
        /// <param name="accountId">DEPRECATED - The user's account id</param>
        /// <param name="password">DEPRECATED - The password for the account</param>
        /// <returns>true if the account was created, and false if not.</returns>
        /// <remarks>Put the username/password in the JSON body posted here, not in the query string parameters. That process works, but isnt't secure without SSL (and maybe even then).
        /// This call IS SUPPOSED TO be slow. PraxisMapper uses BCrypt to secure account info, and the rough target time for solid security is about 250ms to create a password hash.</remarks>
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

            return GenericData.EncryptPassword(accountId, password, Configuration.GetValue<int>("PasswordRounds"));
        }

        /// <summary>
        /// Change the password on an account.
        /// </summary>
        /// <param name="accountId">DEPRECATED - The user's account id</param>
        /// <param name="passwordOld">DEPRECATED - The user's current password</param>
        /// <param name="passwordNew">DEPRECATED - The user's future/new password</param>
        /// <returns>true if the password was changed, or false if it wasnt</returns>
        /// <remarks>Put the username/passwords in the JSON body posted here, not in the query string parameters. That process works, but isnt't secure without SSL (and maybe even then).
        /// This call IS SUPPOSED TO be slow. PraxisMapper uses BCrypt to secure account info, and the rough target time for solid security is about 250ms to create a password hash.
        /// This will be slower than creating an account, because this must update ALL secure data rows in the database for this account.
        /// </remarks>
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

        /// <summary>
        /// get a random point anywhere within the server boundaries
        /// </summary>
        /// <returns>a random point anywhere within the server boundaries in "lat|lon" format</returns>
        /// <remarks> This does't save anyting to the server, so it's specific to whichever client requested this and may not be useful if the bounds are larger than a single city.</remarks>
        [HttpGet]
        [Route("/[controller]/RandomPoint")]
        public string RandomPoint()
        {
            var bounds = cache.Get<DbTables.ServerSetting>("settings");
            return PraxisCore.Place.RandomPoint(bounds);
        }

        /// <summary>
        /// Get random numbers based on a specific area
        /// </summary>
        /// <param name="plusCode">the PlusCode to get values for</param>
        /// <param name="count">how many values to return</param>
        /// <returns>An array of integers</returns>
        /// <remarks>This was added as a way for the server to pass clients the same random numbers for the same area.
        /// This is probably better implemented on the client, but this remains available here.</remarks>
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

        /// <summary>
        /// Export all saved data on the user, per GDPR law
        /// </summary>
        /// <param name="username">DEPRECATED - the account id to pull data from</param>
        /// <param name="pwd">DEPRECATED - the password for the account in question</param>
        /// <returns>The internal data on the account, plus all data keys stored for the account</returns>
        /// <remarks>Put the username/password in the JSON body posted here, not in the query string parameters. That process works, but isnt't secure without SSL (and maybe even then).
        /// GDPR compliance requires users to be able to request all the data on them. This allows for this without manual admin effort.
        /// Since this is the user requesting their own data, encrypted data keys are DECRYPTED and returned to the user.
        /// The first line is the account data, in "key1:value1|key2:value2" format for the accountID, bannedUntil, bannedReason, and isAdmin columns.
        /// The remaining lines will be entries from the PlayerData table for the accountId, in "key:value/n" format.
        /// Lines that were encrypted in the database will have "[DECRYPTED]" after "key" in the response.
        ///</remarks>
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

        /// <summary>
        /// Opens the GDPR self-service page.
        /// </summary>
        /// <returns>The GDPR self-service view.</returns>
        [HttpGet]
        [Route("/[controller]/Gdpr")]
        public IActionResult GDPR()
        {
            return View();
        }

        /// <summary>
        /// See the PraxisMapper Privacy Policy
        /// </summary>
        /// <returns>The whole of the Privacy Policy</returns>
        /// <remarks>I initially added this because the Google Play Store requires a Privacy Policy from a live webserver.
        /// This will include the hard-coded PraxisMapper value, and then adds on any privacy policy addendums found in any of the plugins.</remarks>
        [HttpGet]
        [Route("/[controller]/PrivacyPolicy")]
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

            //TODO: Plugin information. To be loaded as part of the IPraxisPlugin interface.


            return fullPolicy.ToString();
        }
    }
}

