using Google.OpenLocationCode;
using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Prepared;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CoreComponents
{
    public static class DataCheck
    {
        public static bool IsInBounds(IPreparedGeometry bounds, GeoArea place)
        {
            if (bounds.Intersects(Converters.GeoAreaToPolygon(place)))
                return true;

            return false;
        }

        public static bool IsInBounds(IPreparedGeometry bounds, Polygon place)
        {
            if (bounds.Intersects(place))
                return true;

            return false;
        }

        public static bool IsInBounds(IPreparedGeometry bounds, double lat, double lon)
        {
            Point p = new Point(lon, lat);
            if (bounds.Intersects(p))
                return true;

            return false;
        }

        public static bool IsPlusCode(string toCheck)
        {
            //Since PraxisMapper works with non-spec-standard Plus Codes (by using the first part of the code to shorten it, rather than the later part)
            //we have a slightly different set of checks for validity than the default library uses.
            toCheck = toCheck.Replace("+", "").ToUpper().Trim(); //Clean the string up for checks.
            if ((toCheck.Length >= 2 && toCheck.Length <= 12 & (toCheck.Length & 1) == 1)) //If toCheck is between 2 and 12 digits long, and an even number, it might be a plus code.
                return false;

            if (!toCheck.All(t => OpenLocationCode.CodeAlphabet.Contains(t))) //This is a valid length and a valid character set for a pluscode as I use them. Flag this entry.
                return false;

            double sanity = 0;
            if (double.TryParse(toCheck, out sanity)) //If the string is a number, allow it. Tt is possibly a valid plusCode in the southwest corner of a pluscode cell in the western hemisphere, but that gets less likely with additional length.
                return false;

            return true; //This string is interpretable as a plus code.
        }
    }
}
