using Google.OpenLocationCode;
using NetTopologySuite.Geometries;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PraxisCore.GameTools
{
    /// <summary>
    /// A class used to save Point locations to JSON format inside other classes.
    /// </summary>
    public class JsonPoint {
        public double X { get; set; }
        public double Y { get; set; }
    }

    /// <summary>
    /// Stores a list of point that have been recently visited, and can provide them as a Geometry(Linestring) object to be drawn. 
    /// Duplicates functionality seen in fitness trackers and smart watches, with automatic expiration and replacement of data for privacy.
    /// If this stores a player's location history, it MUST be saved with SetSecurePlayerData()
    /// </summary>
    public sealed class RecentPath {
        /// <summary>
        /// The list of Points in the path, in order. 
        /// </summary>
        public List<JsonPoint> points { get; set; } = new List<JsonPoint>(); //Coordinates and Points don't convert to JSON nicely.
        /// <summary>
        /// The timestamp when the most recent point was added to the list of points. If this is older than pathExpirationMinutes ago, the list of points is erased and started over.
        /// </summary>
        public DateTime lastUpdate { get; set; }
        /// <summary>
        /// A speed limit, in meters per second. Setting to  0 disables the speed checks.
        /// If the speed between the current and last points is over this, the point is ignored entirely. Future points may be added to the path,
        /// if they are added before the expiration timer is reached and the total speed traveled falls under the speed limit.
        /// </summary>
        public double speedLimitMetersPerSecond { get; set; } = 11; //11 m/s ~= 25MPH.
        /// <summary>
        /// How long should pass, in minutes, before the recent path expires and restarts. Path data is retained until AddPoint() is called after this expiration time is reached.
        /// A game could load a path from a previous session, check if it's expired, and grant any rewards for a path if so, or continue adding points to the existing path if not.
        /// </summary>
        public double pathExpirationMinutes { get; set; } = 3;

        /// <summary>
        /// Adds the center of the given PlusCode to the current path.
        /// </summary>
        public void AddPoint(string plusCode11) {
            var olc = OpenLocationCode.DecodeValid(plusCode11);
            AddPoint(olc.ToPoint());
        }

        /// <summary>
        /// Adds the center of the given GeoArea to the current path.
        /// </summary>
        public void AddPoint(GeoArea olc) {
            AddPoint(olc.ToPoint());
        }

        /// <summary>
        /// Adds the given point to the current path.
        /// </summary>
        public void AddPoint(NetTopologySuite.Geometries.Point p) {
            if (lastUpdate.AddMinutes(pathExpirationMinutes) < DateTime.UtcNow) {
                //path expired, reset it.
                points.Clear();
            }

            var lastPoint = points.LastOrDefault();
            if (lastPoint == null) {
                JsonPoint thisPoint = new JsonPoint() { X = p.X, Y = p.Y };
                points.Add(thisPoint);
                lastUpdate = DateTime.UtcNow;
                return;
            }

            double speed = 0;
            if (speedLimitMetersPerSecond > 0 ) {
                speed = GeometrySupport.SpeedCheck(p, DateTime.UtcNow, new NetTopologySuite.Geometries.Point(lastPoint.X, lastPoint.Y), lastUpdate);
            }

            if (speed <= speedLimitMetersPerSecond)
            {
                if (lastPoint.X != p.X && lastPoint.Y != p.Y)
                {
                    JsonPoint thisPoint = new JsonPoint() { X = p.X, Y = p.Y };
                    points.Add(thisPoint);
                    lastUpdate = DateTime.UtcNow;
                }
            }
        }

        /// <summary>
        /// Takes the path in its current state, and renders it as a LineString Geometry to allow for drawing on a map.
        /// </summary>
        public LineString ConvertToLine() {
            if (points.Count > 1) //can't draw a line of 1 point.
                return Singletons.geometryFactory.CreateLineString(points.Select(p => new Coordinate(p.X, p.Y)).ToArray());
            return null;
        }
    }
}
