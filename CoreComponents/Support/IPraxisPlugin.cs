using System;
using System.Collections.Generic;
using static PraxisCore.DbTables;

namespace PraxisCore.Support
{
    public interface IPraxisPlugin
    {
        //NOTE: additions here need to be made to Chat, Demos, Muni, Offline?, Slippy, and Version plugins, plus actual games.
        public string PrivacyPolicy { get; }

        //TODO: expand IPraxisPlugin interface, use GlobalPlugins.plugins to track these. Find other points to call.
        //But these also should NOT be in the Controller file, since that screws up the auto-documentation call
        //(It NEEDS every function to be an API endpoint, and if a helper is the in the same file it dies and does not build the app)
        //Thankfully, IPraxisPlugin can be its own class, it just has to be present in the assembly to load the controllers.
        //So redo it to make that its own class.
        public string PluginName { get; } //Lets us find and call only specific plugins if present.
        public float Version { get; }
        public List<StyleEntry> Styles { get; } //Lets us import styles on startup.
        public void Startup();
        public void OnCreateAccount(string accountId); //Lets us do additional actions when an account is created.
        public void OnDeleteAccount(string accountId); //lets us do additional work when an account is deleted.
    }
}
