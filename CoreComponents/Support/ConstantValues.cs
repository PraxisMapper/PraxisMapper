namespace PraxisCore
{
    public static class ConstantValues
    {
        //the 11th+ digit uses a 4x5 grid, not a 20x20. They need separate scaling values for X and Y and are rectangular even at the equator.
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

        //Slippy map tile zoom levels to degrees per pixel at 512x512 (double numbers for 256x256)
        //TODO: fill in odd levels too by dividing larger number in half?
        //TODO: calculate this in ImageStats by dividing the width of a tile (in degrees) by the number of pixels in the image? Or keep these as static reference?
        public const double zoom4DegPerPixelX =  0.0439453125;
        public const double zoom6DegPerPixelX =  0.010986328125;
        public const double zoom8DegPerPixelX =  0.00274658203125;
        public const float zoom10DegPerPixelX = 0.0006866455078125F;
        public const float zoom12DegPerPixelX = 0.000171661376953125F;
        public const double zoom14DegPerPixelX = 0.00004291534423828125;
        public const double zoom16DegPerPixelX = 0.000010728836059570312;
        public const double zoom18DegPerPixelX = 0.000002682209014892578;
        public const double zoom20DegPerPixelX = 0.0000006705522537231445;

        public const double maptileLineWidthBase = 0.00000625F; //~1/20th of a Cell10. ~1/5th of a Cell11
    }
}
