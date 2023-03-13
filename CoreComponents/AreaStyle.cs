using Google.OpenLocationCode;
using NetTopologySuite.Geometries;
using PraxisCore.Support;
using System;
using System.Collections.Generic;
using System.Linq;
using static PraxisCore.ConstantValues;
using static PraxisCore.Place;

namespace PraxisCore
{
    /// <summary>
    /// Functions that search or sort Places by their TagParser styles, displaying information by Area. Usually done for Cell10s.
    /// </summary>
    public static class AreaStyle
    {
        /// <summary>
        /// Get the StyleName (as defined by TagParser) for each Place in the list, along with name and client-facing ID.
        /// </summary>
        /// <param name="entriesHere">the list of elements to pull data from</param>
        /// <returns>A list of name, StyleName, and elementIds for a client</returns>
        public static List<AreaInfo> DetermineAreaInfos(List<DbTables.Place> entriesHere)
        {
            var results = new List<AreaInfo>(entriesHere.Count);
            foreach (var e in entriesHere)
            {
                results.Add(new AreaInfo(TagParser.GetName(e), e.StyleName, e.PrivacyId));
            }
            return results;
        }

        /// <summary>
        /// Returns the smallest (most-important) element in a list, to identify which element a client should use.
        /// </summary>
        /// <param name="entriesHere">the list of elements to pull data from</param>
        /// <returns>the name, areatype, and client facing ID of the OSM element to use</returns>
        public static AreaInfo DetermineAreaInfo(List<DbTables.Place> entriesHere)
        {
            //Which Place in this given Area is the one that should be displayed on the game/map as the name? picks the smallest one.
            //This one only returns the smallest entry, for games that only need to check the most interesting area in a cell.
            //loading data through GetPlaces() will sort them largest to smallest, so we dont sort again here.
            var entry = entriesHere.Last();
            return new AreaInfo(TagParser.GetName(entry), entry.StyleName, entry.PrivacyId);
        }

        /// <summary>
        /// Find which element in the list intersect with which PlusCodes inside the area. Returns one element (the smallest) per Cell10 in the area.
        /// </summary>
        /// <param name="area">GeoArea from a decoded PlusCode</param>
        /// <param name="elements">A list of OSM elements</param>
        /// <returns>returns a dictionary using PlusCode as the key and name/areatype/client facing Id of the smallest element intersecting that PlusCode</returns>
        public static List<AreaDetail> GetAreaDetails(ref GeoArea area, ref List<DbTables.Place> elements) {
            List<AreaDetail> results = new List<AreaDetail>(400); //starting capacity for a full Cell8

            //Singular function, returns 1 item entry per cell10.
            if (elements.Count == 0)
                return results;

            double x = area.Min.Longitude;
            double y = area.Min.Latitude;

            GeoArea searchArea;
            List<DbTables.Place> searchPlaces;
            AreaDetail? placeFound;
            Polygon searchAreaPoly = null;

            while (x < area.Max.Longitude) {
                searchArea = new GeoArea(area.Min.Latitude, x - resolutionCell10, area.Max.Latitude, x + resolutionCell10);
                searchAreaPoly = searchArea.ToPolygon();
                searchPlaces = elements.Where(e => e.ElementGeometry.Intersects(searchAreaPoly)).ToList();
                while (y < area.Max.Latitude) {
                    placeFound = GetAreaDetailForCell10(x, y, ref searchPlaces);
                    if (placeFound.HasValue)
                        results.Add(placeFound.Value);

                    y = Math.Round(y + resolutionCell10, 6); //Round ensures we get to the next pluscode in the event of floating point errors.
                }
                x = Math.Round(x + resolutionCell10, 6);
                y = area.Min.Latitude;
            }

            return results;
        }

        /// <summary>
        /// Find which elements in the list intersect with which PlusCodes inside the area. Returns multiple elements per PlusCode
        /// </summary>
        /// <param name="area">GeoArea from a decoded PlusCode</param>
        /// <param name="elements">A list of OSM elements</param>
        /// <returns>returns a dictionary using PlusCode as the key and name/areatype/client facing Id of all element intersecting that PlusCode</returns>
        public static List<AreaDetailAll> GetAreaDetailsAll(ref GeoArea area, ref List<DbTables.Place> elements)
        {
            if (elements.Count == 0)
                return null;

            //Plural function, returns all entries for each cell10.
            List<AreaDetailAll> results = new List<AreaDetailAll>(400); //starting capacity for a full Cell8

            double x = area.Min.Longitude;
            double y = area.Min.Latitude;

            GeoArea searchArea;
            Polygon searchAreaPoly = null;
            List<DbTables.Place> searchPlaces;
            AreaDetailAll? placesFound;
            
            while (x < area.Max.Longitude)
            {
                searchArea = new GeoArea(area.Min.Latitude, x - resolutionCell10, area.Max.Latitude, x + resolutionCell10);
                searchAreaPoly = searchArea.ToPolygon();
                searchPlaces = elements.Where(e => e.ElementGeometry.Intersects(searchAreaPoly)).ToList();
                while (y < area.Max.Latitude)
                {
                    placesFound = GetAreaDetailAllForCell10(x, y, ref searchPlaces);
                    if (placesFound.HasValue)
                        results.Add(placesFound.Value);

                    y = Math.Round(y + resolutionCell10, 6); //Round ensures we get to the next pluscode in the event of floating point errors.
                }
                x = Math.Round(x + resolutionCell10, 6);
                y = area.Min.Latitude;
            }

            return results;
        }

        /// <summary>
        /// Returns all the elements in a list that intersect with the 10-digit PlusCode at the given lat/lon coordinates.
        /// </summary>
        /// <param name="lon">longitude in degrees</param>
        /// <param name="lat">latitude in degrees</param>
        /// <param name="places">list of OSM elements</param>
        /// <returns>a tuple of the 10-digit plus code and a list of name/areatype/client facing ID for each element in that pluscode.</returns>
        public static AreaDetailAll? GetAreaDetailAllForCell10(double lon, double lat, ref List<DbTables.Place> places)
        {
            //Plural function, gets all areas in each cell10.
            var olc = new OpenLocationCode(lat, lon);
            var box = olc.Decode();
            var geoPoly = box.ToPolygon();
            var entriesHere = places.Where(p => p.ElementGeometry.Intersects(geoPoly)).ToList();

            if (entriesHere.Count == 0)
                return null;

            var area = DetermineAreaInfos(entriesHere);
            return new AreaDetailAll(olc.CodeDigits, area);

        }

        /// <summary>
        /// Returns the smallest element in a list that intersect with the 10-digit PlusCode at the given lat/lon coordinates.
        /// </summary>
        /// <param name="lon">longitude in degrees</param>
        /// <param name="lat">latitude in degrees</param>
        /// <param name="places">list of OSM elements</param>
        /// <returns>a tuple of the 10-digit plus code and the name/areatype/client facing ID for the smallest element in that pluscode.</returns>
        public static AreaDetail? GetAreaDetailForCell10(double x, double y, ref List<DbTables.Place> places)
        {
            //singular function, only returns the smallest area in a cell.           
            var olc = new OpenLocationCode(y, x);
            var box = olc.Decode();
            var geoPoly = box.ToPolygon();
            var entriesHere = places.Where(p => p.ElementGeometry.Intersects(geoPoly)).ToList();

            if (entriesHere.Count == 0)
                return null;

            var area = DetermineAreaInfo(entriesHere);
            return new AreaDetail(olc.CodeDigits, area);
        }

        /// <summary>
        /// Returns the smallest area in a Cell10. A quick helper function for when you want to know what style applies to a Cell10.
        /// </summary>
        /// <param name="plusCode"></param>
        /// <returns></returns>
        public static DbTables.Place GetSinglePlaceFromArea(string plusCode)
        {
            //for individual Cell10 or Cell11 checks. Existing terrain calls only do Cell10s in a Cell8 or larger area.
            var place = GetPlaces(plusCode.ToGeoArea()).LastOrDefault();
            return place;
        }
    }
}