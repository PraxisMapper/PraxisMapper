using Google.OpenLocationCode;
using Microsoft.EntityFrameworkCore;
using PraxisCore.Support;
using System;
using System.Collections.Generic;
using System.Linq;
using static PraxisCore.ConstantValues;
using static PraxisCore.Place;
using static PraxisCore.StandaloneDbTables;

namespace PraxisCore
{
    /// <summary>
    /// Functions that search or sort the gameplay on the TagParser entries of Places, most often by Area.
    /// </summary>
    public static class TerrainInfo
    {
        //The new version, which returns a sorted list of places, smallest to largest, for when a single space contains multiple entries (default ScavengerHunt logic)
        /// <summary>
        /// Sorts the given list by AreaSize. Larger elements should be drawn first, so smaller areas will appear over them on maptiles.
        /// </summary>
        /// <param name="entries">The list of entries to sort by</param>
        /// <param name="allowPoints">If true, include points in the return list as size 0. If false, filters those out from the returned list.</param>
        /// <returns>The sorted list of entries</returns>
        public static List<DbTables.Place> SortGameElements(List<DbTables.Place> entries, bool allowPoints = true)
        {
            //I sort entries on loading from the Database. It's possible this step is unnecessary if everything else runs in order, just using last instead of first.
            if (!allowPoints)
                entries = entries.Where(e => e.SourceItemType != 1).ToList(); // .ElementGeometry.GeometryType != "Point")

            entries = entries.OrderBy(e => e.DrawSizeHint).ToList(); //I want lines to show up before areas in most cases, so this should do that.
            return entries;
        }

        /// <summary>
        /// Get the areatype (as defined by TagParser) for each OSM element in the list, along with name and client-facing ID.
        /// </summary>
        /// <param name="entriesHere">the list of elements to pull data from</param>
        /// <returns>A list of name, areatype, and elementIds for a client</returns>
        public static List<TerrainData> DetermineAreaTerrains(List<DbTables.Place> entriesHere)
        {
            //Which Place in this given Area is the one that should be displayed on the game/map as the name? picks the smallest one.
            //This one return all entries, for a game mode that might need all of them.
            var results = new List<TerrainData>(entriesHere.Count);
            foreach (var e in entriesHere)
            {
                results.Add(new TerrainData(TagParser.GetPlaceName(e.Tags), e.GameElementName, e.PrivacyId));
            }
            return results;
        }

        /// <summary>
        /// Returns the smallest (most-important) element in a list, to identify which element a client should use.
        /// </summary>
        /// <param name="entriesHere">the list of elements to pull data from</param>
        /// <returns>the name, areatype, and client facing ID of the OSM element to use</returns>
        public static TerrainData DetermineAreaTerrain(List<DbTables.Place> entriesHere)
        {
            //Which Place in this given Area is the one that should be displayed on the game/map as the name? picks the smallest one.
            //This one only returns the smallest entry, for games that only need to check the most interesting area in a cell.
            var entry = entriesHere.Last();
            return new TerrainData(TagParser.GetPlaceName(entry.Tags), entry.GameElementName, entry.PrivacyId);
        }

        /// <summary>
        /// Find which element in the list intersect with which PlusCodes inside the area. Returns one element per PlusCode
        /// </summary>
        /// <param name="area">GeoArea from a decoded PlusCode</param>
        /// <param name="elements">A list of OSM elements</param>
        /// <returns>returns a dictionary using PlusCode as the key and name/areatype/client facing Id of the smallest element intersecting that PlusCode</returns>
        public static List<FindPlaceResult> SearchArea(ref GeoArea area, ref List<DbTables.Place> elements)
        {
            List<FindPlaceResult> results = new List<FindPlaceResult>(400); //starting capacity for a full Cell8

            //Singular function, returns 1 item entry per cell10.
            if (elements.Count == 0)
                return results;

            //var xCells = area.LongitudeWidth / resolutionCell10;
            //var yCells = area.LatitudeHeight / resolutionCell10;
            double x = area.Min.Longitude;
            double y = area.Min.Latitude;

            GeoArea searchArea;
            List<DbTables.Place> searchPlaces;
            FindPlaceResult? placeFound;

            //for (double xx = 0; xx < xCells; xx++) 
            while (x < area.Max.Longitude)
            {
                searchArea = new GeoArea(area.Min.Latitude, x - resolutionCell10, area.Max.Latitude, x + resolutionCell10);
                searchPlaces = GetPlaces(searchArea, elements, skipTags: true);
                //for (double yy = 0; yy < yCells; yy++) 
                while (y < area.Max.Latitude)
                {
                    placeFound = FindPlaceInCell10(x, y, ref searchPlaces);
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
        public static List<FindPlacesResult> SearchAreaFull(ref GeoArea area, ref List<DbTables.Place> elements)
        {
            if (elements.Count == 0)
                return null;

            //Plural function, returns all entries for each cell10.
            List<FindPlacesResult> results = new List<FindPlacesResult>(400); //starting capacity for a full Cell8

            //var xCells = area.LongitudeWidth / resolutionCell10;
            //var yCells = area.LatitudeHeight / resolutionCell10;
            double x = area.Min.Longitude;
            double y = area.Min.Latitude;

            GeoArea searchArea;
            List<DbTables.Place> searchPlaces;
            FindPlacesResult? placeFound;

            //for (double xx = 0; xx < xCells; xx++)
            while (x < area.Max.Longitude)
            {
                searchArea = new GeoArea(area.Min.Latitude, x - resolutionCell10, area.Max.Latitude, x + resolutionCell10);
                searchPlaces = GetPlaces(searchArea, elements, skipTags: true);
                //for (double yy = 0; yy < yCells; yy++)
                while (y < area.Max.Latitude)
                {
                    placeFound = FindPlacesInCell10(x, y, ref searchPlaces);
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
        /// Returns all the elements in a list that intersect with the 10-digit PlusCode at the given lat/lon coordinates.
        /// </summary>
        /// <param name="lon">longitude in degrees</param>
        /// <param name="lat">latitude in degrees</param>
        /// <param name="places">list of OSM elements</param>
        /// <returns>a tuple of the 10-digit plus code and a list of name/areatype/client facing ID for each element in that pluscode.</returns>
        public static FindPlacesResult? FindPlacesInCell10(double lon, double lat, ref List<DbTables.Place> places)
        {
            //Plural function, gets all areas in each cell10.
            var box = new GeoArea(new GeoPoint(lat, lon), new GeoPoint(lat + resolutionCell10, lon + resolutionCell10));
            var entriesHere = GetPlaces(box, places, skipTags: true);

            if (entriesHere.Count == 0)
                return null;

            var area = DetermineAreaTerrains(entriesHere);
            string olc = new OpenLocationCode(lat, lon).CodeDigits;
            return new FindPlacesResult(olc, area);

        }

        /// <summary>
        /// Returns the smallest element in a list that intersect with the 10-digit PlusCode at the given lat/lon coordinates.
        /// </summary>
        /// <param name="lon">longitude in degrees</param>
        /// <param name="lat">latitude in degrees</param>
        /// <param name="places">list of OSM elements</param>
        /// <returns>a tuple of the 10-digit plus code and the name/areatype/client facing ID for the smallest element in that pluscode.</returns>
        public static FindPlaceResult? FindPlaceInCell10(double x, double y, ref List<DbTables.Place> places)
        {
            //singular function, only returns the smallest area in a cell.
            var olc = new OpenLocationCode(y, x);
            var box = olc.Decode();
            var entriesHere = GetPlaces(box, places, skipTags: true);

            if (entriesHere.Count == 0)
                return null;

            var area = DetermineAreaTerrain(entriesHere);
            return new FindPlaceResult(olc.CodeDigits, area);
        }

        public static FindPlaceResult? FindPlaceInCell10(string plusCode, ref List<DbTables.Place> places)
        {
            //singular function, only returns the smallest area in a cell.
            var box = plusCode.ToGeoArea();
            var entriesHere = GetPlaces(box, places, skipTags: true);

            if (entriesHere.Count == 0)
                return null;

            var area = DetermineAreaTerrain(entriesHere);
            return new FindPlaceResult(plusCode, area);
        }

        public static DbTables.Place GetSinglePlaceFromArea(string plusCode)
        {
            //for individual Cell10 or Cell11 checks. Existing terrain calls only do Cell10s in a Cell8 or larger area.

            //Self contained set of calls here
            var db = new PraxisContext();
            var poly = plusCode.ToPolygon();
            var place = db.Places.Include(s => s.Tags).Where(md => poly.Intersects(md.ElementGeometry)).OrderByDescending(w => w.ElementGeometry.Area).ThenByDescending(w => w.ElementGeometry.Length).Last();

            return place;
        }

        public static DbTables.Place GetSingleGameplayPlaceFromArea(string plusCode, string styleSet = "mapTiles")
        {
            //for individual Cell10 or Cell11 checks. Existing terrain calls only do Cell10s in a Cell8 or larger area.
            //Self contained set of calls here
            var db = new PraxisContext();
            var poly = plusCode.ToPolygon();
            var places = db.Places.Include(s => s.Tags).Where(md => poly.Intersects(md.ElementGeometry)).ToList();
            TagParser.ApplyTags(places, styleSet);
            var place = places.Where(p => p.IsGameElement).OrderByDescending(w => w.ElementGeometry.Area).ThenByDescending(w => w.ElementGeometry.Length).LastOrDefault();

            return place;
        }
    }
}