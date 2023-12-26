using Google.OpenLocationCode;
using NetTopologySuite.Geometries;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PraxisCore.GameTools
{
    public class GeometryBuilder
    {
        //GeometryBuilder is a GameTool for allowing players to create simple polygon geometry in-play.
        //This class handles tracking partial geometry progress and enforcing rules on the results
        //Each point is a Cell10, 

        public List<string> currentPoints { get; set; }

        public bool Add(Point p)
        {
            return Add(OpenLocationCode.Encode(p.Y, p.X).Replace("+", ""));
        }

        public bool Add(string plusCode)
        {
            if (currentPoints.Last() == plusCode) //Dont re-add the last point.
                return false;

            currentPoints.Add(plusCode.Replace("+", ""));
            if (currentPoints.Count <= 2)
                return true;

            if (Create() != null)
                return true;
            else
            {
                Remove(plusCode);
                return false;
            }
        }

        public bool Remove(Point p)
        {
            var plusCode = OpenLocationCode.Encode(p.Y, p.X).Replace("+", "");
            return currentPoints.Remove(plusCode);
        }

        public bool Remove(string plusCode)
        {
            return currentPoints.Remove(plusCode);
        }

        public Polygon Create()
        {
            var coords = currentPoints.Select(c => new Coordinate(c.ToGeoArea().CenterLongitude, c.ToGeoArea().CenterLatitude)).ToList();
            if (coords[0] != coords.Last())
                coords.Add(coords[0]); //force a closed polygon.
            var p = Singletons.geometryFactory.CreatePolygon(coords.ToArray());
            if (p.IsSimple)
                return p;
            return null;
        }

        public static Polygon Create(List<String> plusCodes)
        {
            var coords = plusCodes.Select(c => { var area = c.Replace("+", "").ToGeoArea(); return new Coordinate(area.CenterLongitude, area.CenterLatitude); }).ToList();
            if (coords[0] != coords.Last())
                coords.Add(coords[0]); //force a closed polygon.
            var p = Singletons.geometryFactory.CreatePolygon(coords.ToArray());
            if (p.IsSimple)
                return p;
            return null;
        }

    }
}
