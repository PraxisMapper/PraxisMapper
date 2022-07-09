using Google.OpenLocationCode;
using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Prepared;
using System.Linq;

namespace PraxisCore
{
    /// <summary>
    /// Confirm if data is inside server bounds, or if a PlusCode is parseable by PraxisMapper
    /// </summary>
    public static class DataCheck
    {
        public static bool DisableBoundsCheck = true;
        public static IPreparedGeometry bounds = null;

        /// <summary>
        /// Determine if a GeoArea (presumably from a PlusCode) intersects with the data contained in the server.
        /// </summary>
        /// <param name="bounds">PreparedGeometry representing the server's usable boundaries</param>
        /// <param name="place">GeoArea to check against the server's bounds</param>
        /// <returns>true if the 2 parameters intersect, or false if they do not.</returns>
        public static bool IsInBounds(IPreparedGeometry bounds, GeoArea place)
        {
            if (DisableBoundsCheck || bounds.Intersects(Converters.GeoAreaToPolygon(place)))
                return true;

            return false;
        }

        public static bool IsInBounds(GeoArea place)
        {
            if (DisableBoundsCheck || bounds.Intersects(Converters.GeoAreaToPolygon(place)))
                return true;

            return false;
        }

        /// <summary>
        /// Determine if a Polygon (presumably from a map element) intersects with the data contained in the server.
        /// </summary>
        /// <param name="bounds">PreparedGeometry representing the server's usable boundaries</param>
        /// <param name="place">Polygon to check against the server's bounds</param>
        /// <returns>true if the 2 parameters intersect, or false if they do not.</returns>
        public static bool IsInBounds(IPreparedGeometry bounds, Polygon place)
        {
            if (DisableBoundsCheck || bounds.Intersects(place))
                return true;

            return false;
        }

        public static bool IsInBounds(Polygon place)
        {
            if (DisableBoundsCheck || bounds.Intersects(place))
                return true;

            return false;
        }

        /// <summary>
        /// Determine if a Polygon (presumably from a map element) intersects with the data contained in the server.
        /// </summary>
        /// <param name="bounds">PreparedGeometry representing the server's usable boundaries</param>
        /// <param name="plusCode">PlusCode string to check against the server bounds.</param>
        /// <returns>true if the 2 parameters intersect, or false if they do not.</returns>
        public static bool IsInBounds(IPreparedGeometry bounds, string plusCode)
        {
            return IsInBounds(bounds, OpenLocationCode.DecodeValid(plusCode));
        }

        public static bool IsInBounds(string plusCode)
        {
            return IsInBounds(bounds, OpenLocationCode.DecodeValid(plusCode));
        }



        /// <summary>
        /// Determine if a Lat/Lon coordinate pair intersects with the data contained in the server.
        /// </summary>
        /// <param name="bounds">PreparedGeometry representing the server's usable boundaries</param>
        /// <param name="lat">latitude in degrees</param>
        /// <param name="lon">longitude in degrees</param>
        /// <returns>true if the 2 parameters intersect, or false if they do not.</returns>
        public static bool IsInBounds(IPreparedGeometry bounds, double lat, double lon)
        {
            Point p = new Point(lon, lat);
            if (DisableBoundsCheck || bounds.Intersects(p))
                return true;

            return false;
        }

        /// <summary>
        /// Determine if a string is a PlusCode as used by PraxisMapper. This app uses a non-standard form, where
        /// the + symbol is normally omitted (to allow for processing in URLs) and abbreviated codes omit padding 0s
        /// (EX: PM uses 9C5M instead of 9C5M0000+00 to represent Dublin, Ireland), which requires some specialized checking.
        /// PlusCodes that area also valid integer values will not be flagged as a PlusCode here, as it is much more likely that the number 6792 is being used as some kind of score rather than specifically locating a city in Equador.
        /// </summary>
        /// <param name="toCheck">The string to attempt to evaluate as a PlusCode</param>
        /// <returns>true if the string is likely to be a PlusCode, false if it is an invalid PlusCode OR it is a valid PlusCode that also parses to a long.</returns>
        public static bool IsPlusCode(string toCheck)
        {
            //Since PraxisMapper works with non-spec-standard Plus Codes (by using the first part of the code to shorten it, rather than the later part)
            //we have a slightly different set of checks for validity than the default library uses.
            toCheck = toCheck.Replace("+", "").ToUpper().Trim(); //Clean the string up for checks.
            if ((toCheck.Length >= 2 && toCheck.Length <= 12 & (toCheck.Length & 1) == 1)) //If toCheck is between 2 and 12 digits long, and an even number, it might be a plus code.
                return false;

            if (!toCheck.All(t => OpenLocationCode.CodeAlphabet.Contains(t))) //This is a valid length and a valid character set for a pluscode as I use them. Flag this entry.
                return false;

            long sanity = 0;
            if (long.TryParse(toCheck, out sanity)) //If the string is a number, allow it. Tt is possibly a valid plusCode in the southwest corner of a pluscode cell in the western hemisphere, but that gets less likely with additional length.
                return false;

            return true; //This string is interpretable as a plus code.
        }
    }
}
