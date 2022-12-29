using Microsoft.AspNetCore.Mvc;
using PraxisCore.Support;

namespace PraxisMastodonPlugin.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class MastondonController : Controller, IPraxisPlugin
    {

        //Current Scope: Allow the server to send messages out to followers. Not users through their account, and certainly not hosting user data.
        //May need to implement Startup to load followers (and/or old posts?)
        string accountName = "annoucements";
        string serverName = "us.praxismapper.org";

        List<string> followers = new List<string>(); //TODO: persist list.

        public MastondonController()
        {
        }

        
        [HttpGet]
        [Route("/.well-known/webfinger")]
        public string Webfinger()
        {
            return "{ 'subject':'acct:" + accountName + "@" + serverName +
                "'links':[{" +
                "'rel':'self', 'type':'application/activity+json', 'href':'" + serverName + "/serverActor'" +
                "}]"               
                +  "}";
        }

        [HttpGet]
        [Route("/serverActor")]
        public string ServerActor()
        {
            return "{'@context': ['https://www.w3.org/ns/activitystreams','https://w3id.org/security/v1']," +
                "'id': 'https://us.praxismapper.org/serverActor','type': 'Application','preferredUsername': '" + accountName + "','inbox': '" + serverName + "/inbox'," +
                "'publicKey': {'id': '" + serverName + "/serverActor#main-key','owner': '" + serverName + "/serverActor','publicKeyPem': '-----BEGIN PUBLIC KEY-----...-----END PUBLIC KEY-----'}"
                //TODO: generate placeholder keys.
                + "}";
        }

        public string Inbox()
        {
            //Only accept follow requests, and do so automatically.
            return "";
        }

        public string Outbox()
        {
            //This might be where I send messages out?
            return "";
        }
    }
}