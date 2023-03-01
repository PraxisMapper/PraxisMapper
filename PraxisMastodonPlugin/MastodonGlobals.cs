namespace PraxisMastodonPlugin
{

    public class MastodonPost
    {
        public Guid id;
        public string contents;
        public DateTime published;
    }

    
public static class MastodonGlobals
    {
        //TODO: make these properties for persisting with JSON?
        public static string accountName = "annoucements";
        public static string serverName = "https://us.praxismapper.org";

        public static List<string> followers = new List<string>();
        public static List<string> posts = new List<string>();

        public static string UserName = accountName;
        public static string DisplayName = "";
        public static string Note = "";
    }
}
