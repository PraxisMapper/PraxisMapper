using System;

namespace PraxisCore.GameTools
{
    public static class AreaCalculator
    {
        //This is for area-control games where some resource determines the size of the area controlled.
        //Presuambly here, you take your resources and convert those into the area controlled in degrees, and then
        //these functions tell you how big you'll want the circle/square to be.

        public static double GetCircleRadius(double area)
        {
            return Math.Round(Math.Sqrt(area / Math.PI) / ConstantValues.resolutionCell10);
        }

        public static double GetSquareLength(double area) 
        {
            return Math.Sqrt(area);
        }
        //TODO: rectangle with golden ratio?

        //TODO: move these to Godotcomponents as well
    }
}
