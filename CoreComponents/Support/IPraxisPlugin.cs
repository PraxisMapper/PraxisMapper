using System.Collections.Generic;
using static PraxisCore.DbTables;

namespace PraxisCore.Support
{
    public interface IPraxisPlugin
    {
        public string PrivacyPolicy { get; }
        public string PluginName { get; } //Lets us find and call only specific plugins if present.
        public float Version { get; }
        public List<StyleEntry> Styles { get; } //Lets us import styles on startup.
        public void Startup(); // Called for all plugins on server start, if they have any work they need to do .
        public void OnCreateAccount(string accountId); //Lets us do additional actions when an account is created.
        public void OnDeleteAccount(string accountId); //lets us do additional work when an account is deleted.
    }
}
