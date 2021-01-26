using Google.OpenLocationCode;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;
using static CoreComponents.DbTables;
using static CoreComponents.ConstantValues;
using static CoreComponents.Place;

namespace CoreComponents
{
    //this is data on an Area (PlusCode cell), so AreaTypeInfo is the correct name. Places are MapData entries.
    public static class AreaTypeInfo
    {
        //TODO: replace Cell10/Cell11 functions with the generic GetAreaType call directly.
        public static int GetAreaType(GeoArea cell, ref List<MapData> places, bool includePoints = true, double filterSize = 0)
        {
            if (places.Count() == 0) //One shortcut: if we have no places to check, don't bother with the rest of the logic.
                return 0;

            //We can't shortcut this intersection check past that. This is the spot where we determine what's in this Cell11, and can't assume the list contains an overlapping entry.
            var entriesHere = GetPlaces(cell, places).ToList(); //Excluding admin boundaries from this list.  

            if (entriesHere.Count() == 0)
                return 0;

            int area = DetermineAreaType(entriesHere, includePoints, filterSize);
            return area;
        }

        public static int GetAreaType(GeoArea cell, ref List<PreparedMapData> places, bool includePoints = true, double filterSize = 0)
        {
            if (places.Count() == 0) //One shortcut: if we have no places to check, don't bother with the rest of the logic.
                return 0;

            //We can't shortcut this intersection check past that. This is the spot where we determine what's in this Cell11, and can't assume the list contains an overlapping entry.
            var entriesHere = GetPreparedPlaces(cell, places, false).ToList(); //Excluding admin boundaries from this list.  

            if (entriesHere.Count() == 0)
                return 0;

            int area = DetermineAreaType(entriesHere, includePoints, filterSize);
            return area;
        }

        public static int GetAreaTypeForCell10(double x, double y, ref List<MapData> places)
        {
            var box = new GeoArea(new GeoPoint(y, x), new GeoPoint(y + resolutionCell10, x + resolutionCell10));
            return GetAreaType(box, ref places, true);
        }

        public static int GetAreaTypeForCell11(double x, double y, ref List<MapData> places)
        {
            
            //We can't shortcut this intersection check past that. This is the spot where we determine what's in this Cell11, and can't assume the list contains an overlapping entry.
            var box = new GeoArea(new GeoPoint(y, x), new GeoPoint(y + resolutionCell11Lat, x + resolutionCell11Lon));
            return GetAreaType(box, ref places, true);
        }

        public static int GetAreaTypeForCell11(double x, double y, ref List<PreparedMapData> places)
        {
            var box = new GeoArea(new GeoPoint(y, x), new GeoPoint(y + resolutionCell11Lat, x + resolutionCell11Lon));
            return GetAreaType(box, ref places, true);
        }

        public static int DetermineAreaType(List<MapData> entriesHere, bool allowPoints, double filterSize = 0)
        {
            var entry = PickSmallestEntry(entriesHere, allowPoints, filterSize);
            return entry.AreaTypeId;
        }

        public static int DetermineAreaType(List<PreparedMapData> entriesHere, bool allowPoints, double filterSize = 0)
        {
            var entry = PickSmallestEntry(entriesHere, allowPoints, filterSize);
            if (entry != null)
                return entry.AreaTypeId;

            return 0;
        }

        public static int DetermineBiggestAreaType(List<MapData> entries)
        {
            //This is an alternate sorting rule for maptiles with pixels bigger than a Cell10 in area.
            //TODO: finish this, implement it correctly in appropriate spots.
            //TODO: may need more complex rules on roads/buildings so that they can occasionally show up without dominating the map.
            Dictionary<int, double> results = new Dictionary<int, double>();

            foreach (var areatype in entries.Select(e => e.AreaTypeId).Distinct())
                results.Add(areatype, 0);

            foreach (var entry in entries)
                results[entry.AreaTypeId] += entry.place.Area;

            return results.OrderByDescending(r => r.Value).First().Key;
        }

        public static MapData PickSmallestEntry(List<MapData> entries, bool allowPoints = true, double filterSize = 0)
        {
            //Current sorting rules:
            //If points are not allowed, remove them from the list
            //if filtersize is not 0, remove all lines and areas with an area below filtersize. Overrides allowPoints, always acts as allowPoints = false
            //If there's only one place, take it without any additional queries. Otherwise:
            //if there's a Point in the mapdata list, take the first one (No additional sub-sorting applied yet)
            //else if there's a Line in the mapdata list, take the shortest one by length
            //else if there's polygonal areas here, take the smallest one by area 
            //(In general, the smaller areas should be overlaid on larger areas.)

            if (!allowPoints)
                entries = entries.Where(e => e.place.GeometryType != "Point").ToList();

            if (filterSize != 0) // remove areatypes where the total area is below this.
                entries = entries.Where(e => e.place.GeometryType == "Polygon" || e.place.GeometryType == "MultiPolygon")
                    .Where(e => e.place.Area >= filterSize)
                    .ToList();

            if (entries.Count() == 1) //simple optimization, but must be applied after parameter rules are applied.
                return entries.First();

            var place = entries.Where(e => e.place.GeometryType == "Point").FirstOrDefault();
            if (place == null)
                place = entries.Where(e => e.place.GeometryType == "LineString" || e.place.GeometryType == "MultiLineString").OrderBy(e => e.place.Length).FirstOrDefault();
            if (place == null)
                place = entries.Where(e => e.place.GeometryType == "Polygon" || e.place.GeometryType == "MultiPolygon").OrderBy(e => e.place.Area).FirstOrDefault();
            return place;
        }


        public static PreparedMapData PickSmallestEntry(List<PreparedMapData> entries, bool allowPoints = true, double filterSize = 0)
        {
            //Current sorting rules:
            //If points are not allowed, remove them from the list
            //If there's only one place, take it without any additional queries. Otherwise:
            //if there's a Point in the mapdata list, take the first one (No additional sub-sorting applied yet)
            //else if there's a Line in the mapdata list, take the shortest one by length
            //else if there's polygonal areas here, take the smallest one by area 
            //(In general, the smaller areas should be overlaid on larger areas.)
            if (!allowPoints)
                entries = entries.Where(e => e.place.Geometry.GeometryType != "Point").ToList();

            if (filterSize != 0)
                entries = entries.Where(e => e.place.Geometry.GeometryType == "Polygon" || e.place.Geometry.GeometryType == "MultiPolygon")
                    .Where(e => e.place.Geometry.Area >= filterSize)
                    .ToList();

            if (entries.Count() == 1) //simple optimization
                return entries.First();

            var place = entries.Where(e => e.place.Geometry.GeometryType == "Point").FirstOrDefault();
            if (place == null)
                place = entries.Where(e => e.place.Geometry.GeometryType == "LineString" || e.place.Geometry.GeometryType == "MultiLineString").OrderBy(e => e.place.Geometry.Length).FirstOrDefault();
            if (place == null)
                place = entries.Where(e => e.place.Geometry.GeometryType == "Polygon" || e.place.Geometry.GeometryType == "MultiPolygon").OrderBy(e => e.place.Geometry.Area).FirstOrDefault();
            return place;
        }



        public static string DetermineAreaPlace(List<MapData> entriesHere)
        {
            //Which Place in this given Area is the one that should be displayed on the game/map as the name? picks the smallest one.
            var entry = PickSmallestEntry(entriesHere);
            return entry.name + "|" + entry.AreaTypeId + "|" + entry.MapDataId;
        }

        //ONly directly used in this class, above.
        public static int DetermineAreaFaction(List<MapData> entriesHere, Tuple<long, int> shortcut = null)
        {
            var entry = PickSmallestEntry(entriesHere).MapDataId;
            if (shortcut != null && entry == shortcut.Item1) //we are being told the results for a specific MapData entry from higher up in the chain.
                return shortcut.Item2;

            var db = new PraxisContext();
            var faction = db.AreaControlTeams.Where(a => a.MapDataId == entry).FirstOrDefault();
            if (faction == null)
                return 0;

            return faction.FactionId;
        }

        public static StringBuilder SearchArea(ref GeoArea area, ref List<MapData> mapData, bool entireCode = false)
        {
            StringBuilder sb = new StringBuilder();
            if (mapData.Count() == 0)
                return sb;

            var xCells = area.LongitudeWidth / resolutionCell10;
            var yCells = area.LatitudeHeight / resolutionCell10;

            for (double xx = 0; xx < xCells; xx += 1)
            {
                for (double yy = 0; yy < yCells; yy += 1)
                {
                    double x = area.Min.Longitude + (resolutionCell10 * xx);
                    double y = area.Min.Latitude + (resolutionCell10 * yy);

                    var placesFound = FindPlacesInCell10(x, y, ref mapData, entireCode);
                    if (!string.IsNullOrWhiteSpace(placesFound))
                        sb.AppendLine(placesFound);
                }
            }
            return sb;
        }


        //The core data transfer function for the original mode planned.
        public static string FindPlacesInCell10(double x, double y, ref List<MapData> places, bool entireCode = false)
        {
            var box = new GeoArea(new GeoPoint(y, x), new GeoPoint(y + resolutionCell10, x + resolutionCell10));
            var entriesHere = GetPlaces(box, places).ToList(); //Excluding admin boundaries from this list.  

            if (entriesHere.Count() == 0)
                return "";

            string area = DetermineAreaPlace(entriesHere);
            if (area != "")
            {
                string olc;
                if (entireCode)
                    olc = new OpenLocationCode(y, x).CodeDigits;
                else
                    //TODO: decide on passing in a value for the split instead of a bool so this can be reused a little more
                    //olc = new OpenLocationCode(y, x).CodeDigits.Substring(6, 4); //This takes lat, long, Coordinate takes X, Y. This line is correct.
                    olc = new OpenLocationCode(y, x).CodeDigits.Substring(8, 2); //This takes lat, long, Coordinate takes X, Y. This line is correct.
                return olc + "|" + area;
            }
            return "";
        }

        //Used when drawing MP AreaControl map tiles.
        public static int GetFactionForCell11(double x, double y, ref List<MapData> places, Tuple<long, int> shortcut = null)
        {
            var box = new GeoArea(new GeoPoint(y, x), new GeoPoint(y + resolutionCell11Lat, x + resolutionCell11Lon));
            var entriesHere = GetPlaces(box, places).Where(p => p.AreaTypeId != 13).ToList(); //Excluding admin boundaries from this list.  

            if (entriesHere.Count() == 0)
                return 0;

            int factionID = DetermineAreaFaction(entriesHere, shortcut);
            return factionID;
        }
    }
}
