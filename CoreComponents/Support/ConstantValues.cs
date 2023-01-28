namespace PraxisCore
{
    public static class ConstantValues
    {
        //The original data on GitHub for plus code sizes:
        //This table assumes one degree is 111321 meters, and that all distances are calculated at the equator.
        //chars Degrees             Meters
        //2 	20 	                2226 km
        //4 	1 	                111.321 km
        //6 	1/20 	            5566 meters
        //8 	1/400 	            278 meters
        //10 	1/8000 	            13.9 meters
        //11 	1/40000 x 1/32000 	2.8 x 3.5 meters
        //12 	1/200000 x 1/128000 56 x 87 cm
        //13 	1/1e6 x 1/512000 	11 x 22 cm
        //14 	1/5e6 x 1/2.048e6 	2 x 5 cm
        //15 	1/2.5e7 x 1/8.192e6 4 x 14 mm

        //Count of cells by size, globally
        //2: 162 (18x9 grid)
        //4: 64,800
        //6: 25,920,000 //Max unique hashable entries with 32-bit int is ~4,200,000,000
        //8: 10,368,000,000
        //10: 4,147,200,000,000
        //11: 82,944,000,000,000 (* 20, instead of * 400 per level starting at 11)
        //    9,223,372,036,854,775,807 //max 64-bit long.


        //the 11th+ digit uses a 4x5 grid, not a 20x20. They need separate scaling values for X and Y and are rectangular even at the equator.
        public const double resolutionCell12Lat = .000025 / 5;
        public const double resolutionCell12Lon = .00003125 / 4; 
        public const double resolutionCell11Lat = .000025;
        public const double resolutionCell11Lon = .00003125; 
        public const double resolutionCell10 = .000125; 
        public const double resolutionCell8 = .0025; 
        public const double resolutionCell6 = .05; 
        public const double resolutionCell4 = 1; 
        public const double resolutionCell2 = 20; 

        public const double squareCell10Area = resolutionCell10 * resolutionCell10; //for area-control calculations.
        public const double squareCell11Area = resolutionCell11Lat * resolutionCell11Lon; //for area-control calculations.

        //Slippy map tile zoom levels to degrees per pixel at 512x512 (double numbers for 256x256)
        //NOTE: odd levels can be worked out by dividing a zoom level by 2 (EX: zoom14 / 2 = zoom15)
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
