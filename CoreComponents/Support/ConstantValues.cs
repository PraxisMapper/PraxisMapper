namespace CoreComponents
{
    public static class ConstantValues
    {
        //the 11th digit uses a 4x5 grid, not a 20x20. They need separate scaling values for X and Y and are rectangular even at the equator.
        public const double resolutionCell12Lat = .000025 / 5;
        public const double resolutionCell12Lon = .00003125 / 4; //12-digit plus codes are... pretty small.
        public const double resolutionCell11Lat = .000025;
        public const double resolutionCell11Lon = .00003125; //11-digit plus codes are approx. 3.5m ^2
        public const double resolutionCell10 = .000125; //the size of a 10-digit PlusCode, in degrees. Approx. 14 meters^2
        public const double resolutionCell8 = .0025; //the size of a 8-digit PlusCode, in degrees. Approx. 275 meters^2
        public const double resolutionCell6 = .05; //the size of a 6-digit PlusCode, in degrees. Approx. 5.5 km^2
        public const double resolutionCell4 = 1; //the size of a 4-digit PlusCode, in degrees. Approx. 110km ^2
        public const double resolutionCell2 = 20; //the size of a 2-digit PlusCode, in degrees. Approx 2200km ^2

        public const double squareCell10Area = resolutionCell10 * resolutionCell10; //for area-control calculations.
    }
}
