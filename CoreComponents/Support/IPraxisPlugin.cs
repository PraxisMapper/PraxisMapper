using System;

namespace PraxisCore.Support
{
    public interface IPraxisPlugin
    {
        //NOTE: additions here need to be made to Chat, Demos, Muni, Offline?, Slippy, and Version plugins, plus actual games.
        //public string PrivacyPolicy; //
        //public List<DbTables.StyleEntry> Styles; //could automatically import styles?
    }

    public interface IPraxisStartup
    {
        public static void Startup() => throw new NotImplementedException();
    }

}
