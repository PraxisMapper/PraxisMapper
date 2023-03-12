using Google.OpenLocationCode;
using NetTopologySuite.Geometries;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PraxisCore.GameTools {

    public class JsonPoint {
        public double X { get; set; }
        public double Y { get; set; }
    }

    public class RecentPath {
        public List<JsonPoint> points { get; set; } = new List<JsonPoint>(); //Coordinates and Points don't convert to JSON nicely.
        public DateTime lastUpdate { get; set; } // drop if too old.   
        public double speedLimitMetersPerSecond { get; set; } = 11; //11 m/s ~= 25MPH
        public double pathExpirationMinutes { get; set; } = 3;

        public void AddPoint(string plusCode11) {
            var olc = OpenLocationCode.DecodeValid(plusCode11);
            var point = olc.ToPoint();

            if (lastUpdate.AddMinutes(pathExpirationMinutes) < DateTime.UtcNow) {
                //path expired, reset it.
                points.Clear();
            }

            var lastPoint = points.LastOrDefault();
            double speed = 0;
            if (lastPoint != null) {
                speed = GeometrySupport.SpeedCheck(olc.Center, DateTime.UtcNow, new GeoPoint(lastPoint.Y, lastPoint.X), lastUpdate);
            }

            if (speed <= speedLimitMetersPerSecond) {
                JsonPoint thisPoint = new JsonPoint() { X = point.X, Y = point.Y };
                if (lastPoint != thisPoint) {
                    points.Add(thisPoint);
                    lastUpdate = DateTime.UtcNow;
                }
            }
        }

        public LineString ConvertToLine() {
            if (points.Count > 1) //can't draw a line of 1 point.
                return Singletons.geometryFactory.CreateLineString(points.Select(p => new Coordinate(p.X, p.Y)).ToArray());
            return null;
        }
    }
}
