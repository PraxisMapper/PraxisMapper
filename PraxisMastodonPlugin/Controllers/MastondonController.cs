using Microsoft.AspNetCore.Mvc;
using PraxisCore;
using PraxisCore.Support;
using static PraxisMastodonPlugin.MastodonGlobals;

namespace PraxisMastodonPlugin.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class MastondonController : Controller, IPraxisPlugin
    {

        //Current Scope: Allow the server to send messages out to followers. Not users through their account, and certainly not hosting user data.
        //May need to implement Startup to load followers (and/or old posts?)

        //TODO: do I need to add an endpoint to anything for other servers to verify a public key?

        //This public key was slapped together specifically for testing this. TODO: Replace this once I have proven this plugin works, support loading it from a file or from the DB
        string keyData = "-----BEGIN PUBLIC KEY-----\nMIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEA0Zx+mA4k4xKKRGHpn5v+\nCxayBIIfdcc4HS7RHZ/CXC3KOUh5XljRcGvMdIIUFrnUpECT44yVYeU28opoPtar\neNL3ea19cBVhjyyclWx8sAFmZvA5eqfdxcxR8yrgcEVGPRU+px1D2chO1tPmCpP6\nE/5/S8L2LiuR/EYrpvhbJWqsqJyfxUoakXmuaWJPv/f7CWnRJQ/gEuPlqXYeH3gY\n4WSECf9kM/dpcy/EnUaAJ/np26kclOhp7kOQH2qcMCe+s5DAJIWWf3wpjIeabEYP\nehKyI84kCwk5YIGnrLHECRq0EYUUHmio39urgNdiSE5X/Mdl86H5U3yDRirVtSCs\nXQIDAQAB\n-----END PUBLIC KEY-----";

        MastodonPost tempPost = new MastodonPost() { id = new Guid("12345678-9ABC-DEF0-1234-567890ABCDEF"), contents = "test post!", published = DateTime.UtcNow };
        

         //TODO: persist list of followers and posts.

        public MastondonController()
        {
        }

        
        [HttpGet]
        [Route("/.well-known/webfinger")]
        public string Webfinger()
        {
            return "{ 'subject':'acct:" + accountName + "@" + serverName +
                "'links':[{" +
                "'rel':'self', 'type':'application/activity+json', 'href':'" + serverName + "/" + accountName + "\"" +
                "}]"               
                +  "}";
        }

        [HttpGet]
        [Route("/serverActor")] //Original name
        [Route("/announcements")] //Current working name. TODO: make this the accountName value, if routes can be assigned with variables.
        public string ServerActor()
        {
            return "{'@context': ['https://www.w3.org/ns/activitystreams','https://w3id.org/security/v1']," +
                "'id': 'https://us.praxismapper.org/serverActor','type': 'Application','preferredUsername': '" + accountName + "','inbox': '" + serverName + "/inbox'," +
                "'publicKey': {'id': '" + serverName + "/" + accountName + "#main-key','owner': '" + serverName + "/" + accountName + "','publicKeyPem': '" + keyData + "'}"
                + "}";
        }

        [HttpPost]
        [Route("/announcements/inbox")]
        public string Inbox()
        {
            //Only accept follow requests, and do so automatically. This doesn't persist.
            //TODO: a minimum response to get this working.
            //if request is "type":"Follow", then add "Actor" to the list of followers, reply with an "Accept" activity (or send to their inbox? (add "/inbox" to the end of actor)).

            return "OK"; //the expected answer on a POST here.
        }

        [Route("/announcements/outbox")]
        public string Outbox()
        {
            if (!Request.QueryString.HasValue) //tell the requestor how to request the outbox info.
                return "{ \"@context\": \"https://www.w3.org/ns/activitystreams\", \"id\": \"" + serverName + "/" + accountName + "/outbox\", \"type\": \"OrderedCollection\", \"first\": \"" + serverName + "/" + accountName + "/outbox?page=true\"}";

            if (Request.Query["page"] == "true")
            {
                //PraxisMapper limit: Only keep 1 page, which is 30 entries according to ActivityPub spec.
                string result = "{\"id\": \"" + serverName + "/" + accountName + "/outbox?page=true\", \"type\": \"OrderedCollectionPage\", \"partOf\": \"" + serverName + "/" + accountName + "/outbox\", \"orderedItems\":[";
                //Foreach item, insert to entry.
                var outbox = GenericData.GetGlobalData<List<MastodonPost>>("mastodonOutbox");
                //TODO: a minimum response to get this working. Temporarily shoving in a fixed answer.
                //TODO: might be able to increase perf by checking what server each follower is on, and grouping/batching those into a single request per server.
                outbox = new List<MastodonPost>() { tempPost };
                if (outbox != null)
                    foreach(var p in outbox)
                        result += ConvertPostToJSONLD(p) + ",";
                

                result += "]}";
                return result;
            }           

            return "";
        }

        [Route("/announcements/statuses")]
        [Route("/announcements/statuses/{uid}")]
        public string statuses(Guid uid)
        {
            //Required since the outbox only returns a list of these.
            var statuses = GenericData.GetGlobalData<List<MastodonPost>>("mastodonOutbox");

            //if uid is absent, return all. If uid is given, return only that one.

            return "";
        }

        public string ConvertPostToJSONLD(MastodonPost post)
        {
            string result = "{" +
                "\"id\":\"" + post.id.ToString() + "/activity\"" +
                "\"type\": \"Create\"" +
                "\"actor\":\"" + serverName + "\\" + accountName + "\"" +
                "\"published\":\"" + post.published.ToIso8601() + "\"" +
                "\"to\": [\"https://www.w3.org/ns/activitystreams#Public\"]," +
                "\"cc\": [\"" + serverName + "/" + accountName + "/followers\"]," +
                "\"object\": \"" + serverName + "/" + accountName + "/statuses/" + post.id.ToString() + "\"" +
                "}";

            return result;

        }

        public string SignPost()
        {


            return "";
        }
    }
}