namespace Larry
{
    public static class ParserSettings
    {
        //Do multiple passes on all pbf files regardless of size.
        public static bool ForceSeparateFiles = false;

        //TODO: load options from file somewhere.
        public static string PbfFolder = @"D:\Projects\PraxisMapper Files\XmlToProcess\";
        public static string JsonMapDataFolder = @"D:\Projects\PraxisMapper Files\Trimmed JSON Files\";
        public static long FilesizeSplit = 400000000;

        //If false, round coords to 7 decimal places and simplify paths to a Cell10's width.
        //If true, use coords as-is (cast to float from double), and don't simplify path data.
        //Setting to false uses significantly less storage (945MB of MapData.json vs 596MB, 36% smaller)
        //and keeps areas very close to the same for Cell10 gameplay purposes but maptiless look worse.
        //TODO: investigate how helpful it is having separate gameplay and maptile server instances with these value set differently
        public static bool UseHighAccuracy = true;
    }
}
