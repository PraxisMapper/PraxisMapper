using Google.OpenLocationCode;
using NetTopologySuite.Geometries;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace PraxisCore.GameTools {

    public class JsonPoint {
        public double X { get; set; }
        public double Y { get; set; }
    }

    public sealed class RecentPath {
        public List<JsonPoint> points { get; set; } = new List<JsonPoint>(); //Coordinates and Points don't convert to JSON nicely.
        public DateTime lastUpdate { get; set; } // drop if too old.   
        public double speedLimitMetersPerSecond { get; set; } = 11; //11 m/s ~= 25MPH. Set to 0 to disable.
        public double pathExpirationMinutes { get; set; } = 3;

        public void AddPoint(string plusCode11) {
            var olc = OpenLocationCode.DecodeValid(plusCode11);
            AddPoint(olc.ToPoint());
        }

        public void AddPoint(GeoArea olc) {
            AddPoint(olc.ToPoint());
        }

        public void AddPoint(NetTopologySuite.Geometries.Point p) {
            if (lastUpdate.AddMinutes(pathExpirationMinutes) < DateTime.UtcNow) {
                //path expired, reset it.
                points.Clear();
            }

            var lastPoint = points.LastOrDefault();
            double speed = 0;
            if (lastPoint != null && speedLimitMetersPerSecond > 0 ) {
                speed = GeometrySupport.SpeedCheck(p, DateTime.UtcNow, new NetTopologySuite.Geometries.Point(lastPoint.X, lastPoint.Y), lastUpdate);
            }

            if (speedLimitMetersPerSecond == 0 || speed <= speedLimitMetersPerSecond) {
                JsonPoint thisPoint = new JsonPoint() { X = p.X, Y = p.Y };
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
