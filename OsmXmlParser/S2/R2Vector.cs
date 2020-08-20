using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Google.Common.Geometry
{
    public struct R2Vector : IEquatable<R2Vector>
    {
        private readonly double _x;
        private readonly double _y;

        public R2Vector(double x, double y)
        {
            _x = x;
            _y = y;
        }

        /// <summary>
        ///     Point as a list of 2; x is index 0, y is index 1
        /// </summary>
        /// <param name="coord"></param>
        public R2Vector(IList<double> coord)
        {
            if (coord.Count != 2)
            {
                throw new ArgumentException("Points must have exactly 2 coordinates", "coord");
            }
            _x = coord[0];
            _y = coord[1];
        }

        public double X
        {
            get { return _x; }
        }

        public double Y
        {
            get { return _y; }
        }

        public double this[int index]
        {
            get
            {
                if (index > 1)
                {
                    throw new ArgumentOutOfRangeException("index");
                }
                return index == 0 ? _x : _y;
            }
        }

        public double Norm2
        {
            get { return (_x * _x) + (_y * _y); }
        }

        public bool Equals(R2Vector other)
        {
            return _y.Equals(other._y) && _x.Equals(other._x);
        }

        public override bool Equals(object obj)
        {
            return obj is R2Vector && Equals((R2Vector)obj);
        }


        /**
     * Calcualates hashcode based on stored coordinates. Since we want +0.0 and
     * -0.0 to be treated the same, we ignore the sign of the coordinates.
     */

        public override int GetHashCode()
        {
            unchecked
            {
                return (Math.Abs(_y).GetHashCode() * 397) ^ Math.Abs(_x).GetHashCode();
            }
        }

        public static bool operator ==(R2Vector left, R2Vector right)
        {
            return Equals(left, right);
        }

        public static bool operator !=(R2Vector left, R2Vector right)
        {
            return !Equals(left, right);
        }

        public static R2Vector operator +(R2Vector p1, R2Vector p2)
        {
            return new R2Vector(p1._x + p2._x, p1._y + p2._y);
        }


        public static R2Vector operator *(R2Vector p, double m)
        {
            return new R2Vector(m * p._x, m * p._y);
        }

        public static double DotProd(R2Vector p1, R2Vector p2)
        {
            return (p1._x * p2._x) + (p1._y * p2._y);
        }

        public double DotProd(R2Vector that)
        {
            return DotProd(this, that);
        }

        public double CrossProd(R2Vector that)
        {
            return _x * that._y - _y * that._x;
        }

        public static bool operator <(R2Vector x, R2Vector y)
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
            return false;
        }

        public static bool operator >(R2Vector x, R2Vector y)
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
            return false;
        }

        public override string ToString()
        {
            return "(" + _x + ", " + _y + ")";
        }
    }
}