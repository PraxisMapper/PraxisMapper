using Google.OpenLocationCode;
using NetTopologySuite.Geometries;
using System;

namespace PraxisCore.GameTools {
    /// <summary>
    /// DistanceTracker allows you to calculate the cumulative distance traveled between two points. A minimum can be applied to ignore GPS drift, and a speed limit can be applied
    /// as well to ignore movement that's too fast to be gameplay. If this tracks a player, it MUST be saved with SetSecurePlayerData, because it contains a location and a timestamp.
    /// </summary>
    public sealed class DistanceTracker {

        /// <summary>
        /// The most recently recorded point. Required to determine distance to the next point provided. Reset this with SetLastPoint() when you need to start tracking distance
        /// again after stopping, such as when a player has reopened their game for the first time. 
        /// </summary>
        public JsonPoint lastPoint { get; set; }
        /// <summary>
        /// The total valid distance this object has recorded, in meters.
        /// </summary>
        public double totalDistance { get; set; }
        /// <summary>
        /// The smallest increment of distance this object should record, intended to help negate GPS drift. 
        /// Values under 3 are unlikely to be reported when using PlusCodes, since a Cell11 is ~3.5m.
        /// Values under 1 meter are very likely to occur if raw GPS coordinates are provided, since very few phones are reliably accurate for distances under 1 meter.
        /// This number may need adjusted depending on how often you call Add if you want to skip checking this on every location call back to the server.
        /// </summary>
        public double minimumChange { get; set; } = 1; 
        /// <summary>
        /// If this is > 0, each Add() call will check against the previous point and time recorded to determine how fast travelling between those two points was. 
        /// Points that are too fast will not be added to the total distance covered, but will be used for future checks.
        /// This value is meters per second, you can roughly convert to miles per hours by doubling this number.
        /// </summary>
        public double speedCapMetersPerSecond { get; set; } = 0;
        /// <summary>
        /// The timestamp to use for determining speed when Add() is called with a new point. This is not read or set if speedCapMetersPerSecond is 0.
        /// </summary>
        public DateTime? lastPointRecordedAt { get; set; } //Only saved if there's a speed cap check.

        public DistanceTracker() { }

        public DistanceTracker(double speedCap, double minDistance) {
            speedCapMetersPerSecond = speedCap;
            minimumChange = minDistance;
        }

        /// <summary>
        /// Replaces the current last-tracked point and timestamp with the given PlusCode's center and current timestamp.
        /// </summary>
        /// <param name="plusCode"></param>
        public void SetLastPoint(string plusCode) {
            SetLastPoint(plusCode.ToGeoArea().ToPoint());
        }

        /// <summary>
        /// Replaces the current last-tracked point and timestamp with the given GeoArea's center and current timestamp.
        /// </summary>
        public void SetLastPoint(GeoArea g) {
            SetLastPoint(g.ToPoint());
        }

        /// <summary>
        /// Replaces the current last-tracked point and timestamp with the given point and current timestamp.
        /// </summary>
        public void SetLastPoint(Point incPoint) {
            lastPoint = new JsonPoint() { X = incPoint.X, Y = incPoint.Y };
            lastPointRecordedAt = DateTime.UtcNow;
        }

        /// <summary>
        /// Add the distance traveled between the last point and the given PlusCode's center to the total distance.
        /// </summary>
        /// <param name="plusCode"></param>
        public void Add(string plusCode) {
            Add(plusCode.ToGeoArea().ToPoint());
        }

        /// <summary>
        /// Add the distance traveled between the last point and the given GeoArea's center to the total distance.
        /// </summary>
        public void Add(GeoArea g) {
            Add(g.ToPoint());
        }

        /// <summary>
        /// Add the distance traveled between the last point and the given Point to the total distance.
        /// </summary>
        public void Add(Point point) {
            if (lastPoint == null) {
                lastPoint = new JsonPoint() { X = point.X, Y = point.Y };
                totalDistance = 0;
                if (speedCapMetersPerSecond > 0)
                    lastPointRecordedAt = DateTime.UtcNow;
                return;
            }

            if (lastPoint.X == point.X && lastPoint.Y == point.Y) //No movement occurred, skip processing.
                return;

            Point lp = new Point(lastPoint.X, lastPoint.Y);
            var distance = lp.MetersDistanceTo(point);
            bool speedCapOk = false;

            if (speedCapMetersPerSecond == 0)
                speedCapOk = true;
            else {
                if (GeometrySupport.SpeedCheck(lp, lastPointRecordedAt.Value, point, DateTime.UtcNow) < speedCapMetersPerSecond) {
                    speedCapOk = true;
                }
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
