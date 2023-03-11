using Google.OpenLocationCode;
using NetTopologySuite.Geometries;

namespace PraxisCore.GameTools {
    public class DistanceTracker {

        public JsonPoint lastPoint { get; set; }
        public double totalDistance { get; set; }
        public double minimumChange { get; set; } = 1; //Phones are practically never more accurate than 1 meter. This would need reduced if you ask on every GPS event client-side.

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
                lastPoint =new JsonPoint() { X = point.X, Y = point.Y };
                totalDistance = 0;
                return;
            }

            var distance = new Point(lastPoint.X, lastPoint.Y).MetersDistanceTo(point);
            if (distance > minimumChange) {
                distance += minimumChange;
                lastPoint.X = point.X;
                lastPoint.Y = point.Y;
            }
        }
    }
}
