using Google.OpenLocationCode;
using System;
using System.Collections.Generic;
using System.Linq;
using static PraxisCore.ConstantValues;
using static PraxisCore.DbTables;
using static PraxisCore.Place;
using static PraxisCore.StandaloneDbTables;

namespace PraxisCore
{
    //this is data on an Area (PlusCode cell), so AreaTypeInfo is the correct name. Places are StoredOsmElement entries.
    public static class AreaTypeInfo 
    {
        //The new version, which returns a sorted list of places, smallest to largest, for when a single space contains multiple entries (default ScavengerHunt logic)
        public static List<StoredOsmElement> SortGameElements(List<StoredOsmElement> entries, bool allowPoints = true)
        {
            //I sort entries on loading from the Database. It's possible this step is unnecessary if everything else runs in order, just using last instead of first.
            if (!allowPoints)
                entries = entries.Where(e => e.elementGeometry.GeometryType != "Point").ToList();            

            entries = entries.OrderBy(e => e.AreaSize).ToList(); //I want lines to show up before areas in most cases, so this should do that.
            return entries;
        }

        public static List<TerrainData> DetermineAreaPlaces(List<StoredOsmElement> entriesHere)
        {
            //Which Place in this given Area is the one that should be displayed on the game/map as the name? picks the smallest one.
            //This one return all entries, for a game mode that might need all of them.
            //var entries = SortGameElements(entriesHere); //If I want all of them, I don't care as much about sorting.
            var results = new List<TerrainData>();
            foreach (var e in entriesHere)
                results.Add(new TerrainData() { Name = e.name, areaType = e.GameElementName, StoredOsmElementId = e.privacyId }); //e.id
            return results;
        }

        public static TerrainData DetermineAreaPlace(List<StoredOsmElement> entriesHere)
        {
            //Which Place in this given Area is the one that should be displayed on the game/map as the name? picks the smallest one.
            //This one only returns the smallest entry, for games that only need to check the most interesting area in a cell.
            //var entry = SortGameElements(entriesHere).First(); //Entries should already be sorted biggest to smallest, so just get the last one.
            var entry = entriesHere.Last();
            return new TerrainData() { Name = entry.name, areaType = entry.GameElementName, StoredOsmElementId = entry.privacyId };
        }

        public static Dictionary<string, TerrainData> SearchArea(ref GeoArea area, ref List<StoredOsmElement> elements)
        {
            //Singular function, returns 1 item entry per cell10.
            if (elements.Count() == 0)
                return null;
            
            Dictionary<string, TerrainData> results = new Dictionary<string, TerrainData>(400); //starting capacity for a full Cell8
            
            var xCells = area.LongitudeWidth / resolutionCell10;
            var yCells = area.LatitudeHeight / resolutionCell10;
            double x = area.Min.Longitude;
            double y = area.Min.Latitude;

            for (double xx = 0; xx < xCells; xx += 1)
            {
                for (double yy = 0; yy < yCells; yy += 1)
                {
                    var placeFound = FindPlaceInCell10(x, y, ref elements);
                    if (placeFound != null)
                        results.Add(placeFound.Item1, placeFound.Item2);

                    y = Math.Round(y + resolutionCell10, 6); //Round ensures we get to the next pluscode in the event of floating point errors.
                }
                x = Math.Round(x + resolutionCell10, 6);
                y = area.Min.Latitude;
            }

            return results;
        }

        public static Dictionary<string, List<TerrainData>> SearchAreaFull(ref GeoArea area, ref List<StoredOsmElement> elements)
        {
            //Plural function, returns all entries for each cell10.
            Dictionary<string, List<TerrainData>> results = new Dictionary<string, List<TerrainData>>(400); //starting capacity for a full Cell8
            if (elements.Count() == 0)
                return null;

            var xCells = area.LongitudeWidth / resolutionCell10;
            var yCells = area.LatitudeHeight / resolutionCell10;
            double x = area.Min.Longitude;
            double y = area.Min.Latitude;

            for (double xx = 0; xx < xCells; xx += 1)
            {
                for (double yy = 0; yy < yCells; yy += 1)
                {
                    var placeFound = FindPlacesInCell10(x, y, ref elements);
                    if (placeFound != null)
                        results.Add(placeFound.Item1, placeFound.Item2);

                    y = Math.Round(y + resolutionCell10, 6); //Round ensures we get to the next pluscode in the event of floating point errors.
                }
                x = Math.Round(x + resolutionCell10, 6);
                y = area.Min.Latitude;
            }

            return results;
        }

        public static Tuple<string, List<TerrainData>> FindPlacesInCell10(double x, double y, ref List<StoredOsmElement> places)
        {
            //Plural function, gets all areas in each cell10.
            var box = new GeoArea(new GeoPoint(y, x), new GeoPoint(y + resolutionCell10, x + resolutionCell10));
            var entriesHere = GetPlaces(box, places, skipTags:true).ToList();

            if (entriesHere.Count() == 0)
                return null;

            var area = DetermineAreaPlaces(entriesHere);
            if (area != null && area.Count() > 0)
            {
                string olc = new OpenLocationCode(y, x).CodeDigits;
                return new Tuple<string, List<TerrainData>>(olc, area);
            }
            return null;
        }

        public static Tuple<string, TerrainData> FindPlaceInCell10(double x, double y, ref List<StoredOsmElement> places)
        {
            //singular function, only returns the smallest area in a cell.
            var olc = new OpenLocationCode(y, x);
            var box = olc.Decode();
            var entriesHere = GetPlaces(box, places, skipTags: true).ToList();

            if (entriesHere.Count() == 0)
                return null;

            var area = DetermineAreaPlace(entriesHere);
            if (area != null)
            {
                return new Tuple<string, TerrainData>(olc.CodeDigits, area);
            }
            return null;
        }
    }
}
