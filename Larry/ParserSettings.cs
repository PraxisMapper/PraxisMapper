namespace Larry
{
    public static class ParserSettings
    {
        //Do multiple passes on all pbf files regardless of size.
        public static bool ForceSeparateFiles = false;

        //TODO: load options from file somewhere.
        public static string PbfFolder = @"D:\Projects\OSM Server Info\XmlToProcess\";
        public static string JsonMapDataFolder = @"D:\Projects\OSM Server Info\Trimmed JSON Files\";
        public static long FilesizeSplit = 400000000;

        //If false, round coords to 7 decimal places and simplify paths to a Cell10's width.
        //If true, use coords as-is (cast to float from double), and don't simplify path data.
        //Allowing paths to be simplified and coords rounded to 7 places uses significantly less storage (945MB of MapData.json vs 596MB, 36% smaller)
        public static bool UseHighAccuracy = true;
    }
}
