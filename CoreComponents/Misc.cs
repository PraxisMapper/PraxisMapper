using System.Linq;

namespace CoreComponents
{
    public class Misc
    {
        //Stuff that doesn't actually get used here but will be a reference for later, possibly on the mobile-side.

        public static string NameObfuscator(string pluscode)
        {
            //take a plus code, change it to something sharable (requires some research and math to reverse) and human readable.
            var plusCodeArray = pluscode.ToArray();

            //This is an 8 code, I think
            //First 2 are a 9x18 grid, so i need some words there. Remember chars might be backwards for axes, so this might be 18x9
            //could also consider a unique list of 163-ish words for this?
            string[] adjectiveN = new string[9] {"", "", "", "", "", "", "", "", "" }; //index 0
            string[] adjectiveE = new string[18] { "", "", "", "", "", "", "", "", "", "", "", "", "", "", "", "", "", "" }; //index 1

            //next 2 will becomes directional.
            //on the 20 line, it'll be  7-6-7 split for dir-center-dir on each axis.
            var values = Google.OpenLocationCode.OpenLocationCode.CodeAlphabet;

            string directions = "";
            if (values.IndexOf(plusCodeArray[2]) < 7)
                directions = "North";
            else if (values.IndexOf(plusCodeArray[2]) >= 13)
                directions = "South";
            else
                directions = "";

            if (values.IndexOf(plusCodeArray[3]) < 7)
                directions += "East";
            else if (values.IndexOf(plusCodeArray[3]) >= 13)
                directions += "South";
            else
                if (directions == "")
                    directions += "Central";


            //rest is 20x20, which has 400 possible total combinations each.
            //2 pairs of characters left, so 1600 combinations that repeat across the first 4 pairs.
            //so make up some fantasy words here by merging syllables, based on last 4 characters.



            return "";
        }

        public static void LoadCustomData()
        {
            //For loading in your own fixed-place files outside of OSM. Probably most useful for dedicated area games like above.
        }

        //Unused so far. Keeping as a potential future feature.
        //public static GeoPoint ProxyLocation(double lat, double lon, GeoArea bounds)
        //{
        //    //Treat the user like they're in the real-world location
        //    //Mod their location by the box size, then add that to the minimum to get their new location
        //    var shiftX = lon % bounds.LongitudeWidth;
        //    var shiftY = lat % bounds.LatitudeHeight;

        //    double newLat = bounds.SouthLatitude + shiftY;
        //    double newLon = bounds.WestLongitude + shiftX;

        //    return new GeoPoint(newLat, newLon);
        //}
    }
}
