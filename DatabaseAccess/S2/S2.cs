using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Google.Common.Geometry
{
    //Note: S2 code used in this folder is Apache-2.0 licensed.
    public static class S2
    {
        // Declare some frequently used constants
        public const double Pi = Math.PI;
        public const double InversePi = 1.0 / Math.PI;
        public const double PiOver2 = Math.PI / 2.0;
        public const double PiOver4 = Math.PI / 4.0;
        public const double E = Math.E;

        // Together these flags define a cell orientation. If SWAP_MASK
        // is true, then canonical traversal order is flipped around the
        // diagonal (i.e. i and j are swapped with each other). If
        // INVERT_MASK is true, then the traversal order is rotated by 180
        // degrees (i.e. the bits of i and j are inverted, or equivalently,
        // the axis directions are reversed).
        public const int SwapMask = 0x01;
        public const int InvertMask = 0x02;

        // Number of bits in the mantissa of a double.
        private const int ExponentShift = 52;
        // Mask to extract the exponent from a double.
        private const long ExponentMask = 0x7ff0000000000000L;
        public static readonly double Sqrt2 = Math.Sqrt(2);

        /**
   * If v is non-zero, return an integer {@code exp} such that
   * {@code (0.5 <= |v|*2^(-exp) < 1)}. If v is zero, return 0.
   *
   * <p>Note that this arguably a bad definition of exponent because it makes
   * {@code exp(9) == 4}. In decimal this would be like saying that the
   * exponent of 1234 is 4, when in scientific 'exponent' notation 1234 is
   * {@code 1.234 x 10^3}.
   *
   * TODO(dbeaumont): Replace this with "DoubleUtils.getExponent(v) - 1" ?
   */

        /** Mapping Hilbert traversal order to orientation adjustment mask. */

        private static readonly int[] _PosToOrientation =
        {SwapMask, 0, 0, InvertMask + SwapMask};

        /**
   * Returns an XOR bit mask indicating how the orientation of a child subcell
   * is related to the orientation of its parent cell. The returned value can
   * be XOR'd with the parent cell's orientation to give the orientation of
   * the child cell.
   *
   * @param position the position of the subcell in the Hilbert traversal, in
   *     the range [0,3].
   * @return a bit mask containing some combination of {@link #SWAP_MASK} and
   *     {@link #INVERT_MASK}.
   * @throws IllegalArgumentException if position is out of bounds.
   */

        /** Mapping from cell orientation + Hilbert traversal to IJ-index. */

        private static readonly int[][] _PosToIj = new int[][]
        {
            // 0 1 2 3
            new[] {0, 1, 3, 2}, // canonical order: (0,0), (0,1), (1,1), (1,0)
            new[] {0, 2, 3, 1}, // axes swapped: (0,0), (1,0), (1,1), (0,1)
            new[] {3, 2, 0, 1}, // bits inverted: (1,1), (1,0), (0,0), (0,1)
            new[] {3, 1, 0, 2}, // swapped & inverted: (1,1), (0,1), (0,0), (1,0)
        };

        /**
   * Return the IJ-index of the subcell at the given position in the Hilbert
   * curve traversal with the given orientation. This is the inverse of
   * {@link #ijToPos}.
   *
   * @param orientation the subcell orientation, in the range [0,3].
   * @param position the position of the subcell in the Hilbert traversal, in
   *     the range [0,3].
   * @return the IJ-index where {@code 0->(0,0), 1->(0,1), 2->(1,0), 3->(1,1)}.
   * @throws IllegalArgumentException if either parameter is out of bounds.
   */

        /** Mapping from Hilbert traversal order + cell orientation to IJ-index. */

        private static readonly int[][] _IjToPos = new int[][]
        {
            // (0,0) (0,1) (1,0) (1,1)
            new[] {0, 1, 3, 2}, // canonical order
            new[] {0, 3, 1, 2}, // axes swapped
            new[] {2, 3, 1, 0}, // bits inverted
            new[] {2, 1, 3, 0}, // swapped & inverted
        };

        public static readonly S2Point Origin = new S2Point(0, 1, 0);

        internal static int Exp(double v)
        {
            if (v == 0)
            {
                return 0;
            }
            var bits = BitConverter.DoubleToInt64Bits(v);
            return (int)((ExponentMask & bits) >> ExponentShift) - 1022;
        }

        public static int PosToOrientation(int position)
        {
            Preconditions.CheckArgument(0 <= position && position < 4);

            return _PosToOrientation[position];
        }

        public static int PosToIj(int orientation, int position)
        {
            //Preconditions.checkArgument(0 <= orientation && orientation < 4);
            //Preconditions.checkArgument(0 <= position && position < 4);
            if (!(0 <= position && position < 4))
                throw new ArgumentException("position");
            if (!(0 <= orientation && orientation < 4))
                throw new ArgumentException("orientation");

            return _PosToIj[orientation][position];
        }

        /**
   * Returns the order in which a specified subcell is visited by the Hilbert
   * curve. This is the inverse of {@link #posToIJ}.
   *
   * @param orientation the subcell orientation, in the range [0,3].
   * @param ijIndex the subcell index where
   *     {@code 0->(0,0), 1->(0,1), 2->(1,0), 3->(1,1)}.
   * @return the position of the subcell in the Hilbert traversal, in the range
   *     [0,3].
   * @throws IllegalArgumentException if either parameter is out of bounds.
   */

        public static int IjToPos(int orientation, int ijIndex)
        {
            Preconditions.CheckArgument(0 <= orientation && orientation < 4);
            Preconditions.CheckArgument(0 <= ijIndex && ijIndex < 4);
            return _IjToPos[orientation][ijIndex];
        }

        /**
   * Defines an area or a length cell metric.
   */

        /**
   * Return a unique "origin" on the sphere for operations that need a fixed
   * reference point. It should *not* be a point that is commonly used in edge
   * tests in order to avoid triggering code to handle degenerate cases. (This
   * rules out the north and south poles.)
   */


        /**
   * Return true if the given point is approximately unit length (this is mainly
   * useful for assertions).
   */

        public static bool IsUnitLength(S2Point p)
        {
            return Math.Abs(p.Norm2 - 1) <= 1e-15;
        }

        /**
   * Return true if edge AB crosses CD at a point that is interior to both
   * edges. Properties:
   *
   *  (1) SimpleCrossing(b,a,c,d) == SimpleCrossing(a,b,c,d) (2)
   * SimpleCrossing(c,d,a,b) == SimpleCrossing(a,b,c,d)
   */

        public static bool SimpleCrossing(S2Point a, S2Point b, S2Point c, S2Point d)
        {
            // We compute SimpleCCW() for triangles ACB, CBD, BDA, and DAC. All
            // of these triangles need to have the same orientation (CW or CCW)
            // for an intersection to exist. Note that this is slightly more
            // restrictive than the corresponding definition for planar edges,
            // since we need to exclude pairs of line segments that would
            // otherwise "intersect" by crossing two antipodal points.

            var ab = S2Point.CrossProd(a, b);
            var cd = S2Point.CrossProd(c, d);
            var acb = -ab.DotProd(c);
            var cbd = -cd.DotProd(b);
            var bda = ab.DotProd(d);
            var dac = cd.DotProd(a);

            return (acb * cbd > 0) && (cbd * bda > 0) && (bda * dac > 0);
        }

        /**
   * Return a vector "c" that is orthogonal to the given unit-length vectors "a"
   * and "b". This function is similar to a.CrossProd(b) except that it does a
   * better job of ensuring orthogonality when "a" is nearly parallel to "b",
   * and it returns a non-zero result even when a == b or a == -b.
   *
   *  It satisfies the following properties (RCP == RobustCrossProd):
   *
   *  (1) RCP(a,b) != 0 for all a, b (2) RCP(b,a) == -RCP(a,b) unless a == b or
   * a == -b (3) RCP(-a,b) == -RCP(a,b) unless a == b or a == -b (4) RCP(a,-b)
   * == -RCP(a,b) unless a == b or a == -b
   */

        public static S2Point RobustCrossProd(S2Point a, S2Point b)
        {
            // The direction of a.CrossProd(b) becomes unstable as (a + b) or (a - b)
            // approaches zero. This leads to situations where a.CrossProd(b) is not
            // very orthogonal to "a" and/or "b". We could fix this using Gram-Schmidt,
            // but we also want b.RobustCrossProd(a) == -b.RobustCrossProd(a).
            //
            // The easiest fix is to just compute the cross product of (b+a) and (b-a).
            // Given that "a" and "b" are unit-length, this has good orthogonality to
            // "a" and "b" even if they differ only in the lowest bit of one component.

            // assert (isUnitLength(a) && isUnitLength(b));
            var x = S2Point.CrossProd(b + a, b - a);
            if (!x.Equals(new S2Point(0, 0, 0)))
            {
                return x;
            }

            // The only result that makes sense mathematically is to return zero, but
            // we find it more convenient to return an arbitrary orthogonal vector.
            return Ortho(a);
        }

        /**
   * Return a unit-length vector that is orthogonal to "a". Satisfies Ortho(-a)
   * = -Ortho(a) for all a.
   */

        public static S2Point Ortho(S2Point a)
        {
            // The current implementation in S2Point has the property we need,
            // i.e. Ortho(-a) = -Ortho(a) for all a.
            return a.Ortho;
        }

        /**
   * Return the area of triangle ABC. The method used is about twice as
   * expensive as Girard's formula, but it is numerically stable for both large
   * and very small triangles. The points do not need to be normalized. The area
   * is always positive.
   *
   *  The triangle area is undefined if it contains two antipodal points, and
   * becomes numerically unstable as the length of any edge approaches 180
   * degrees.
   */

        internal static double Area(S2Point a, S2Point b, S2Point c)
        {
            // This method is based on l'Huilier's theorem,
            //
            // tan(E/4) = sqrt(tan(s/2) tan((s-a)/2) tan((s-b)/2) tan((s-c)/2))
            //
            // where E is the spherical excess of the triangle (i.e. its area),
            // a, b, c, are the side lengths, and
            // s is the semiperimeter (a + b + c) / 2 .
            //
            // The only significant source of error using l'Huilier's method is the
            // cancellation error of the terms (s-a), (s-b), (s-c). This leads to a
            // *relative* error of about 1e-16 * s / Min(s-a, s-b, s-c). This compares
            // to a relative error of about 1e-15 / E using Girard's formula, where E is
            // the true area of the triangle. Girard's formula can be even worse than
            // this for very small triangles, e.g. a triangle with a true area of 1e-30
            // might evaluate to 1e-5.
            //
            // So, we prefer l'Huilier's formula unless dmin < s * (0.1 * E), where
            // dmin = Min(s-a, s-b, s-c). This basically includes all triangles
            // except for extremely long and skinny ones.
            //
            // Since we don't know E, we would like a conservative upper bound on
            // the triangle area in terms of s and dmin. It's possible to show that
            // E <= k1 * s * sqrt(s * dmin), where k1 = 2*sqrt(3)/Pi (about 1).
            // Using this, it's easy to show that we should always use l'Huilier's
            // method if dmin >= k2 * s^5, where k2 is about 1e-2. Furthermore,
            // if dmin < k2 * s^5, the triangle area is at most k3 * s^4, where
            // k3 is about 0.1. Since the best case error using Girard's formula
            // is about 1e-15, this means that we shouldn't even consider it unless
            // s >= 3e-4 or so.

            // We use volatile doubles to force the compiler to truncate all of these
            // quantities to 64 bits. Otherwise it may compute a value of dmin > 0
            // simply because it chose to spill one of the intermediate values to
            // memory but not one of the others.
            var sa = b.Angle(c);
            var sb = c.Angle(a);
            var sc = a.Angle(b);
            var s = 0.5 * (sa + sb + sc);
            if (s >= 3e-4)
            {
                // Consider whether Girard's formula might be more accurate.
                var s2 = s * s;
                var dmin = s - Math.Max(sa, Math.Max(sb, sc));
                if (dmin < 1e-2 * s * s2 * s2)
                {
                    // This triangle is skinny enough to consider Girard's formula.
                    var area = GirardArea(a, b, c);
                    if (dmin < s * (0.1 * area))
                    {
                        return area;
                    }
                }
            }
            // Use l'Huilier's formula.
            return 4
                   * Math.Atan(
                       Math.Sqrt(
                           Math.Max(0.0,
                                    Math.Tan(0.5 * s) * Math.Tan(0.5 * (s - sa)) * Math.Tan(0.5 * (s - sb))
                                    * Math.Tan(0.5 * (s - sc)))));
        }

        /**
   * Return the area of the triangle computed using Girard's formula. This is
   * slightly faster than the Area() method above is not accurate for very small
   * triangles.
   */

        public static double GirardArea(S2Point a, S2Point b, S2Point c)
        {
            // This is equivalent to the usual Girard's formula but is slightly
            // more accurate, faster to compute, and handles a == b == c without
            // a special case.

            var ab = S2Point.CrossProd(a, b);
            var bc = S2Point.CrossProd(b, c);
            var ac = S2Point.CrossProd(a, c);
            return Math.Max(0.0, ab.Angle(ac) - ab.Angle(bc) + bc.Angle(ac));
        }

        /**
   * Like Area(), but returns a positive value for counterclockwise triangles
   * and a negative value otherwise.
   */

        public static double SignedArea(S2Point a, S2Point b, S2Point c)
        {
            return Area(a, b, c) * RobustCcw(a, b, c);
        }

        // About centroids:
        // ----------------
        //
        // There are several notions of the "centroid" of a triangle. First, there
        // // is the planar centroid, which is simply the centroid of the ordinary
        // (non-spherical) triangle defined by the three vertices. Second, there is
        // the surface centroid, which is defined as the intersection of the three
        // medians of the spherical triangle. It is possible to show that this
        // point is simply the planar centroid projected to the surface of the
        // sphere. Finally, there is the true centroid (mass centroid), which is
        // defined as the area integral over the spherical triangle of (x,y,z)
        // divided by the triangle area. This is the point that the triangle would
        // rotate around if it was spinning in empty space.
        //
        // The best centroid for most purposes is the true centroid. Unlike the
        // planar and surface centroids, the true centroid behaves linearly as
        // regions are added or subtracted. That is, if you split a triangle into
        // pieces and compute the average of their centroids (weighted by triangle
        // area), the result equals the centroid of the original triangle. This is
        // not true of the other centroids.
        //
        // Also note that the surface centroid may be nowhere near the intuitive
        // "center" of a spherical triangle. For example, consider the triangle
        // with vertices A=(1,eps,0), B=(0,0,1), C=(-1,eps,0) (a quarter-sphere).
        // The surface centroid of this triangle is at S=(0, 2*eps, 1), which is
        // within a distance of 2*eps of the vertex B. Note that the median from A
        // (the segment connecting A to the midpoint of BC) passes through S, since
        // this is the shortest path connecting the two endpoints. On the other
        // hand, the true centroid is at M=(0, 0.5, 0.5), which when projected onto
        // the surface is a much more reasonable interpretation of the "center" of
        // this triangle.

        /**
   * Return the centroid of the planar triangle ABC. This can be normalized to
   * unit length to obtain the "surface centroid" of the corresponding spherical
   * triangle, i.e. the intersection of the three medians. However, note that
   * for large spherical triangles the surface centroid may be nowhere near the
   * intuitive "center" (see example above).
   */

        public static S2Point PlanarCentroid(S2Point a, S2Point b, S2Point c)
        {
            return new S2Point((a.X + b.X + c.X) / 3.0, (a.Y + b.Y + c.Y) / 3.0, (a.Z + b.Z + c.Z) / 3.0);
        }

        /**
   * Returns the true centroid of the spherical triangle ABC multiplied by the
   * signed area of spherical triangle ABC. The reasons for multiplying by the
   * signed area are (1) this is the quantity that needs to be summed to compute
   * the centroid of a union or difference of triangles, and (2) it's actually
   * easier to calculate this way.
   */

        public static S2Point TrueCentroid(S2Point a, S2Point b, S2Point c)
        {
            // I couldn't find any references for computing the true centroid of a
            // spherical triangle... I have a truly marvellous demonstration of this
            // formula which this margin is too narrow to contain :)

            // assert (isUnitLength(a) && isUnitLength(b) && isUnitLength(c));
            var sina = S2Point.CrossProd(b, c).Norm;
            var sinb = S2Point.CrossProd(c, a).Norm;
            var sinc = S2Point.CrossProd(a, b).Norm;
            var ra = (sina == 0) ? 1 : (Math.Asin(sina) / sina);
            var rb = (sinb == 0) ? 1 : (Math.Asin(sinb) / sinb);
            var rc = (sinc == 0) ? 1 : (Math.Asin(sinc) / sinc);

            // Now compute a point M such that M.X = rX * det(ABC) / 2 for X in A,B,C.
            var x = new S2Point(a.X, b.X, c.X);
            var y = new S2Point(a.Y, b.Y, c.Y);
            var z = new S2Point(a.Z, b.Z, c.Z);
            var r = new S2Point(ra, rb, rc);
            return new S2Point(0.5 * S2Point.CrossProd(y, z).DotProd(r),
                               0.5 * S2Point.CrossProd(z, x).DotProd(r), 0.5 * S2Point.CrossProd(x, y).DotProd(r));
        }

        /**
   * Return true if the points A, B, C are strictly counterclockwise. Return
   * false if the points are clockwise or colinear (i.e. if they are all
   * contained on some great circle).
   *
   *  Due to numerical errors, situations may arise that are mathematically
   * impossible, e.g. ABC may be considered strictly CCW while BCA is not.
   * However, the implementation guarantees the following:
   *
   *  If SimpleCCW(a,b,c), then !SimpleCCW(c,b,a) for all a,b,c.
   *
   * In other words, ABC and CBA are guaranteed not to be both CCW
   */

        public static bool SimpleCcw(S2Point a, S2Point b, S2Point c)
        {
            // We compute the signed volume of the parallelepiped ABC. The usual
            // formula for this is (AxB).C, but we compute it here using (CxA).B
            // in order to ensure that ABC and CBA are not both CCW. This follows
            // from the following identities (which are true numerically, not just
            // mathematically):
            //
            // (1) x.CrossProd(y) == -(y.CrossProd(x))
            // (2) (-x).DotProd(y) == -(x.DotProd(y))

            return S2Point.CrossProd(c, a).DotProd(b) > 0;
        }

        /**
   * WARNING! This requires arbitrary precision arithmetic to be truly robust.
   * This means that for nearly colinear AB and AC, this function may return the
   * wrong answer.
   *
   * <p>
   * Like SimpleCCW(), but returns +1 if the points are counterclockwise and -1
   * if the points are clockwise. It satisfies the following conditions:
   *
   *  (1) RobustCCW(a,b,c) == 0 if and only if a == b, b == c, or c == a (2)
   * RobustCCW(b,c,a) == RobustCCW(a,b,c) for all a,b,c (3) RobustCCW(c,b,a)
   * ==-RobustCCW(a,b,c) for all a,b,c
   *
   *  In other words:
   *
   *  (1) The result is zero if and only if two points are the same. (2)
   * Rotating the order of the arguments does not affect the result. (3)
   * Exchanging any two arguments inverts the result.
   *
   *  This function is essentially like taking the sign of the determinant of
   * a,b,c, except that it has additional logic to make sure that the above
   * properties hold even when the three points are coplanar, and to deal with
   * the limitations of floating-point arithmetic.
   *
   *  Note: a, b and c are expected to be of unit length. Otherwise, the results
   * are undefined.
   */

        public static int RobustCcw(S2Point a, S2Point b, S2Point c)
        {
            return RobustCcw(a, b, c, S2Point.CrossProd(a, b));
        }

        /**
   * A more efficient version of RobustCCW that allows the precomputed
   * cross-product of A and B to be specified.
   *
   *  Note: a, b and c are expected to be of unit length. Otherwise, the results
   * are undefined
   */

        public static int RobustCcw(S2Point a, S2Point b, S2Point c, S2Point aCrossB)
        {
            // assert (isUnitLength(a) && isUnitLength(b) && isUnitLength(c));

            // There are 14 multiplications and additions to compute the determinant
            // below. Since all three points are normalized, it is possible to show
            // that the average rounding error per operation does not exceed 2**-54,
            // the maximum rounding error for an operation whose result magnitude is in
            // the range [0.5,1). Therefore, if the absolute value of the determinant
            // is greater than 2*14*(2**-54), the determinant will have the same sign
            // even if the arguments are rotated (which produces a mathematically
            // equivalent result but with potentially different rounding errors).
            const double kMinAbsValue = 1.6e-15; // 2 * 14 * 2**-54

            var det = aCrossB.DotProd(c);

            // Double-check borderline cases in debug mode.
            // assert ((Math.Abs(det) < kMinAbsValue) || (Math.Abs(det) > 1000 * kMinAbsValue)
            //    || (det * expensiveCCW(a, b, c) > 0));

            if (det > kMinAbsValue)
            {
                return 1;
            }

            if (det < -kMinAbsValue)
            {
                return -1;
            }

            return ExpensiveCcw(a, b, c);
        }

        /**
   * A relatively expensive calculation invoked by RobustCCW() if the sign of
   * the determinant is uncertain.
   */

        private static int ExpensiveCcw(S2Point a, S2Point b, S2Point c)
        {
            // Return zero if and only if two points are the same. This ensures (1).
            if (a.Equals(b) || b.Equals(c) || c.Equals(a))
            {
                return 0;
            }

            // Now compute the determinant in a stable way. Since all three points are
            // unit length and we know that the determinant is very close to zero, this
            // means that points are very nearly colinear. Furthermore, the most common
            // situation is where two points are nearly identical or nearly antipodal.
            // To get the best accuracy in this situation, it is important to
            // immediately reduce the magnitude of the arguments by computing either
            // A+B or A-B for each pair of points. Note that even if A and B differ
            // only in their low bits, A-B can be computed very accurately. On the
            // other hand we can't accurately represent an arbitrary linear combination
            // of two vectors as would be required for Gaussian elimination. The code
            // below chooses the vertex opposite the longest edge as the "origin" for
            // the calculation, and computes the different vectors to the other two
            // vertices. This minimizes the sum of the lengths of these vectors.
            //
            // This implementation is very stable numerically, but it still does not
            // return consistent results in all cases. For example, if three points are
            // spaced far apart from each other along a great circle, the sign of the
            // result will basically be random (although it will still satisfy the
            // conditions documented in the header file). The only way to return
            // consistent results in all cases is to compute the result using
            // arbitrary-precision arithmetic. I considered using the Gnu MP library,
            // but this would be very expensive (up to 2000 bits of precision may be
            // needed to store the intermediate results) and seems like overkill for
            // this problem. The MP library is apparently also quite particular about
            // compilers and compilation options and would be a pain to maintain.

            // We want to handle the case of nearby points and nearly antipodal points
            // accurately, so determine whether A+B or A-B is smaller in each case.
            double sab = (a.DotProd(b) > 0) ? -1 : 1;
            double sbc = (b.DotProd(c) > 0) ? -1 : 1;
            double sca = (c.DotProd(a) > 0) ? -1 : 1;
            var vab = a + (b * sab);
            var vbc = b + (c * sbc);
            var vca = c + (a * sca);
            var dab = vab.Norm2;
            var dbc = vbc.Norm2;
            var dca = vca.Norm2;

            // Sort the difference vectors to find the longest edge, and use the
            // opposite vertex as the origin. If two difference vectors are the same
            // length, we break ties deterministically to ensure that the symmetry
            // properties guaranteed in the header file will be true.
            double sign;
            if (dca < dbc || (dca == dbc && a < b))
            {
                if (dab < dbc || (dab == dbc && a < c))
                {
                    // The "sab" factor converts A +/- B into B +/- A.
                    sign = S2Point.CrossProd(vab, vca).DotProd(a) * sab; // BC is longest
                    // edge
                }
                else
                {
                    sign = S2Point.CrossProd(vca, vbc).DotProd(c) * sca; // AB is longest
                    // edge
                }
            }
            else
            {
                if (dab < dca || (dab == dca && b < c))
                {
                    sign = S2Point.CrossProd(vbc, vab).DotProd(b) * sbc; // CA is longest
                    // edge
                }
                else
                {
                    sign = S2Point.CrossProd(vca, vbc).DotProd(c) * sca; // AB is longest
                    // edge
                }
            }
            if (sign > 0)
            {
                return 1;
            }
            if (sign < 0)
            {
                return -1;
            }

            // The points A, B, and C are numerically indistinguishable from coplanar.
            // This may be due to roundoff error, or the points may in fact be exactly
            // coplanar. We handle this situation by perturbing all of the points by a
            // vector (eps, eps**2, eps**3) where "eps" is an infinitesmally small
            // positive number (e.g. 1 divided by a googolplex). The perturbation is
            // done symbolically, i.e. we compute what would happen if the points were
            // perturbed by this amount. It turns out that this is equivalent to
            // checking whether the points are ordered CCW around the origin first in
            // the Y-Z plane, then in the Z-X plane, and then in the X-Y plane.

            var ccw =
                PlanarOrderedCcw(new R2Vector(a.Y, a.Z), new R2Vector(b.Y, b.Z), new R2Vector(c.Y, c.Z));
            if (ccw == 0)
            {
                ccw =
                    PlanarOrderedCcw(new R2Vector(a.Z, a.X), new R2Vector(b.Z, b.X), new R2Vector(c.Z, c.X));
                if (ccw == 0)
                {
                    ccw = PlanarOrderedCcw(
                        new R2Vector(a.X, a.Y), new R2Vector(b.X, b.Y), new R2Vector(c.X, c.Y));
                    // assert (ccw != 0);
                }
            }
            return ccw;
        }


        public static int PlanarCcw(R2Vector a, R2Vector b)
        {
            // Return +1 if the edge AB is CCW around the origin, etc.
            double sab = (a.DotProd(b) > 0) ? -1 : 1;
            var vab = a + (b * sab);
            var da = a.Norm2;
            var db = b.Norm2;
            double sign;
            if (da < db || (da == db && a < b))
            {
                sign = a.CrossProd(vab) * sab;
            }
            else
            {
                sign = vab.CrossProd(b);
            }
            if (sign > 0)
            {
                return 1;
            }
            if (sign < 0)
            {
                return -1;
            }
            return 0;
        }

        public static int PlanarOrderedCcw(R2Vector a, R2Vector b, R2Vector c)
        {
            var sum = 0;
            sum += PlanarCcw(a, b);
            sum += PlanarCcw(b, c);
            sum += PlanarCcw(c, a);
            if (sum > 0)
            {
                return 1;
            }
            if (sum < 0)
            {
                return -1;
            }
            return 0;
        }

        /**
   * Return true if the edges OA, OB, and OC are encountered in that order while
   * sweeping CCW around the point O. You can think of this as testing whether
   * A <= B <= C with respect to a continuous CCW ordering around O.
   *
   * Properties:
   * <ol>
   *   <li>If orderedCCW(a,b,c,o) && orderedCCW(b,a,c,o), then a == b</li>
   *   <li>If orderedCCW(a,b,c,o) && orderedCCW(a,c,b,o), then b == c</li>
   *   <li>If orderedCCW(a,b,c,o) && orderedCCW(c,b,a,o), then a == b == c</li>
   *   <li>If a == b or b == c, then orderedCCW(a,b,c,o) is true</li>
   *   <li>Otherwise if a == c, then orderedCCW(a,b,c,o) is false</li>
   * </ol>
   */

        public static bool OrderedCcw(S2Point a, S2Point b, S2Point c, S2Point o)
        {
            // The last inequality below is ">" rather than ">=" so that we return true
            // if A == B or B == C, and otherwise false if A == C. Recall that
            // RobustCCW(x,y,z) == -RobustCCW(z,y,x) for all x,y,z.

            var sum = 0;
            if (RobustCcw(b, o, a) >= 0)
            {
                ++sum;
            }
            if (RobustCcw(c, o, b) >= 0)
            {
                ++sum;
            }
            if (RobustCcw(a, o, c) > 0)
            {
                ++sum;
            }
            return sum >= 2;
        }

        /**
   * Return the angle at the vertex B in the triangle ABC. The return value is
   * always in the range [0, Pi]. The points do not need to be normalized.
   * Ensures that Angle(a,b,c) == Angle(c,b,a) for all a,b,c.
   *
   *  The angle is undefined if A or C is diametrically opposite from B, and
   * becomes numerically unstable as the length of edge AB or BC approaches 180
   * degrees.
   */

        public static double Angle(S2Point a, S2Point b, S2Point c)
        {
            return S2Point.CrossProd(a, b).Angle(S2Point.CrossProd(c, b));
        }

        /**
   * Return the exterior angle at the vertex B in the triangle ABC. The return
   * value is positive if ABC is counterclockwise and negative otherwise. If you
   * imagine an ant walking from A to B to C, this is the angle that the ant
   * turns at vertex B (positive = left, negative = right). Ensures that
   * TurnAngle(a,b,c) == -TurnAngle(c,b,a) for all a,b,c.
   *
   * @param a
   * @param b
   * @param c
   * @return the exterior angle at the vertex B in the triangle ABC
   */

        public static double TurnAngle(S2Point a, S2Point b, S2Point c)
        {
            // This is a bit less efficient because we compute all 3 cross products, but
            // it ensures that turnAngle(a,b,c) == -turnAngle(c,b,a) for all a,b,c.
            var outAngle = S2Point.CrossProd(b, a).Angle(S2Point.CrossProd(c, b));
            return (RobustCcw(a, b, c) > 0) ? outAngle : -outAngle;
        }

        /**
   * Return true if two points are within the given distance of each other
   * (mainly useful for testing).
   */

        public static bool ApproxEquals(S2Point a, S2Point b, double maxError)
        {
            return a.Angle(b) <= maxError;
        }

        public static bool ApproxEquals(S2Point a, S2Point b)
        {
            return ApproxEquals(a, b, 1e-15);
        }

        public static bool ApproxEquals(double a, double b, double maxError)
        {
            return Math.Abs(a - b) <= maxError;
        }

        public static bool ApproxEquals(double a, double b)
        {
            return ApproxEquals(a, b, 1e-15);
        }
    }

    public struct S2CellMetric
    {
        private readonly double _deriv;
        private readonly int _dim;

        /**
     * Defines a cell metric of the given dimension (1 == length, 2 == area).
     */

        public S2CellMetric(int dim, double deriv)
        {
            _deriv = deriv;
            _dim = dim;
        }

        /**
     * The "deriv" value of a metric is a derivative, and must be multiplied by
     * a length or area in (s,t)-space to get a useful value.
     */

        public double Deriv()
        {
            return _deriv;
        }

        /** Return the value of a metric for cells at the given level. */

        public double GetValue(int level)
        {
            return FpUtils.Scalb(_deriv, _dim * (1 - level));
        }

        /**
     * Return the level at which the metric has approximately the given value.
     * For example, S2::kAvgEdge.GetClosestLevel(0.1) returns the level at which
     * the average cell edge length is approximately 0.1. The return value is
     * always a valid level.
     */

        public int GetClosestLevel(double value)
        {
            return GetMinLevel(S2.Sqrt2 * value);
        }

        /**
     * Return the minimum level such that the metric is at most the given value,
     * or S2CellId::kMaxLevel if there is no such level. For example,
     * S2::kMaxDiag.GetMinLevel(0.1) returns the minimum level such that all
     * cell diagonal lengths are 0.1 or smaller. The return value is always a
     * valid level.
     */

        public int GetMinLevel(double value)
        {
            if (value <= 0)
            {
                return S2CellId.MaxLevel;
            }

            // This code is equivalent to computing a floating-point "level"
            // value and rounding up.
            var exponent = S2.Exp(value / ((1 << _dim) * _deriv));
            var level = Math.Max(0,
                                 Math.Min(S2CellId.MaxLevel, -((exponent - 1) >> (_dim - 1))));
            // assert (level == S2CellId.MAX_LEVEL || getValue(level) <= value);
            // assert (level == 0 || getValue(level - 1) > value);
            return level;
        }

        /**
     * Return the maximum level such that the metric is at least the given
     * value, or zero if there is no such level. For example,
     * S2.kMinWidth.GetMaxLevel(0.1) returns the maximum level such that all
     * cells have a minimum width of 0.1 or larger. The return value is always a
     * valid level.
     */

        public int GetMaxLevel(double value)
        {
            if (value <= 0)
            {
                return S2CellId.MaxLevel;
            }

            // This code is equivalent to computing a floating-point "level"
            // value and rounding down.
            var exponent = S2.Exp((1 << _dim) * _deriv / value);
            var level = Math.Max(0,
                                 Math.Min(S2CellId.MaxLevel, ((exponent - 1) >> (_dim - 1))));
            // assert (level == 0 || getValue(level) >= value);
            // assert (level == S2CellId.MAX_LEVEL || getValue(level + 1) < value);
            return level;
        }
    }
}