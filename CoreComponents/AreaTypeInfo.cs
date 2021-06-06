using Google.OpenLocationCode;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using static CoreComponents.ConstantValues;
using static CoreComponents.DbTables;
using static CoreComponents.Place;
using static CoreComponents.StandaloneDbTables;

namespace CoreComponents
{
    //this is data on an Area (PlusCode cell), so AreaTypeInfo is the correct name. Places are StoredOsmElement entries.
    public static class AreaTypeInfo 
    {
        //The original function, for when there's only 1 thing per tile that might matter.
        //public static StoredOsmElement PickSmallestEntry(List<StoredOsmElement> entries, bool allowPoints = true, double filterSize = 0)
        //{
        //    //Current sorting rules:
        //    //If points are not allowed, remove them from the list
        //    //if filtersize is not 0, remove all lines and areas with an area below filtersize. Overrides allowPoints, always acts as allowPoints = false
        //    //If there's only one place, take it without any additional queries. Otherwise:
        //    //if there's a Point in the storedElements list, take the first one (No additional sub-sorting applied yet)
        //    //else if there's a Line in the storedElements list, take the shortest one by length
        //    //else if there's polygonal areas here, take the smallest one by area 
        //    //(In general, the smaller areas should be overlaid on larger areas.)

        //    if (!allowPoints)
        //        entries = entries.Where(e => e.elementGeometry.GeometryType != "Point").ToList();

        //    if (filterSize != 0) // remove areatypes where the total area is below this.
        //        entries = entries.Where(e => e.elementGeometry.GeometryType == "Polygon" || e.elementGeometry.GeometryType == "MultiPolygon")
        //            //.Where(e => e.place.Area >= filterSize)
        //            .Where(e => e.AreaSize >= filterSize)
        //            .ToList();

        //    if (entries.Count() == 1) //simple optimization, but must be applied after parameter rules are applied.
        //        return entries.First();

        //    var place = entries.Where(e => e.elementGeometry.GeometryType == "Point").FirstOrDefault();
        //    if (place == null)
        //        place = entries.Where(e => e.elementGeometry.GeometryType == "LineString" || e.elementGeometry.GeometryType == "MultiLineString").OrderBy(e => e.AreaSize).FirstOrDefault();
        //    if (place == null)
        //        place = entries.Where(e => e.elementGeometry.GeometryType == "Polygon" || e.elementGeometry.GeometryType == "MultiPolygon").OrderBy(e => e.AreaSize).FirstOrDefault();
        //    return place;
        //}

        //The new version, which returns a sorted list of places, smallest to largest, for when a single space contains multiple entries (default ScavengerHunt logic)
        public static List<StoredOsmElement> SortGameElements(List<StoredOsmElement> entries, bool allowPoints = true)
        {
            if (!allowPoints)
                entries = entries.Where(e => e.elementGeometry.GeometryType != "Point").ToList();            

            entries = entries.OrderBy(e => e.AreaSize).ToList();
            return entries;
        }


        //public static string DetermineAreaPlace(List<StoredOsmElement> entriesHere)
        //{
        //    //Which Place in this given Area is the one that should be displayed on the game/map as the name? picks the smallest one.
        //    var entry = PickSmallestEntry(entriesHere);
        //    return entry.name + "|" + entry.GameElementName + "|" + entry.sourceItemID + "|" + entry.sourceItemType;
        //}

        public static List<TerrainData> DetermineAreaPlace2(List<StoredOsmElement> entriesHere)
        {
            //Which Place in this given Area is the one that should be displayed on the game/map as the name? picks the smallest one.
            var entries = SortGameElements(entriesHere);
            var results = new List<TerrainData>();
            foreach (var e in entries)
                results.Add(new TerrainData() { Name = e.name, areaType = e.GameElementName, OsmElementId = e.sourceItemID, OsmElementType = e.sourceItemType }); // entry.name + "|" + entry.GameElementName + "|" + entry.sourceItemID + "|" + entry.sourceItemType;
            return results;
        }

        public static Dictionary<string, List<TerrainData>> SearchArea2(ref GeoArea area, ref List<StoredOsmElement> elements, bool entireCode = false)
        {
            //This rewrite should be faster than the original on virtue of using the appropriate objects and not StringBuilder to pass around the data
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

                    var placeFound = FindPlacesInCell10V2(x, y, ref elements, entireCode);
                    if (placeFound != null)
                        results.Add(placeFound.Item1, placeFound.Item2);
                }
            }

            return results;

        }

        //public static StringBuilder SearchArea(ref GeoArea area, ref List<StoredOsmElement> elements, bool entireCode = false)
        //{
        //    StringBuilder sb = new StringBuilder();
        //    if (elements.Count() == 0)
        //        return sb;

        //    var xCells = area.LongitudeWidth / resolutionCell10;
        //    var yCells = area.LatitudeHeight / resolutionCell10;

        //    for (double xx = 0; xx < xCells; xx += 1)
        //    {
        //        for (double yy = 0; yy < yCells; yy += 1)
        //        {
        //            double x = area.Min.Longitude + (resolutionCell10 * xx);
        //            double y = area.Min.Latitude + (resolutionCell10 * yy);

        //            var placesFound = FindPlacesInCell10(x, y, ref elements, entireCode);
        //            if (!string.IsNullOrWhiteSpace(placesFound))
        //                sb.AppendLine(placesFound);
        //        }
        //    }
        //    return sb;
        //}


        //The core data transfer function for the original mode planned.
        //public static string FindPlacesInCell10(double x, double y, ref List<StoredOsmElement> places, bool entireCode = false)
        //{
        //    var box = new GeoArea(new GeoPoint(y, x), new GeoPoint(y + resolutionCell10, x + resolutionCell10));
        //    var entriesHere = GetPlaces(box, places).ToList(); 

        //    if (entriesHere.Count() == 0)
        //        return "";

        //    string area = DetermineAreaPlace(entriesHere);
        //    if (area != "")
        //    {
        //        string olc;
        //        if (entireCode)
        //            olc = new OpenLocationCode(y, x).CodeDigits;
        //        else
        //            //TODO: decide on passing in a value for the split instead of a bool so this can be reused a little more
        //            //olc = new OpenLocationCode(y, x).CodeDigits.Substring(6, 4); //This takes lat, long, Coordinate takes X, Y. This line is correct.
        //            olc = new OpenLocationCode(y, x).CodeDigits.Substring(8, 2); //This takes lat, long, Coordinate takes X, Y. This line is correct.
        //        return olc + "|" + area;
        //    }
        //    return "";
        //}

        public static Tuple<string, List<TerrainData>> FindPlacesInCell10V2(double x, double y, ref List<StoredOsmElement> places, bool entireCode = false)
        {
            var box = new GeoArea(new GeoPoint(y, x), new GeoPoint(y + resolutionCell10, x + resolutionCell10));
            var entriesHere = GetPlaces(box, places).ToList();

            if (entriesHere.Count() == 0)
                return null;

            var area = DetermineAreaPlace2(entriesHere);
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
