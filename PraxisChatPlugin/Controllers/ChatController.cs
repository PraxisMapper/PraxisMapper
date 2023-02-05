using Microsoft.AspNetCore.Mvc;
using PraxisCore;
using PraxisCore.Support;
using PraxisMapper.Classes;

namespace PraxisChatPlugin.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class ChatController : Controller, IPraxisPlugin
    {
        int chatLengthLines = 100;

        public ChatController() //TODO: cache chat for performance, allow configuration.
        {
        }

        [HttpGet]
        [Route("/[controller]/Region/{region}")]
        public List<string> ReadRegionChat(string region)
        {
            //region is a PlusCode 2-8 chars long.
            var chat = GenericData.GetAreaData<List<string>>(region, "chatLog");
            return chat;
        }

        [HttpPut]
        [Route("/[controller]/Region/{region}")]
        public List<string> WriteRegionChat(string region)
        {
            //user comes from PraxisAuth.
            //message may come from body.
            PraxisAuthentication.GetAuthInfo(Response, out var accountId, out var password);
            if (IsUserBanned(accountId))
                return null;

            var message = GenericData.ReadBody(Request.BodyReader, (int)Request.ContentLength).FromJsonBytesTo<string>();
            List<string> chat;
            var chatLock = SimpleLockable.GetUpdateLock(region + "chatLog");
            lock (chatLock)
            {
                chat = GenericData.GetAreaData<List<string>>(region, "chatLog");
                chat.Add(accountId + ": " + message);
                if (chat.Count > chatLengthLines)
                    chat = chat.TakeLast(chatLengthLines).ToList();

                GenericData.SetAreaDataJson(region, "chatLog", chat.ToJsonByteArray());
            }
            SimpleLockable.DropUpdateLock(region + "chatLog");

            return chat;
        }

        [HttpGet]
        [Route("/[controller]/Channel/{channel}")]
        public List<string> ReadChannelChat(string channel)
        {
            //channel is any string.
            var chat = GenericData.GetGlobalData<List<string>>(channel + "chatLog");
            return chat;

        }

        [HttpPut]
        [Route("/[controller]/Channel/{channel}")]
        public List<string> WriteChannelChat(string channel)
        {
            PraxisAuthentication.GetAuthInfo(Response, out var accountId, out var password);
            if (IsUserBanned(accountId))
                return null;

            var message = GenericData.ReadBody(Request.BodyReader, (int)Request.ContentLength).FromJsonBytesTo<string>();
            List<string> chat;
            var chatLock = SimpleLockable.GetUpdateLock(channel + "chatLog");
            lock (chatLock)
            {
                chat = GenericData.GetGlobalData<List<string>>(channel + "chatLog");
                chat.Add(accountId + ": " + message);
                if (chat.Count > chatLengthLines)
                    chat = chat.TakeLast(chatLengthLines).ToList();

                GenericData.SetGlobalDataJson(channel + "chatLog", chat.ToJsonByteArray());
            }
            SimpleLockable.DropUpdateLock(channel + "chatLog");

            return chat;
        }


        [HttpDelete]
        [Route("/[controller]/Region/{region}")]
        public void DeleteRegionMessage(string region)
        {
            PraxisAuthentication.GetAuthInfo(Response, out var accountId, out var password);
            if (!PraxisAuthentication.IsAdmin(accountId))
                return;

            var message = GenericData.ReadBody(Request.BodyReader, (int)Request.ContentLength).FromJsonBytesTo<string>();
            var chatLock = SimpleLockable.GetUpdateLock(region + "chatLog");
            lock (chatLock)
            {
                var chat = GenericData.GetAreaData<List<string>>(region, "chatLog");
                chat.Remove(message);
                GenericData.SetAreaDataJson(region, "chatLog", chat.ToJsonByteArray());
            }
            SimpleLockable.DropUpdateLock(region + "chatLog");
        }

        [HttpDelete]
        [Route("/[controller]/Channel/{channel}")]
        public void DeleteChannelMessage(string channel)
        {
            PraxisAuthentication.GetAuthInfo(Response, out var accountId, out var password);
            if (!PraxisAuthentication.IsAdmin(accountId))
                return;

            var message = GenericData.ReadBody(Request.BodyReader, (int)Request.ContentLength).FromJsonBytesTo<string>();
            var chatLock = SimpleLockable.GetUpdateLock(channel + "chatLog");
            lock (chatLock)
            {
                var chat = GenericData.GetGlobalData<List<string>>(channel + "chatLog");
                chat.Remove(message);
                GenericData.SetGlobalDataJson(channel + "chatLog", chat.ToJsonByteArray());
            }
            SimpleLockable.DropUpdateLock(channel + "chatLog");
        }

        [HttpPut]
        [Route("/[controller]/ChatBan/{username}")]
        public string ChatBanUser(string username)
        {
            PraxisAuthentication.GetAuthInfo(Response, out var accountId, out var password);
            if (!PraxisAuthentication.IsAdmin(accountId))
                return "No";

            GenericData.SetPlayerData(username, "isChatBanned", true.ToJsonByteArray());
            return "OK";
        }

        public bool IsUserBanned(string username)
        {
            var result = GenericData.GetPlayerData<bool>(username, "isChatBanned");
            return result;
        }
    }
}