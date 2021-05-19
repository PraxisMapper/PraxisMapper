namespace Larry
{
    public static class ParserSettings
    {
        //TODO: should these be in another appsettings.json file? Probably

        //Do multiple passes on all pbf files regardless of size.
        public static bool ForceSeparateFiles = false;

        public static string PbfFolder = @"D:\Projects\PraxisMapper Files\XmlToProcess\";
        public static string JsonMapDataFolder = @"D:\Projects\PraxisMapper Files\Trimmed JSON Files\";
        public static long FilesizeSplit = 400000000;

        //If false, round coords to 7 decimal places and simplify paths to a Cell10's width.
        //If true, use coords as-is (cast to float from double), and don't simplify path data.
        //Setting to false uses significantly less storage (945MB of MapData.json vs 596MB, 36% smaller)
        //and keeps areas very close to the same for Cell10 gameplay purposes but maptiless look worse.
        public static bool UseHighAccuracy = true;

        //public static string DbMode = "SQLServer";
        //public static string DbConnectionString = "Data Source=localhost\\SQLDEV;UID=GpsExploreService;PWD=lamepassword;Initial Catalog=Praxis;";

        public static string DbMode = "MariaDB";
        public static string DbConnectionString = "server=localhost;database=praxis;user=root;password=asdf;";

        //public static string DbMode = "PostgreSQL";
        //public static string DbConnectionString = "server=localhost;database=praxis;user=root;password=asdf;";

        
    }
}
