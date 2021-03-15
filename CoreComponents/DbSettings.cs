namespace CoreComponents
{
    public static class DbSettings
    {
        public static bool processAdmin { get; set; } = true;
        public static bool processBeach { get; set; } = true;
        public static bool processCemetery { get; set; } = true;
        public static bool processHistorical { get; set; } = true;
        public static bool processNatureReserve { get; set; } = true;
        public static bool processPark { get; set; } = true;
        public static bool processRetail { get; set; } = true;
        public static bool processTourism { get; set; } = true;
        public static bool processTrail { get; set; } = true;
        public static bool processUniversity { get; set; } = true;
        public static bool processWater { get; set; } = true;
        public static bool processWetland { get; set; } = true;

        //These ones are for a more thorough area-specific settting.
        public static bool processRoads { get; set; } = true;
        public static bool processBuildings { get; set; } = true;
        public static bool processParking{ get; set; } = true;
    }
}

