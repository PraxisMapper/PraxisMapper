using Google.OpenLocationCode;
using System;
using System.Collections.Generic;
using System.Linq;
using static CoreComponents.ConstantValues;
using static CoreComponents.DbTables;
using static CoreComponents.Place;
using static CoreComponents.StandaloneDbTables;

namespace CoreComponents
{
    //this is data on an Area (PlusCode cell), so AreaTypeInfo is the correct name. Places are StoredOsmElement entries.
    public static class AreaTypeInfo 
    {
        //The new version, which returns a sorted list of places, smallest to largest, for when a single space contains multiple entries (default ScavengerHunt logic)
        public static List<StoredOsmElement> SortGameElements(List<StoredOsmElement> entries, bool allowPoints = true)
        {
            if (!allowPoints)
                entries = entries.Where(e => e.elementGeometry.GeometryType != "Point").ToList();            

            entries = entries.OrderBy(e => e.elementGeometry.Length).ToList(); //I want lines to show up before areas in most cases, so this should do that.
            return entries;
        }

        public static List<TerrainData> DetermineAreaPlace(List<StoredOsmElement> entriesHere)
        {
            //Which Place in this given Area is the one that should be displayed on the game/map as the name? picks the smallest one.
            var entries = SortGameElements(entriesHere);
            var results = new List<TerrainData>();
            foreach (var e in entries)
                results.Add(new TerrainData() { Name = e.name, areaType = e.GameElementName, StoredOsmElementId = e.id}); // entry.name + "|" + entry.GameElementName + "|" + entry.sourceItemID + "|" + entry.sourceItemType;
            return results;
        }

        public static Dictionary<string, List<TerrainData>> SearchArea(ref GeoArea area, ref List<StoredOsmElement> elements, bool entireCode = false)
        {
            Dictionary<string, List<TerrainData>> results = new Dictionary<string, List<TerrainData>>();
            if (elements.Count() == 0)
                return null;
            
            var xCells = area.LongitudeWidth / resolutionCell10;
            var yCells = area.LatitudeHeight / resolutionCell10;

            for (double xx = 0; xx < xCells; xx += 1)
            {
                for (double yy = 0; yy < yCells; yy += 1)
                {
                    double x = area.Min.Longitude + (resolutionCell10 * xx);
                    double y = area.Min.Latitude + (resolutionCell10 * yy);

                    var placeFound = FindPlacesInCell10(x, y, ref elements, entireCode);
                    if (placeFound != null)
                        results.Add(placeFound.Item1, placeFound.Item2);
                }
            }

            return results;
        }

        public static Tuple<string, List<TerrainData>> FindPlacesInCell10(double x, double y, ref List<StoredOsmElement> places, bool entireCode = false)
        {
            var box = new GeoArea(new GeoPoint(y, x), new GeoPoint(y + resolutionCell10, x + resolutionCell10));
            var entriesHere = GetPlaces(box, places).ToList();

            if (entriesHere.Count() == 0)
                return null;

            var area = DetermineAreaPlace(entriesHere);
            if (area != null && area.Count() > 0)
            {
                string olc;
                if (entireCode)
                    olc = new OpenLocationCode(y, x).CodeDigits;
                else
                    //TODO: decide on passing in a value for the split instead of a bool so this can be reused a little more
                    olc = new OpenLocationCode(y, x).CodeDigits.Substring(8, 2); //This takes lat, long, Coordinate takes X, Y. This line is correct.


                return new Tuple<string, List<TerrainData>>(olc, area);
            }
            return null;
        }
    }
}
