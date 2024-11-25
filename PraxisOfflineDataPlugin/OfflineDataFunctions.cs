using Google.OpenLocationCode;
using NetTopologySuite.Geometries;
using PraxisCore;
using PraxisMapper.Classes;

namespace PraxisOfflineDataPlugin
{
    public static class OfflineDataFunctions
    {
        public static List<string> GetCoordEntries(DbTables.Place place, GeoPoint min)
        {
            List<string> points = new List<string>();

            if (place.ElementGeometry.GeometryType == "MultiPolygon")
            {
                foreach (var poly in ((MultiPolygon)place.ElementGeometry).Geometries) //This should be the same as the Polygon code below.
                {
                    points.AddRange(GetPolygonPoints(poly as Polygon, min));
                }
            }
            else if (place.ElementGeometry.GeometryType == "Polygon")
            {
                points.AddRange(GetPolygonPoints(place.ElementGeometry as Polygon, min));
            }
            else
                points.Add(string.Join("|", place.ElementGeometry.Coordinates.Select(c => (int)((c.X - min.Longitude) / ConstantValues.resolutionCell11Lon) + "," + ((int)((c.Y - min.Latitude) / ConstantValues.resolutionCell11Lat)))));

            if (points.Count == 0)
            {
                System.Diagnostics.Debugger.Break();
            }

            return points;
        }

        public static List<string> GetPolygonPoints(Polygon p, GeoPoint min)
        {
            List<string> results = new List<string>();
            if (p.Holes.Length == 0)
                results.Add(string.Join("|", p.Coordinates.Select(c => (int)((c.X - min.Longitude) / ConstantValues.resolutionCell11Lon) + "," + ((int)((c.Y - min.Latitude) / ConstantValues.resolutionCell11Lat)))));
            else
            {
                //Split this polygon into smaller pieces, split on the center of each hole present longitudinally
                //West to east direction chosen arbitrarily.
                var westEdge = p.Coordinates.Min(c => c.X);
                var northEdge = p.Coordinates.Max(c => c.Y);
                var southEdge = p.Coordinates.Min(c => c.Y);

                List<double> splitPoints = new List<double>();
                foreach (var hole in p.Holes.OrderBy(h => h.Centroid.X))
                    splitPoints.Add(hole.Centroid.X);

                foreach (var point in splitPoints)
                {
                    var splitPoly = new GeoArea(southEdge, westEdge, northEdge, point).ToPolygon();
                    var subPoly = p.Intersection(splitPoly);

                    //Still need to check that we have reasonable geometry here.
                    if (subPoly.GeometryType == "Polygon")
                        results.AddRange(GetPolygonPoints(subPoly as Polygon, min));
                    else if (subPoly.GeometryType == "MultiPolygon")
                    {
                        foreach (var p2 in ((MultiPolygon)subPoly).Geometries)
                            results.AddRange(GetPolygonPoints(p2 as Polygon, min));
                    }
                    else
                        ErrorLogger.LogError(new Exception("Offline proccess error: Got geoType " + subPoly.GeometryType + ", which wasnt expected"));
                    westEdge = point;
                }
            }
            return results.Distinct().ToList(); //In the unlikely case splitting ends up processing the same part twice
        }
    }
}
