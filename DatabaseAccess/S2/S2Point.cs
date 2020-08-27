using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Google.Common.Geometry
{
    public struct S2Point : IEquatable<S2Point>, IComparable<S2Point>
    {
        // coordinates of the points
        private readonly double _x;
        private readonly double _y;
        private readonly double _z;


        public S2Point(double x, double y, double z)
        {
            _x = x;
            _y = y;
            _z = z;
        }

        public double X
        {
            get { return _x; }
        }

        public double Y
        {
            get { return _y; }
        }

        public double Z
        {
            get { return _z; }
        }

        public double Norm2
        {
            get { return _x * _x + _y * _y + _z * _z; }
        }

        public double Norm
        {
            get { return Math.Sqrt(Norm2); }
        }

        public S2Point Ortho
        {
            get
            {
                var k = LargestAbsComponent;
                S2Point temp;
                if (k == 1)
                {
                    temp = new S2Point(1, 0, 0);
                }
                else if (k == 2)
                {
                    temp = new S2Point(0, 1, 0);
                }
                else
                {
                    temp = new S2Point(0, 0, 1);
                }
                return Normalize(CrossProd(this, temp));
            }
        }

        /** Return the index of the largest component fabs */

        public int LargestAbsComponent
        {
            get
            {
                var temp = Fabs(this);
                if (temp._x > temp._y)
                {
                    if (temp._x > temp._z)
                    {
                        return 0;
                    }
                    else
                    {
                        return 2;
                    }
                }
                else
                {
                    if (temp._y > temp._z)
                    {
                        return 1;
                    }
                    else
                    {
                        return 2;
                    }
                }
            }
        }

        public double this[int axis]
        {
            get { return (axis == 0) ? _x : (axis == 1) ? _y : _z; }
        }

        public int CompareTo(S2Point other)
        {
            return this < other ? -1 : (Equals(other) ? 0 : 1);
        }

        public bool Equals(S2Point other)
        {
            return _x.Equals(other._x) && _y.Equals(other._y) && _z.Equals(other._z);
        }

        public override bool Equals(object obj)
        {
            if (obj.GetType() != GetType()) return false;
            return Equals((S2Point)obj);
        }

        /**
   * Calcualates hashcode based on stored coordinates. Since we want +0.0 and
   * -0.0 to be treated the same, we ignore the sign of the coordinates.
   */

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = Math.Abs(_x).GetHashCode();
                hashCode = (hashCode * 397) ^ Math.Abs(_y).GetHashCode();
                hashCode = (hashCode * 397) ^ Math.Abs(_z).GetHashCode();
                return hashCode;
            }
        }

        public static bool operator ==(S2Point left, S2Point right)
        {
            return Equals(left, right);
        }

        public static bool operator !=(S2Point left, S2Point right)
        {
            return !Equals(left, right);
        }

        public static S2Point operator -(S2Point p1, S2Point p2)
        {
            return new S2Point(p1._x - p2._x, p1._y - p2._y, p1._z - p2._z);
        }

        public static S2Point operator -(S2Point p)
        {
            return new S2Point(-p._x, -p._y, -p._z);
        }

        public static S2Point CrossProd(S2Point p1, S2Point p2)
        {
            return new S2Point(
                p1._y * p2._z - p1._z * p2._y, p1._z * p2._x - p1._x * p2._z, p1._x * p2._y - p1._y * p2._x);
        }

        public static S2Point operator +(S2Point p1, S2Point p2)
        {
            return new S2Point(p1._x + p2._x, p1._y + p2._y, p1._z + p2._z);
        }

        public double DotProd(S2Point that)
        {
            return _x * that._x + _y * that._y + _z * that._z;
        }

        public static S2Point operator *(S2Point p, double m)
        {
            return new S2Point(m * p._x, m * p._y, m * p._z);
        }

        public static S2Point operator /(S2Point p, double m)
        {
            return new S2Point(p._x / m, p._y / m, p._z / m);
        }


        /** return a vector orthogonal to this one */

        public static S2Point Fabs(S2Point p)
        {
            return new S2Point(Math.Abs(p._x), Math.Abs(p._y), Math.Abs(p._z));
        }

        public static S2Point Normalize(S2Point p)
        {
            var norm = p.Norm;
            if (norm != 0)
            {
                norm = 1.0 / norm;
            }
            return p * norm;
        }


        /** Return the angle between two vectors in radians */

        public double Angle(S2Point va)
        {
            return Math.Atan2(CrossProd(this, va).Norm, DotProd(va));
        }

        /**
   * Compare two vectors, return true if all their components are within a
   * difference of margin.
   */

        public bool ApproxEquals(S2Point that, double margin)
        {
            return (Math.Abs(_x - that._x) < margin) && (Math.Abs(_y - that._y) < margin)
                   && (Math.Abs(_z - that._z) < margin);
        }


        public static bool operator <(S2Point x, S2Point y)
        {
            if (x._x < y._x)
            {
                return true;
            }
            if (y._x < x._x)
            {
                return false;
            }
            if (x._y < y._y)
            {
                return true;
            }
            if (y._y < x._y)
            {
                return false;
            }
            if (x._z < y._z)
            {
                return true;
            }
            return false;
        }

        public static bool operator >(S2Point x, S2Point y)
        {
            if (x._x > y._x)
            {
                return true;
            }
            if (y._x > x._x)
            {
                return false;
            }
            if (x._y > y._y)
            {
                return true;
            }
            if (y._y > x._y)
            {
                return false;
            }
            if (x._z > y._z)
            {
                return true;
            }
            return false;
        }


        public override string ToString()
        {
            return "(" + _x + ", " + _y + ", " + _z + ")";
        }

        public string ToDegreesString()
        {
            var s2LatLng = new S2LatLng(this);
            return "(" + s2LatLng.LatDegrees + ", "
                   + s2LatLng.LngDegrees + ")";
        }
    }
}