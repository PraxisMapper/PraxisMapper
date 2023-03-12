using Google.OpenLocationCode;
using NetTopologySuite.Geometries;
using System;

namespace PraxisCore.GameTools {
    public class DistanceTracker {

        public JsonPoint lastPoint { get; set; }
        public double totalDistance { get; set; }
        public double minimumChange { get; set; } = 1; //Phones are practically never more accurate than 1 meter. This would need reduced if you ask on every GPS event client-side.
        public double speedCapMetersPerSecond { get; set; } = 0;
        public DateTime? lastPointRecordedAt { get; set; } //Only saved if there's a speed cap check.

        public DistanceTracker() { }

        public DistanceTracker(double minChangeAllowed) {
            minimumChange = minChangeAllowed;
        }

        public void Add(string plusCode) {
            Add(plusCode.ToGeoArea().ToPoint());
        }

        public void Add(GeoArea g) {
            Add(g.ToPoint());
        }

        public void Add(Point point) {
            if (lastPoint == null) {
                lastPoint = new JsonPoint() { X = point.X, Y = point.Y };
                totalDistance = 0;
                if (speedCapMetersPerSecond > 0)
                    lastPointRecordedAt = DateTime.UtcNow;
                return;
            }

            Point lp = new Point(lastPoint.X, lastPoint.Y);
            var distance = lp.MetersDistanceTo(point);
            bool speedCapOk = false;

            if (speedCapMetersPerSecond == 0)
                speedCapOk = true;
            else if (GeometrySupport.SpeedCheck(lp, lastPointRecordedAt.Value, point, DateTime.UtcNow) < speedCapMetersPerSecond) {
                speedCapOk = true;
                lastPointRecordedAt = DateTime.UtcNow;
            }

            if (distance > minimumChange && speedCapOk) { //Distance only counts if it's above the minimum and (if speed if capped) you stayed under the speed cap
                totalDistance += distance; 
            }

            lastPoint.X = point.X;
            lastPoint.Y = point.Y;
        }
    }
}
