using Microsoft.AspNetCore.Mvc;
using PraxisCore;
using PraxisCore.Support;
using PraxisMapper.Classes;

namespace PraxisChatPlugin.Controllers {
    [ApiController]
    [Route("[controller]")]
    public class ChatController : Controller, IPraxisPlugin {
        readonly int chatLengthLines = 100;

        public ChatController() //TODO: cache chat for performance, allow configuration.
        {
        }

        [HttpGet]
        [Route("/[controller]/Region/{region}")]
        public List<string> ReadRegionChat(string region) {
            //region is a PlusCode 2-8 chars long.
            var chat = GenericData.GetAreaData<List<string>>(region, "chatLog");
            return chat;
        }

        [HttpPut]
        [Route("/[controller]/Region/{region}")]
        public List<string> WriteRegionChat(string region) {
            PraxisAuthentication.GetAuthInfo(Response, out var accountId, out var password);
            if (ChatFunctions.IsUserBanned(accountId))
                return null;

            var message = GenericData.ReadBody(Request.BodyReader, (int)Request.ContentLength).ToUTF8String();
            List<string> chat = new List<string>();
            SimpleLockable.PerformWithLock(region + "chatLog", () => {
                chat = GenericData.GetAreaData<List<string>>(region, "chatLog");
                if (chat == null)
                    chat = new List<string>();
                chat.Add(accountId + ": " + message);
                if (chat.Count > chatLengthLines)
                    chat = chat.TakeLast(chatLengthLines).ToList();

                GenericData.SetAreaDataJson(region, "chatLog", chat);
            });

            return chat;
        }

        [HttpGet]
        [Route("/[controller]/Channel/{channel}")]
        public List<string> ReadChannelChat(string channel) {
            //channel is any string.
            var chat = GenericData.GetGlobalData<List<string>>(channel + "chatLog");
            return chat;

        }

        [HttpPut]
        [Route("/[controller]/Channel/{channel}")]
        public List<string> WriteChannelChat(string channel) {
            PraxisAuthentication.GetAuthInfo(Response, out var accountId, out var password);
            if (ChatFunctions.IsUserBanned(accountId))
                return null;

            var message = GenericData.ReadBody(Request.BodyReader, (int)Request.ContentLength).ToUTF8String();
            List<string> chat = new List<string>();
            SimpleLockable.PerformWithLock(channel + "chatLog", () => {
                chat = GenericData.GetGlobalData<List<string>>(channel + "chatLog");
                if (chat == null)
                    chat = new List<string>();
                chat.Add(accountId + ": " + message);
                if (chat.Count > chatLengthLines)
                    chat = chat.TakeLast(chatLengthLines).ToList();

                GenericData.SetGlobalDataJson(channel + "chatLog", chat);
            });

            return chat;
        }


        [HttpDelete]
        [Route("/[controller]/Region/{region}")]
        public void DeleteRegionMessage(string region) {
            PraxisAuthentication.GetAuthInfo(Response, out var accountId, out var password);
            if (!PraxisAuthentication.IsAdmin(accountId))
                return;

            var message = GenericData.ReadBody(Request.BodyReader, (int)Request.ContentLength).ToUTF8String();
            SimpleLockable.PerformWithLock(region + "chatLog", () => {
                var chat = GenericData.GetAreaData<List<string>>(region, "chatLog");
                chat.Remove(message);
                GenericData.SetAreaDataJson(region, "chatLog", chat);
            });
        }

        [HttpDelete]
        [Route("/[controller]/Channel/{channel}")]
        public void DeleteChannelMessage(string channel) {
            PraxisAuthentication.GetAuthInfo(Response, out var accountId, out var password);
            if (!PraxisAuthentication.IsAdmin(accountId))
                return;

            var message = GenericData.ReadBody(Request.BodyReader, (int)Request.ContentLength).ToUTF8String();
            SimpleLockable.PerformWithLock(channel + "chatLog", () => {
                var chat = GenericData.GetGlobalData<List<string>>(channel + "chatLog");
                chat.Remove(message);
                GenericData.SetGlobalDataJson(channel + "chatLog", chat);
            });
        }

        [HttpPut]
        [Route("/[controller]/ChatBan/{username}")]
        [Route("/[controller]/ChatBan/{username}/{minutes}")]
        public string ChatBanUser(string username, double minutes = 0) {
            PraxisAuthentication.GetAuthInfo(Response, out var accountId, out var password);
            if (!PraxisAuthentication.IsAdmin(accountId))
                return "No";

            if (minutes == 0)
                GenericData.SetPlayerData(username, "isChatBanned", true.ToJsonByteArray());
            else
                GenericData.SetPlayerData(username, "isChatBanned", true.ToJsonByteArray(), minutes * 60);
            return "OK";
        }


    }
}