using Google.OpenLocationCode;
using NetTopologySuite.Geometries;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PraxisCore.GameTools
{
    public static class PointPicker
    {
        //PlacePicker is for grabbing random points inside of an area. It is set up to bias that some points will be easily available
        //when possible by selecting  points near teriarty roads or walking trails.

        //Given a PlusCode string, get pointCount Cell10s within that area. Bias towards walkable points if possible.
        public static List<string> GetPoints(string area, int pointCount, bool checkWalkable = true, int cellSize = 10)
        {
            if (area.Length >= 10 )
                return new List<string>() { area }; //Sanity check on this so we don't infinite loop

            List<string> results = new List<string>();
            GeoArea mainArea = area.ToGeoArea();
            if ((mainArea.ToPolygon().Area / ConstantValues.squareCell10Area) > pointCount)
                return new List<string>() { area }; //they asked for more points than this area can handle, just give them back their code.

            var db = new PraxisContext();
            if (checkWalkable)
            {
                double walkablePercent = .35; //TODO config value.
                int biasedPicks = (int)(pointCount * walkablePercent);
                var places = Place.GetPlaces(mainArea, styleSet: "mapTiles", skipType: "adminBounds", skipTags: true);
                var walkables = places.Where(p => p.StyleName == "trail" || p.StyleName == "tertiary").ToList();

                while (results.Count < biasedPicks)
                {
                    //pick a polygon, pick a random cell10 that poly covers.
                    int biasfailCount = 0;
                    var item = walkables.PickOneRandom();
                    string nextPoint = "";
                    switch(item.ElementGeometry.GeometryType)
                    {
                        case "Point":
                            nextPoint = OpenLocationCode.Encode(item.ElementGeometry.Centroid.Y, item.ElementGeometry.Centroid.X, cellSize);
                            break;
                        case "Polygon":
                            nextPoint = Place.RandomPoint(item.ElementGeometry as Polygon);
                            break;
                        //TODO: multipolygon, linestring.
                    }
                    if (!results.Contains(nextPoint))
                        results.Add(nextPoint);
                    else
                    {
                        biasfailCount++;
                        if (biasfailCount > 12)
                            break;
                    }
                }
            }

            int failCount = 0;
            while (results.Count < pointCount)
            {
                var nextPoint = Place.RandomPoint(mainArea);
                if (!results.Contains(nextPoint))
                    results.Add(nextPoint);
                else
                {
                    failCount++;
                    if (failCount > 12)
                        break;
                }
            }

            return results;
        }
    }
}
