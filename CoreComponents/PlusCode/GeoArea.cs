using System;

namespace Google.OpenLocationCode
{
    /// <summary>
    /// A rectangular area on the geographic coordinate system specified by the minimum and maximum <see cref="GeoPoint"/> coordinates.
    /// The coordinates include the latitude and longitude of the lower left (south west) and upper right (north east) corners.
    /// <para>
    /// Additional properties exist to calculate the <see cref="Center"/> of the bounding box,
    /// and the <see cref="LatitudeHeight"/> or <see cref="LongitudeWidth"/> area dimensions in degrees.
    /// </para>
    /// </summary>
    public class GeoArea
    {

        /// <summary>
        /// Create a new rectangular GeoArea of the provided min and max geo points.
        /// </summary>
        /// <param name="min">The minimum GeoPoint</param>
        /// <param name="max">The maximum GeoPoint</param>
        /// <exception cref="ArgumentException">If min is greater than or equal to max.</exception>
        public GeoArea(GeoPoint min, GeoPoint max)
        {
            if (min.Latitude > max.Latitude || min.Longitude > max.Longitude)
            {
                throw new ArgumentException("min must be less than max");
            }
            Min = min;
            Max = max;
        }

        /// <summary>
        /// Create a new rectangular GeoArea of the provided min and max geo coordinates.
        /// </summary>
        /// <param name="southLatitude">The minimum south latitude</param>
        /// <param name="westLongitude">The minimum west longitude</param>
        /// <param name="northLatitude">The maximum north latitude</param>
        /// <param name="eastLongitude">The maximum east longitude</param>
        public GeoArea(double southLatitude, double westLongitude, double northLatitude, double eastLongitude) :
            this(new GeoPoint(southLatitude, westLongitude), new GeoPoint(northLatitude, eastLongitude))
        { }

        /// <summary>
        /// Create a new copy of the provided GeoArea
        /// </summary>
        /// <param name="other">The other GeoArea to copy</param>
        public GeoArea(GeoArea other) : this(other.Min, other.Max) { }

        /// <summary>
        /// The min (south west) point coordinates of the area bounds.
        /// </summary>
        public GeoPoint Min { get; }

        /// <summary>
        /// The max (north east) point coordinates of the area bounds.
        /// </summary>
        public GeoPoint Max { get; }

        /// <summary>
        /// The center point of the area which is equidistant between <see cref="Min"/> and <see cref="Max"/>.
        /// </summary>
        public GeoPoint Center => new GeoPoint(CenterLatitude, CenterLongitude);


        /// <summary>
        /// The width of the area in longitude degrees.
        /// </summary>
        public double LongitudeWidth => (double)((decimal)Max.Longitude - (decimal)Min.Longitude);

        /// <summary>
        /// The height of the area in latitude degrees.
        /// </summary>
        public double LatitudeHeight => (double)((decimal)Max.Latitude - (decimal)Min.Latitude);


        /// <summary>The south (min) latitude coordinate in decimal degrees.</summary>
        /// <remarks>Alias to <see cref="Min"/>.<see cref="GeoPoint.Latitude">Latitude</see></remarks>
        public double SouthLatitude => Min.Latitude;

        /// <summary>The west (min) longitude coordinate in decimal degrees.</summary>
        /// <remarks>Alias to <see cref="Min"/>.<see cref="GeoPoint.Longitude">Longitude</see></remarks>
        public double WestLongitude => Min.Longitude;

        /// <summary>The north (max) latitude coordinate in decimal degrees.</summary>
        /// <remarks>Alias to <see cref="Max"/>.<see cref="GeoPoint.Latitude">Latitude</see></remarks>
        public double NorthLatitude => Max.Latitude;

        /// <summary>The east (max) longitude coordinate in decimal degrees.</summary>
        /// <remarks>Alias to <see cref="Max"/>.<see cref="GeoPoint.Longitude">Longitude</see></remarks>
        public double EastLongitude => Max.Longitude;

        /// <summary>The center latitude coordinate in decimal degrees.</summary>
        /// <remarks>Alias to <see cref="Center"/>.<see cref="GeoPoint.Latitude">Latitude</see></remarks>
        public double CenterLatitude => (Min.Latitude + Max.Latitude) / 2;

        /// <summary>The center longitude coordinate in decimal degrees.</summary>
        /// <remarks>Alias to <see cref="Center"/>.<see cref="GeoPoint.Longitude">Longitude</see></remarks>
        public double CenterLongitude => (Min.Longitude + Max.Longitude) / 2;


        /// <returns><c>true</c> if this geo area contains the provided point, <c>false</c> otherwise.</returns>
        /// <param name="point">The point coordinates to check.</param>
        public bool Contains(GeoPoint point)
        {
            return Contains(point.Latitude, point.Longitude);
        }

        /// <returns><c>true</c> if this geo area contains the provided point, <c>false</c> otherwise.</returns>
        /// <param name="latitude">The latitude coordinate of the point to check.</param>
        /// <param name="longitude">The longitude coordinate of the point to check.</param>
        public bool Contains(double latitude, double longitude)
        {
            return Min.Latitude <= latitude && latitude < Max.Latitude
                && Min.Longitude <= longitude && longitude < Max.Longitude;
        }

    }
}
