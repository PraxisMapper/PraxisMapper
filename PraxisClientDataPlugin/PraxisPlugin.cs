using PraxisCore;
using PraxisCore.Support;

namespace PraxisChatPlugin
{
    public class PraxisClientDataPlugin : IPraxisPlugin
    {
        public string PrivacyPolicy => "";
        public string PluginName => "ClientData";
        public float Version => 1.0f;
        public List<DbTables.StyleEntry> Styles => new List<DbTables.StyleEntry>();

        public void OnCreateAccount(string accountId)
        {
            return;
        }

        public void OnDeleteAccount(string accountId)
        {
            return;
        }

        public void Startup()
        {
            return;
        }
    }
}
