using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PraxisCore.Support
{
    /// <summary>
    /// Random Number Generator based on Mersenne-Twister algorithm
    /// 
    /// Usage : 
    ///    RandomNumberGenerator.Instance.Generate());
    ///    RandomNumberGenerator.Instance.Generate(1.1,2.2);
    ///    RandomNumberGenerator.Instance.Generate(1,100)
    /// 
    /// inspired from : http://www.math.sci.hiroshima-u.ac.jp/~m-mat/MT/VERSIONS/C-LANG/980409/mt19937-2.c
    /// </summary>
    public class RandomNumberGenerator
    {
        #region constants
        private static readonly int N = 624;
        private static readonly int M = 397;
        private readonly UInt32 MATRIX_A = 0x9908b0df;

        /// <summary>
        /// most significant w-r bits
        /// </summary>
        private readonly UInt32 UPPER_MASK = 0x80000000;

        /// <summary>
        /// least significant r bits
        /// </summary>
        private readonly UInt32 LOWER_MASK = 0x7fffffff;

        /// <summary>
        /// Tempering mask B
        /// </summary>
        private readonly UInt32 TEMPERING_MASK_B = 0x9d2c5680;

        /// <summary>
        /// Tempering mask C
        /// </summary>
        private readonly UInt32 TEMPERING_MASK_C = 0xefc60000;

        /// <summary>
        /// Last constant used for generation
        /// </summary>
        private readonly double FINAL_CONSTANT = 2.3283064365386963e-10;
        #endregion
        
        public RandomNumberGenerator()
        {
            this.sgenrand(4327);
        }
        public RandomNumberGenerator(ulong seed)
        {
            this.sgenrand(seed);
        }

        #region helpers methods
        private ulong TEMPERING_SHIFT_U(ulong y)
        {
            return y >> 11;
        }

        private ulong TEMPERING_SHIFT_S(ulong y)
        {
            return y << 7;
        }

        private ulong TEMPERING_SHIFT_T(ulong y)
        {
            return y << 15;
        }

        private ulong TEMPERING_SHIFT_L(ulong y)
        {
            return y >> 18;
        }
        #endregion

        #region properties

        /// <summary>
        /// the array for the state vector
        /// </summary>
        private readonly ulong[] mt = new ulong[625];

        /// <summary>
        /// mti==N+1 means mt[N] is not initialized 
        /// </summary>
        private int mti = N + 1;
        #endregion

        #region engine
        /// <summary>
        /// setting initial seeds to mt[N] using
        /// the generator Line 25 of Table 1 in
        /// [KNUTH 1981, The Art of Computer Programming Vol. 2 (2nd Ed.), pp102] 
        /// </summary>
        /// <param name="seed"></param>
        private void sgenrand(ulong seed)
        {
            mt[0] = seed & 0xffffffff;
            for (mti = 1; mti < N; mti++)
                mt[mti] = (69069 * mt[mti - 1]) & 0xffffffff;
        }

        private double genrand()
        {
            ulong y;
            ulong[] mag01 = new ulong[2] { 0x0, MATRIX_A };
            /* mag01[x] = x * MATRIX_A  for x=0,1 */

            if (mti >= N)
            { /* generate N words at one time */
                int kk;

                if (mti == N + 1)   /* if sgenrand() has not been called, */
                    sgenrand(4357); /* a default initial seed is used   */

                for (kk = 0; kk < N - M; kk++)
                {
                    y = (mt[kk] & UPPER_MASK) | (mt[kk + 1] & LOWER_MASK);
                    mt[kk] = mt[kk + M] ^ (y >> 1) ^ mag01[y & 0x1];
                }
                for (; kk < N - 1; kk++)
                {
                    y = (mt[kk] & UPPER_MASK) | (mt[kk + 1] & LOWER_MASK);
                    mt[kk] = mt[kk + (M - N)] ^ (y >> 1) ^ mag01[y & 0x1];
                }
                y = (mt[N - 1] & UPPER_MASK) | (mt[0] & LOWER_MASK);
                mt[N - 1] = mt[M - 1] ^ (y >> 1) ^ mag01[y & 0x1];

                mti = 0;
            }

            y = mt[mti++];
            y ^= TEMPERING_SHIFT_U(y);
            y ^= TEMPERING_SHIFT_S(y) & TEMPERING_MASK_B;
            y ^= TEMPERING_SHIFT_T(y) & TEMPERING_MASK_C;
            y ^= TEMPERING_SHIFT_L(y);

            //reals: (0,1)-interval
            //return y; for integer generation
            return ((double)y * FINAL_CONSTANT);
        }
        #endregion

        #region public methods
        /// <summary>
        /// Generate a random number between 0 and 1
        /// </summary>
        /// <returns></returns>
        public double Generate()
        {
            return this.genrand();
        }

        /// <summary>
        /// Generate an int between two bounds
        /// </summary>
        /// <param name="lowerBound">The lower bound (inclusive)</param>
        /// <param name="higherBound">The higher bound (inclusive)</param>
        /// <returns></returns>
        public double Generate(int lowerBound, int higherBound)
        {
            if (higherBound < lowerBound)
            {
                return double.NaN;
            }
            return Convert.ToInt32(Math.Floor(this.Generate(lowerBound * 1.0d, higherBound * 1.0d)));
        }

        /// <summary>
        /// Generate a double between two bounds
        /// </summary>
        /// <param name="lowerBound">The lower bound (inclusive)</param>
        /// <param name="higherBound">The higher bound (inclusive)</param>
        /// <returns>The random num or NaN if higherbound is lower than lowerbound</returns>
        public double Generate(double lowerBound, double higherBound)
        {
            if (higherBound < lowerBound)
            {
                return double.NaN;
            }
            return (this.Generate() * (higherBound - lowerBound + 1)) + lowerBound;
        }
        #endregion
    }
}
