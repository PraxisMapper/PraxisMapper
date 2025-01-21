using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Google.Common.Geometry
{
    /**
 * This class represents a point on the unit sphere as a pair of
 * latitude-longitude coordinates. Like the rest of the "geometry" package, the
 * intent is to represent spherical geometry as a mathematical abstraction, so
 * functions that are specifically related to the Earth's geometry (e.g.
 * easting/northing conversions) should be put elsewhere.
 *
 */

    public struct S2LatLng : IEquatable<S2LatLng>
    {
        public const double EarthRadiusMeters = 6367000.0;

        /** The center point the lat/lng coordinate system. */
        public static readonly S2LatLng Center = new S2LatLng(0.0, 0.0);

        private readonly double _latRadians;
        private readonly double _lngRadians;

        private S2LatLng(double latRadians, double lngRadians)
        {
            _latRadians = latRadians;
            _lngRadians = lngRadians;
        }

        /**
   * Basic constructor. The latitude and longitude must be within the ranges
   * allowed by is_valid() below.
   *
   */

        public S2LatLng(S1Angle lat, S1Angle lng) : this(lat.Radians, lng.Radians)
        {
        }


        /**
   * Convert a point (not necessarily normalized) to an S2LatLng.
   *
   * TODO(dbeaumont): Make this a static factory method (fromPoint() ?).
   */

        public S2LatLng(S2Point p) : this(Math.Atan2(p.Z, Math.Sqrt(p.X * p.X + p.Y * p.Y)), Math.Atan2(p.Y, p.X))
        {
            // The latitude and longitude are already normalized. We use atan2 to
            // compute the latitude because the input vector is not necessarily unit
            // length, and atan2 is much more accurate than asin near the poles.
            // Note that atan2(0, 0) is defined to be zero.
        }

        internal S1Angle Lat
        {
            get { return S1Angle.FromRadians(_latRadians); }
        }

        /** Returns the latitude of this point as radians. */

        public double LatRadians
        {
            get { return _latRadians; }
        }

        /** Returns the latitude of this point as degrees. */

        public double LatDegrees
        {
            get { return 180.0 / Math.PI * _latRadians; }
        }

        /** Returns the longitude of this point as a new S1Angle. */

        internal S1Angle Lng
        {
            get { return S1Angle.FromRadians(_lngRadians); }
        }

        /** Returns the longitude of this point as radians. */

        public double LngRadians
        {
            get { return _lngRadians; }
        }

        /** Returns the longitude of this point as degrees. */

        public double LngDegrees
        {
            get { return 180.0 / Math.PI * _lngRadians; }
        }

        /**
   * Return true if the latitude is between -90 and 90 degrees inclusive and the
   * longitude is between -180 and 180 degrees inclusive.
   */

        public bool IsValid
        {
            get { return Math.Abs(Lat.Radians) <= S2.PiOver2 && Math.Abs(Lng.Radians) <= S2.Pi; }
        }

        /**
   * Returns a new S2LatLng based on this instance for which {@link #isValid()}
   * will be {@code true}.
   * <ul>
   * <li>Latitude is clipped to the range {@code [-90, 90]}
   * <li>Longitude is normalized to be in the range {@code [-180, 180]}
   * </ul>
   * <p>If the current point is valid then the returned point will have the same
   * coordinates.
   */

        public S2LatLng Normalized
        {
            get
            {
                // drem(x, 2 * S2.M_PI) reduces its argument to the range
                // [-S2.M_PI, S2.M_PI] inclusive, which is what we want here.
                return new S2LatLng(Math.Max(-S2.PiOver2, Math.Min(S2.PiOver2, Lat.Radians)),
                                    Math.IEEERemainder(Lng.Radians, 2 * S2.Pi));
            }
        }

        public bool Equals(S2LatLng other)
        {
            return _lngRadians.Equals(other._lngRadians) && _latRadians.Equals(other._latRadians);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            return obj is S2LatLng && Equals((S2LatLng)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (_lngRadians.GetHashCode() * 397) ^ _latRadians.GetHashCode();
            }
        }

        public static bool operator ==(S2LatLng left, S2LatLng right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(S2LatLng left, S2LatLng right)
        {
            return !left.Equals(right);
        }

        /**
   * Approximate "effective" radius of the Earth in meters.
   */

        public static S2LatLng FromRadians(double latRadians, double lngRadians)
        {
            return new S2LatLng(latRadians, lngRadians);
        }

        public static S2LatLng FromDegrees(double latDegrees, double lngDegrees)
        {
            return new S2LatLng(S1Angle.FromDegrees(latDegrees), S1Angle.FromDegrees(lngDegrees));
        }

        public static S2LatLng FromE5(long latE5, long lngE5)
        {
            return new S2LatLng(S1Angle.E5(latE5), S1Angle.E5(lngE5));
        }

        public static S2LatLng FromE6(long latE6, long lngE6)
        {
            return new S2LatLng(S1Angle.E6(latE6), S1Angle.E6(lngE6));
        }

        public static S2LatLng FromE7(long latE7, long lngE7)
        {
            return new S2LatLng(S1Angle.E7(latE7), S1Angle.E7(lngE7));
        }

        public static S1Angle Latitude(S2Point p)
        {
            // We use atan2 rather than asin because the input vector is not necessarily
            // unit length, and atan2 is much more accurate than asin near the poles.
            return S1Angle.FromRadians(
                Math.Atan2(p[2], Math.Sqrt(p[0] * p[0] + p[1] * p[1])));
        }

        public static S1Angle Longitude(S2Point p)
        {
            // Note that atan2(0, 0) is defined to be zero.
            return S1Angle.FromRadians(Math.Atan2(p[1], p[0]));
        }

        /** This is internal to avoid ambiguity about which units are expected. */

        /** Returns the latitude of this point as a new S1Angle. */

        // Clamps the latitude to the range [-90, 90] degrees, and adds or subtracts
        // a multiple of 360 degrees to the longitude if necessary to reduce it to
        // the range [-180, 180].

        /** Convert an S2LatLng to the equivalent unit-length vector (S2Point). */

        public S2Point ToPoint()
        {
            var phi = Lat.Radians;
            var theta = Lng.Radians;
            var cosphi = Math.Cos(phi);
            return new S2Point(Math.Cos(theta) * cosphi, Math.Sin(theta) * cosphi, Math.Sin(phi));
        }

        /**
   * Return the distance (measured along the surface of the sphere) to the given
   * point.
   */

        public S1Angle GetDistance(S2LatLng o)
        {
            // This implements the Haversine formula, which is numerically stable for
            // small distances but only gets about 8 digits of precision for very large
            // distances (e.g. antipodal points). Note that 8 digits is still accurate
            // to within about 10cm for a sphere the size of the Earth.
            //
            // This could be fixed with another sin() and cos() below, but at that point
            // you might as well just convert both arguments to S2Points and compute the
            // distance that way (which gives about 15 digits of accuracy for all
            // distances).

            var lat1 = Lat.Radians;
            var lat2 = o.Lat.Radians;
            var lng1 = Lng.Radians;
            var lng2 = o.Lng.Radians;
            var dlat = Math.Sin(0.5 * (lat2 - lat1));
            var dlng = Math.Sin(0.5 * (lng2 - lng1));
            var x = dlat * dlat + dlng * dlng * Math.Cos(lat1) * Math.Cos(lat2);
            return S1Angle.FromRadians(2 * Math.Atan2(Math.Sqrt(x), Math.Sqrt(Math.Max(0.0, 1.0 - x))));
            // Return the distance (measured along the surface of the sphere) to the
            // given S2LatLng. This is mathematically equivalent to:
            //
            // S1Angle::FromRadians(ToPoint().Angle(o.ToPoint())
            //
            // but this implementation is slightly more efficient.
        }

        /**
   * Returns the surface distance to the given point assuming a constant radius.
   */

        public double GetDistance(S2LatLng o, double radius)
        {
            // TODO(dbeaumont): Maybe check that radius >= 0 ?
            return GetDistance(o).Radians * radius;
        }

        /**
   * Returns the surface distance to the given point assuming the default Earth
   * radius of {@link #EARTH_RADIUS_METERS}.
   */

        public double GetEarthDistance(S2LatLng o)
        {
            return GetDistance(o, EarthRadiusMeters);
        }

        /**
   * Adds the given point to this point.
   * Note that there is no guarantee that the new point will be <em>valid</em>.
   */

        public static S2LatLng operator +(S2LatLng x, S2LatLng y)
        {
            return new S2LatLng(x._latRadians + y._latRadians, x._lngRadians + y._lngRadians);
        }


        /**
   * Subtracts the given point from this point.
   * Note that there is no guarantee that the new point will be <em>valid</em>.
   */

        public static S2LatLng operator -(S2LatLng x, S2LatLng y)
        {
            return new S2LatLng(x._latRadians - y._latRadians, x._lngRadians - y._lngRadians);
        }

        /**
   * Scales this point by the given scaling factor.
   * Note that there is no guarantee that the new point will be <em>valid</em>.
   */

        public static S2LatLng operator *(S2LatLng x, double m)
        {
            // TODO(dbeaumont): Maybe check that m >= 0 ?
            return new S2LatLng(x._latRadians * m, x._lngRadians * m);
        }

        /**
   * Returns true if both the latitude and longitude of the given point are
   * within {@code maxError} radians of this point.
   */

        public bool ApproxEquals(S2LatLng o, double maxError)
        {
            return (Math.Abs(_latRadians - o._latRadians) < maxError)
                   && (Math.Abs(_lngRadians - o._lngRadians) < maxError);
        }

        /**
   * Returns true if the given point is within {@code 1e-9} radians of this
   * point. This corresponds to a distance of less than {@code 1cm} at the
   * surface of the Earth.
   */

        public bool ApproxEquals(S2LatLng o)
        {
            return ApproxEquals(o, 1e-9);
        }

        public override String ToString()
        {
            return "(" + _latRadians + ", " + _lngRadians + ")";
        }

        public String ToStringDegrees()
        {
            return "(" + LatDegrees + ", " + LngDegrees + ")";
        }
    }
}