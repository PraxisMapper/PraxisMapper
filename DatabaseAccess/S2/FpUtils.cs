using System;
using System.Diagnostics;

namespace Google.Common.Geometry
{
    // Data from here
    //http://grepcode.com/file_/repository.grepcode.com/java/root/jdk/openjdk/6-b14/sun/misc/DoubleConsts.java/?v=source
    internal static class DoubleConsts
    {
        /**
     * The number of logical bits in the significand of a
     * <code>double</code> number, including the implicit bit.
     */
        public const int SignificandWidth = 53;

        /**
     * Maximum exponent a finite <code>double</code> number may have.
     * It is equal to the value returned by
     * <code>Math.ilogb(Double.MAX_VALUE)</code>.
     */
        public const int MaxExponent = 1023;

        /**
     * Minimum exponent a normalized <code>double</code> number may
     * have.  It is equal to the value returned by
     * <code>Math.ilogb(Double.MIN_NORMAL)</code>.
     */
        public const int MinExponent = -1022;

        /**
     * Bit mask to isolate the exponent field of a
     * <code>double</code>.
     */
        public const long ExpBitMask = 0x7FF0000000000000L;

        /**
     * Bias used in representing a <code>double</code> exponent.
     */
        public const int ExpBias = 1023;
    }

    // Data from here http://grepcode.com/file_/repository.grepcode.com/java/root/jdk/openjdk/6-b14/sun/misc/FpUtils.java/?v=source
    internal static class FpUtils
    {
        private static readonly double twoToTheDoubleScaleUp = PowerOfTwoD(512);
        private static readonly double twoToTheDoubleScaleDown = PowerOfTwoD(-512);

        /**
    * Returns a floating-point power of two in the normal range.
    */

        private static double PowerOfTwoD(int n)
        {
            Debug.Assert(n >= DoubleConsts.MinExponent && n <= DoubleConsts.MaxExponent);
            return BitConverter.Int64BitsToDouble((((long)n + (long)DoubleConsts.ExpBias) <<
                                                   (DoubleConsts.SignificandWidth - 1))
                                                  & DoubleConsts.ExpBitMask);
        }

        /**
    * Return <code>d</code> &times;
    * 2<sup><code>scale_factor</code></sup> rounded as if performed
    * by a single correctly rounded floating-point multiply to a
    * member of the double value set.  See <a
    * href="http://java.sun.com/docs/books/jls/second_edition/html/typesValues.doc.html#9208">&sect;4.2.3</a>
    * of the <a href="http://java.sun.com/docs/books/jls/html/">Java
    * Language Specification</a> for a discussion of floating-point
    * value sets.  If the exponent of the result is between the
    * <code>double</code>'s minimum exponent and maximum exponent,
    * the answer is calculated exactly.  If the exponent of the
    * result would be larger than <code>doubles</code>'s maximum
    * exponent, an infinity is returned.  Note that if the result is
    * subnormal, precision may be lost; that is, when <code>scalb(x,
    * n)</code> is subnormal, <code>scalb(scalb(x, n), -n)</code> may
    * not equal <i>x</i>.  When the result is non-NaN, the result has
    * the same sign as <code>d</code>.
    *
    *<p>
    * Special cases:
    * <ul>
    * <li> If the first argument is NaN, NaN is returned.
    * <li> If the first argument is infinite, then an infinity of the
    * same sign is returned.
    * <li> If the first argument is zero, then a zero of the same
    * sign is returned.
    * </ul>
    *
    * @param d number to be scaled by a power of two.
    * @param scale_factor power of 2 used to scale <code>d</code>
    * @return <code>d * </code>2<sup><code>scale_factor</code></sup>
    * @author Joseph D. Darcy
    */

        public static double Scalb(double d, int scaleFactor)
        {
            /*
         * This method does not need to be declared strictfp to
         * compute the same correct result on all platforms.  When
         * scaling up, it does not matter what order the
         * multiply-store operations are done; the result will be
         * finite or overflow regardless of the operation ordering.
         * However, to get the correct result when scaling down, a
         * particular ordering must be used.
         *
         * When scaling down, the multiply-store operations are
         * sequenced so that it is not possible for two consecutive
         * multiply-stores to return subnormal results.  If one
         * multiply-store result is subnormal, the next multiply will
         * round it away to zero.  This is done by first multiplying
         * by 2 ^ (scale_factor % n) and then multiplying several
         * times by by 2^n as needed where n is the exponent of number
         * that is a covenient power of two.  In this way, at most one
         * real rounding error occurs.  If the double value set is
         * being used exclusively, the rounding will occur on a
         * multiply.  If the double-extended-exponent value set is
         * being used, the products will (perhaps) be exact but the
         * stores to d are guaranteed to round to the double value
         * set.
         *
         * It is _not_ a valid implementation to first multiply d by
         * 2^MIN_EXPONENT and then by 2 ^ (scale_factor %
         * MIN_EXPONENT) since even in a strictfp program double
         * rounding on underflow could occur; e.g. if the scale_factor
         * argument was (MIN_EXPONENT - n) and the exponent of d was a
         * little less than -(MIN_EXPONENT - n), meaning the final
         * result would be subnormal.
         *
         * Since exact reproducibility of this method can be achieved
         * without any undue performance burden, there is no
         * compelling reason to allow double rounding on underflow in
         * scalb.
         */

            // magnitude of a power of two so large that scaling a finite
            // nonzero value by it would be guaranteed to over or
            // underflow; due to rounding, scaling down takes takes an
            // additional power of two which is reflected here
            const int MAX_SCALE = DoubleConsts.MaxExponent + -DoubleConsts.MinExponent +
                                  DoubleConsts.SignificandWidth + 1;
            var expAdjust = 0;
            var scaleIncrement = 0;
            var expDelta = Double.NaN;

            // Make sure scaling factor is in a reasonable range

            if (scaleFactor < 0)
            {
                scaleFactor = Math.Max(scaleFactor, -MAX_SCALE);
                scaleIncrement = -512;
                expDelta = twoToTheDoubleScaleDown;
            }
            else
            {
                scaleFactor = Math.Min(scaleFactor, MAX_SCALE);
                scaleIncrement = 512;
                expDelta = twoToTheDoubleScaleUp;
            }

            // Calculate (scale_factor % +/-512), 512 = 2^9, using
            // technique from "Hacker's Delight" section 10-2.
            var u = unchecked((uint)(scaleFactor >> 9 - 1));
            u = u >> 32 - 9;
            var t = unchecked((int)u);
            expAdjust = ((scaleFactor + t) & (512 - 1)) - t;

            d *= PowerOfTwoD(expAdjust);
            scaleFactor -= expAdjust;

            while (scaleFactor != 0)
            {
                d *= expDelta;
                scaleFactor -= scaleIncrement;
            }
            return d;
        }
    }
}