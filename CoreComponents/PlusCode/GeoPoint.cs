using System;

namespace Google.OpenLocationCode
{
    /// <summary>
    /// A point on the geographic coordinate system specified by latitude and longitude coordinates in degrees.
    /// </summary>
    public struct GeoPoint : IEquatable<GeoPoint>
    {

        /// <param name="latitude">The latitude coordinate in decimal degrees.</param>
        /// <param name="longitude">The longitude coordinate in decimal degrees.</param>
        /// <exception cref="ArgumentException">If latitude is out of range -90 to 90.</exception>
        /// <exception cref="ArgumentException">If longitude is out of range -180 to 180.</exception>
        public GeoPoint(double latitude, double longitude)
        {
            if (latitude < -90 || latitude > 90) throw new ArgumentException("latitude is out of range -90 to 90");
            if (longitude < -180 || longitude > 180) throw new ArgumentException("longitude is out of range -180 to 180");

            Latitude = latitude;
            Longitude = longitude;
        }

        /// <summary>
        /// The latitude coordinate in decimal degrees (y axis).
        /// </summary>
        public double Latitude { get; }

        /// <summary>
        /// The longitude coordinate in decimal degrees (x axis).
        /// </summary>
        public double Longitude { get; }


        /// <returns>A human readable representation of this GeoPoint coordinates.</returns>
        public override string ToString() => $"[Latitude:{Latitude},Longitude:{Longitude}]";

        /// <returns>The hash code for this GeoPoint coordinates.</returns>
        public override int GetHashCode() => Latitude.GetHashCode() ^ Longitude.GetHashCode();

        /// <inheritdoc />
        /// <summary>
        /// Determines whether the provided object is a GeoPoint with the same
        /// <see cref="Latitude"/> and <see cref="Longitude"/> as this GeoPoint.
        /// </summary>
        public override bool Equals(object obj) => obj is GeoPoint coord && Equals(coord);

        /// <inheritdoc />
        /// <summary>
        /// Determines whether the provided GoePoint has the same
        /// <see cref="Latitude"/> and <see cref="Longitude"/> as this GeoPoint.
        /// </summary>
        public bool Equals(GeoPoint other) => this == other;

        /// <summary>Equality comparison of 2 GeoPoint coordinates.</summary>
        public static bool operator ==(GeoPoint a, GeoPoint b) => a.Latitude == b.Latitude && a.Longitude == b.Longitude;

        /// <summary>Inequality comparison of 2 Geopoint coordinates.</summary>
        public static bool operator !=(GeoPoint a, GeoPoint b) => !(a == b);

    }
}
