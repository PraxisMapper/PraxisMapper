using PraxisCore;

namespace PraxisChatPlugin
{
    public static class ChatFunctions
    {
        public static bool IsUserBanned(string username)
        {
            var result = GenericData.GetPlayerData<bool>(username, "isChatBanned");
            return result;
        }
    }
}
