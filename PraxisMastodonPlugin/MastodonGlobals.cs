namespace PraxisMastodonPlugin
{

    public class MastodonPost
    {
        public Guid id;
        public string contents;
    }

    
public static class MastodonGlobals
    {
        public static List<string> followers = new List<string>();
        public static List<string> posts = new List<string>();

    }
}
