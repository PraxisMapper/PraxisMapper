using PraxisCore.Support;
using PraxisMapper.Classes;
using static PraxisMastodonPlugin.MastodonGlobals;

namespace PraxisMastodonPlugin
{
    public class MastodonStartup : IPraxisStartup
    {
        static bool initialized = false;
        public static void Startup()
        {
            if (initialized)
                return;

            //add mastodon endpoints to access whitelist
            PraxisAuthentication.whitelistedPaths.Add("/.well-known/webfinger");
            PraxisAuthentication.whitelistedPaths.Add("/announcements"); //should cover all sub-paths.


            UserName = accountName;
            DisplayName = "PraxisMapper Announcements";
            Note = "Automated Mastodon account for PraxisMapper instance at " + serverName;



            //Set Followers list

            //Set OUtbox contents.



        }
    }
}
