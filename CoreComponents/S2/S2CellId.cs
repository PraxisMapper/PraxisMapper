using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Google.Common.Geometry
{
    /**
     * An S2CellId is a 64-bit unsigned integer that uniquely identifies a cell in
     * the S2 cell decomposition. It has the following format:
     *
     * <pre>
     * id = [face][face_pos]
     * </pre>
     *
     * face: a 3-bit number (range 0..5) encoding the cube face.
     *
     * face_pos: a 61-bit number encoding the position of the center of this cell
     * along the Hilbert curve over this face (see the Wiki pages for details).
     *
     * Sequentially increasing cell ids follow a continuous space-filling curve over
     * the entire sphere. They have the following properties:
     *  - The id of a cell at level k consists of a 3-bit face number followed by k
     * bit pairs that recursively select one of the four children of each cell. The
     * next bit is always 1, and all other bits are 0. Therefore, the level of a
     * cell is determined by the position of its lowest-numbered bit that is turned
     * on (for a cell at level k, this position is 2 * (MAX_LEVEL - k).)
     *  - The id of a parent cell is at the midpoint of the range of ids spanned by
     * its children (or by its descendants at any level).
     *
     * Leaf cells are often used to represent points on the unit sphere, and this
     * class provides methods for converting directly between these two
     * representations. For cells that represent 2D regions rather than discrete
     * point, it is better to use the S2Cell class.
     *
     *
     */

    public struct S2CellId : IEquatable<S2CellId>, IComparable<S2CellId>
    {
        // Although only 60 bits are needed to represent the index of a leaf
        // cell, we need an extra bit in order to represent the position of
        // the center of the leaf cell along the Hilbert curve.
        internal const int FaceBits = 3;
        internal const int NumFaces = 6;
        internal const int MaxLevel = 30; // Valid levels: 0..MAX_LEVEL
        internal const int PosBits = 2 * MaxLevel + 1;
        internal const int MaxSize = 1 << MaxLevel;

        // Constant related to unsigned long's

        // The following lookup tables are used to convert efficiently between an
        // (i,j) cell index and the corresponding position along the Hilbert curve.
        // "lookup_pos" maps 4 bits of "i", 4 bits of "j", and 2 bits representing the
        // orientation of the current cell into 8 bits representing the order in which
        // that subcell is visited by the Hilbert curve, plus 2 bits indicating the
        // new orientation of the Hilbert curve within that subcell. (Cell
        // orientations are represented as combination of kSwapMask and kInvertMask.)
        //
        // "lookup_ij" is an inverted table used for mapping in the opposite
        // direction.
        //
        // We also experimented with looking up 16 bits at a time (14 bits of position
        // plus 2 of orientation) but found that smaller lookup tables gave better
        // performance. (2KB fits easily in the primary cache.)


        // Values for these constants are *declared* in the *.h file. Even though
        // the declaration specifies a value for the constant, that declaration
        // is not a *definition* of storage for the value. Because the values are
        // supplied in the declaration, we don't need the values here. Failing to
        // define storage causes link errors for any code that tries to take the
        // address of one of these values.
        private const int LookupBits = 4;
        private const int SwapMask = 0x01;
        private const int InvertMask = 0x02;
        private const ulong WrapOffset = (ulong)(NumFaces) << PosBits;

        private static readonly int[] LookupPos = new int[1 << (2 * LookupBits + 2)];
        private static readonly int[] LookupIj = new int[1 << (2 * LookupBits + 2)];

        private static readonly ulong[] MaxValueDivs =
        {
            0, 0, // 0 and 1 are invalid
            9223372036854775807L, 6148914691236517205L, 4611686018427387903L, // 2-4
            3689348814741910323L, 3074457345618258602L, 2635249153387078802L, // 5-7
            2305843009213693951L, 2049638230412172401L, 1844674407370955161L, // 8-10
            1676976733973595601L, 1537228672809129301L, 1418980313362273201L, // 11-13
            1317624576693539401L, 1229782938247303441L, 1152921504606846975L, // 14-16
            1085102592571150095L, 1024819115206086200L, 970881267037344821L, // 17-19
            922337203685477580L, 878416384462359600L, 838488366986797800L, // 20-22
            802032351030850070L, 768614336404564650L, 737869762948382064L, // 23-25
            709490156681136600L, 683212743470724133L, 658812288346769700L, // 26-28
            636094623231363848L, 614891469123651720L, 595056260442243600L, // 29-31
            576460752303423487L, 558992244657865200L, 542551296285575047L, // 32-34
            527049830677415760L, 512409557603043100L
        }; // 35-36

        // calculated as 0xffffffffffffffff % radix
        private static readonly int[] MaxValueMods =
        {
            0, 0, // 0 and 1 are invalid
            1, 0, 3, 0, 3, 1, 7, 6, 5, 4, 3, 2, 1, 0, 15, 0, 15, 16, 15, 15, // 2-21
            15, 5, 15, 15, 15, 24, 15, 23, 15, 15, 31, 15, 17, 15, 15
        }; // 22-36

        public static readonly S2CellId None = new S2CellId();

        /**
   * Returns an invalid cell id guaranteed to be larger than any valid cell id.
   * Useful for creating indexes.
   */

        public static readonly S2CellId Sentinel = new S2CellId(~0UL);

        /**
   * This is the offset required to wrap around from the beginning of the
   * Hilbert curve to the end or vice versa; see next_wrap() and prev_wrap().
   */

        /**
   * The id of the cell.
   */
        private readonly ulong _id;

        static S2CellId()
        {
            InitLookupCell(0, 0, 0, 0, 0, 0);
            InitLookupCell(0, 0, 0, SwapMask, 0, SwapMask);
            InitLookupCell(0, 0, 0, InvertMask, 0, InvertMask);
            InitLookupCell(0, 0, 0, SwapMask | InvertMask, 0, SwapMask | InvertMask);
        }

        public S2CellId(ulong id)
        {
            _id = id;
        }

        public ulong Id
        {
            get { return _id; }
        }

        /** Return true if id() represents a valid cell. */

        public bool IsValid
        {
            get { return Face < NumFaces && ((LowestOnBit & 0x1555555555555555UL) != 0); }
        }

        /** Which cube face this cell belongs to, in the range 0..5. */

        public int Face
        {
            get { return (int)(_id >> PosBits); }
        }

        /**
   * The position of the cell center along the Hilbert curve over this face, in
   * the range 0..(2**kPosBits-1).
   */

        public ulong Position
        {
            get { return (_id & (~0UL >> FaceBits)); }
        }

        /** Return the subdivision level of the cell (range 0..MAX_LEVEL). */

        public int Level
        {
            get
            {
                // Fast path for leaf cells.
                if (IsLeaf)
                {
                    return MaxLevel;
                }
                var x = (uint)(_id);
                var level = -1;
                if (x != 0)
                {
                    level += 16;
                }
                else
                {
                    x = (uint)(_id >> 32);
                }
                // We only need to look at even-numbered bits to determine the
                // level of a valid cell id.
                x &= (uint)-x; // Get lowest bit.
                if ((x & 0x00005555) != 0)
                {
                    level += 8;
                }
                if ((x & 0x00550055) != 0)
                {
                    level += 4;
                }
                if ((x & 0x05050505) != 0)
                {
                    level += 2;
                }
                if ((x & 0x11111111) != 0)
                {
                    level += 1;
                }
                // assert (level >= 0 && level <= MAX_LEVEL);
                return level;
            }
        }


        /**
   * Return true if this is a leaf cell (more efficient than checking whether
   * level() == MAX_LEVEL).
   */

        public bool IsLeaf
        {
            get { return (_id & 1) != 0; }
        }

        /**
   * Return true if this is a top-level face cell (more efficient than checking
   * whether level() == 0).
   */

        public bool IsFace
        {
            get { return (_id & (LowestOnBitForLevel(0) - 1)) == 0; }
        }

        public S2CellId RangeMin
        {
            get { return new S2CellId(_id - (LowestOnBit - 1)); }
        }

        public S2CellId RangeMax
        {
            get { return new S2CellId(_id + (LowestOnBit - 1)); }
        }

        public S2CellId Parent
        {
            get
            {
                // assert (isValid() && level() > 0);
                var newLsb = LowestOnBit << 2;

                // cast to long so we can flip the bits
                var i = (ulong)((long)_id & -(long)newLsb) | newLsb;

                return new S2CellId(i);
            }
        }

        public S2CellId ChildBegin
        {
            get
            {
                // assert (isValid() && level() < MAX_LEVEL);
                var oldLsb = LowestOnBit;
                return new S2CellId(_id - oldLsb + (oldLsb >> 2));
            }
        }

        public S2CellId ChildEnd
        {
            get
            {
                // assert (isValid() && level() < MAX_LEVEL);
                var oldLsb = LowestOnBit;
                return new S2CellId(_id + oldLsb + (oldLsb >> 2));
            }
        }

        public S2CellId Next
        {
            get { return new S2CellId(_id + (LowestOnBit << 1)); }
        }

        /**
   * Return the previous cell at the same level along the Hilbert curve. Works
   * correctly when advancing from one face to the next, but does *not* wrap
   * around from the last face to the first or vice versa.
   */

        public S2CellId Previous
        {
            get { return new S2CellId(_id - (LowestOnBit << 1)); }
        }


        /**
   * Like next(), but wraps around from the last face to the first and vice
   * versa. Should *not* be used for iteration in conjunction with
   * child_begin(), child_end(), Begin(), or End().
   */

        public S2CellId NextWithWrap
        {
            get
            {
                var n = Next;
                if (n._id < WrapOffset)
                {
                    return n;
                }
                return new S2CellId(n._id - WrapOffset);
            }
        }

        /**
   * Like prev(), but wraps around from the last face to the first and vice
   * versa. Should *not* be used for iteration in conjunction with
   * child_begin(), child_end(), Begin(), or End().
   */

        public S2CellId PreviousWithWrap
        {
            get
            {
                var p = Previous;
                if (p._id < WrapOffset)
                {
                    return p;
                }
                return new S2CellId(p._id + WrapOffset);
            }
        }

        public ulong LowestOnBit
        {
            get { return (ulong)((long)_id & -(long)_id); }
        }

        public int CompareTo(S2CellId other)
        {
            return _id.CompareTo(other._id);
        }

        public bool Equals(S2CellId other)
        {
            return _id == other._id;
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            return obj is S2CellId && Equals((S2CellId)obj);
        }

        public override int GetHashCode()
        {
            return _id.GetHashCode();
        }

        public static bool operator ==(S2CellId left, S2CellId right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(S2CellId left, S2CellId right)
        {
            return !left.Equals(right);
        }


        public static bool operator >(S2CellId x, S2CellId y)
        {
            return x._id > y._id;
        }

        public static bool operator <(S2CellId x, S2CellId y)
        {
            return x._id < y._id;
        }

        public static bool operator <=(S2CellId x, S2CellId y)
        {
            return x._id <= y._id;
        }

        public static bool operator >=(S2CellId x, S2CellId y)
        {
            return x._id >= y._id;
        }

        /** The default constructor returns an invalid cell id. */

        /**
   * Return a cell given its face (range 0..5), 61-bit Hilbert curve position
   * within that face, and level (range 0..MAX_LEVEL). The given position will
   * be modified to correspond to the Hilbert curve position at the center of
   * the returned cell. This is a static function rather than a constructor in
   * order to give names to the arguments.
   */

        public static S2CellId FromFacePosLevel(int face, ulong pos, int level)
        {
            return new S2CellId((((ulong)face) << PosBits) + (pos | 1)).ParentForLevel(level);
        }

        /**
   * Return the leaf cell containing the given point (a direction vector, not
   * necessarily unit length).
   */

        public static S2CellId FromPoint(S2Point p)
        {
            var face = S2Projections.XyzToFace(p);
            var uv = S2Projections.ValidFaceXyzToUv(face, p);
            var i = StToIj(S2Projections.UvToSt(uv.X));
            var j = StToIj(S2Projections.UvToSt(uv.Y));
            return FromFaceIj(face, i, j);
        }


        /** Return the leaf cell containing the given S2LatLng. */

        public static S2CellId FromLatLng(S2LatLng ll)
        {
            return FromPoint(ll.ToPoint());
        }

        public S2Point ToPoint()
        {
            return S2Point.Normalize(ToPointRaw());
        }

        /**
   * Return the direction vector corresponding to the center of the given cell.
   * The vector returned by ToPointRaw is not necessarily unit length.
   */

        public S2Point ToPointRaw()
        {
            // First we compute the discrete (i,j) coordinates of a leaf cell contained
            // within the given cell. Given that cells are represented by the Hilbert
            // curve position corresponding at their center, it turns out that the cell
            // returned by ToFaceIJOrientation is always one of two leaf cells closest
            // to the center of the cell (unless the given cell is a leaf cell itself,
            // in which case there is only one possibility).
            //
            // Given a cell of size s >= 2 (i.e. not a leaf cell), and letting (imin,
            // jmin) be the coordinates of its lower left-hand corner, the leaf cell
            // returned by ToFaceIJOrientation() is either (imin + s/2, jmin + s/2)
            // (imin + s/2 - 1, jmin + s/2 - 1). We can distinguish these two cases by
            // looking at the low bit of "i" or "j". In the first case the low bit is
            // zero, unless s == 2 (i.e. the level just above leaf cells) in which case
            // the low bit is one.
            //
            // The following calculation converts (i,j) to the (si,ti) coordinates of
            // the cell center. (We need to multiply the coordinates by a factor of 2
            // so that the center of leaf cells can be represented exactly.)

            var i = 0;
            var j = 0;
            int? notUsed = null;
            var face = ToFaceIjOrientation(ref i, ref j, ref notUsed);
            // System.out.println("i= " + i.intValue() + " j = " + j.intValue());
            var delta = IsLeaf ? 1 : (((i ^ ((int)_id) >> 2) & 1) != 0)
                                         ? 2 : 0;
            var si = (i << 1) + delta - MaxSize;
            var ti = (j << 1) + delta - MaxSize;
            return FaceSiTiToXyz(face, si, ti);
        }

        /** Return the S2LatLng corresponding to the center of the given cell. */

        public S2LatLng ToLatLng()
        {
            return new S2LatLng(ToPointRaw());
        }


        /** The 64-bit unique identifier for this cell. */

        /**
   * Return the child position (0..3) of this cell's ancestor at the given
   * level, relative to its parent. The argument should be in the range
   * 1..MAX_LEVEL. For example, child_position(1) returns the position of this
   * cell's level-1 ancestor within its top-level face cell.
   */

        public int ChildPosition(int level)
        {
            return (int)(_id >> (2 * (MaxLevel - level) + 1)) & 3;
        }

        // Methods that return the range of cell ids that are contained
        // within this cell (including itself). The range is *inclusive*
        // (i.e. test using >= and <=) and the return values of both
        // methods are valid leaf cell ids.
        //
        // These methods should not be used for iteration. If you want to
        // iterate through all the leaf cells, call child_begin(MAX_LEVEL) and
        // child_end(MAX_LEVEL) instead.
        //
        // It would in fact be error-prone to define a range_end() method,
        // because (range_max().id() + 1) is not always a valid cell id, and the
        // iterator would need to be tested using "<" rather that the usual "!=".


        /** Return true if the given cell is contained within this one. */

        public bool Contains(S2CellId other)
        {
            // assert (isValid() && other.isValid());
            return other >= RangeMin && other <= RangeMax;
        }

        /** Return true if the given cell intersects this one. */

        public bool Intersects(S2CellId other)
        {
            // assert (isValid() && other.isValid());
            return other.RangeMin <= RangeMax && other.RangeMax >= RangeMin;
        }

        /**
   * Return the cell at the previous level or at the given level (which must be
   * less than or equal to the current level).
   */

        public S2CellId ParentForLevel(int level)
        {
            // assert (isValid() && level >= 0 && level <= this.level());
            var newLsb = LowestOnBitForLevel(level);
            var i = (ulong)((long)_id & -(long)newLsb) | newLsb;
            return new S2CellId(i);
        }

        public S2CellId Child(int position)
        {
            var newLsb = LowestOnBit >> 2;
            return new S2CellId(_id + (ulong)(2 * position + 1 - 4) * newLsb);
        }

        public S2CellId ChildBeginForLevel(int level)
        {
            // assert (isValid() && level >= this.level() && level <= MAX_LEVEL);
            return new S2CellId(_id - LowestOnBit + LowestOnBitForLevel(level));
        }

        public S2CellId ChildEndForLevel(int level)
        {
            // assert (isValid() && level >= this.level() && level <= MAX_LEVEL);
            return new S2CellId(_id + LowestOnBit + LowestOnBitForLevel(level));
        }

        // Iterator-style methods for traversing the immediate children of a cell or
        // all of the children at a given level (greater than or equal to the current
        // level). Note that the end value is exclusive, just like standard STL
        // iterators, and may not even be a valid cell id. You should iterate using
        // code like this:
        //
        // for(S2CellId c = id.childBegin(); !c.equals(id.childEnd()); c = c.next())
        // ...
        //
        // The convention for advancing the iterator is "c = c.next()", so be sure
        // to use 'equals()' in the loop guard, or compare 64-bit cell id's,
        // rather than "c != id.childEnd()".

        /**
   * Return the next cell at the same level along the Hilbert curve. Works
   * correctly when advancing from one face to the next, but does *not* wrap
   * around from the last face to the first or vice versa.
   */


        public static S2CellId Begin(int level)
        {
            return FromFacePosLevel(0, 0, 0).ChildBeginForLevel(level);
        }

        public static S2CellId End(int level)
        {
            return FromFacePosLevel(5, 0, 0).ChildEndForLevel(level);
        }


        /**
   * Decodes the cell id from a compact text string suitable for display or
   * indexing. Cells at lower levels (i.e. larger cells) are encoded into
   * fewer characters. The maximum token length is 16.
   *
   * @param token the token to decode
   * @return the S2CellId for that token
   * @throws NumberFormatException if the token is not formatted correctly
   */

        public static S2CellId FromToken(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
                throw new ArgumentNullException("token");

            if (token.Length > 16 || "X".Equals(token, StringComparison.OrdinalIgnoreCase))
            {
                return None;
            }

            ulong value = 0;
            for (var pos = 0; pos < 16; pos++)
            {
                var digit = 0;
                if (pos < token.Length)
                {
                    digit = GetIntegerValue(token[pos], 16); // Character.digit(token.charAt(pos), 16);
                    if (digit == -1)
                    {
                        throw new ArgumentException("token");
                    }
                    if (OverflowInParse(value, digit))
                    {
                        throw new ArgumentException("Too large for unsigned long: " + token, "token");
                    }
                }
                value = (value * 16) + (ulong)digit;
            }

            return new S2CellId(value);
        }


        private static int GetIntegerValue(char c, int radix)
        {
            var val = -1;
            if (char.IsDigit(c))
                val = (int)(c - '0');
            else if (char.IsLower(c))
                val = (int)(c - 'a') + 10;
            else if (char.IsUpper(c))
                val = (int)(c - 'A') + 10;
            if (val >= radix)
                val = -1;
            return val;
        }

        /**
   * Encodes the cell id to compact text strings suitable for display or indexing.
   * Cells at lower levels (i.e. larger cells) are encoded into fewer characters.
   * The maximum token length is 16.
   *
   * Simple implementation: convert the id to hex and strip trailing zeros. We
   * could use base-32 or base-64, but assuming the cells used for indexing
   * regions are at least 100 meters across (level 16 or less), the savings
   * would be at most 3 bytes (9 bytes hex vs. 6 bytes base-64).
   *
   * @return the encoded cell id
   */

        public string ToToken()
        {
            if (_id == 0)
            {
                return "X";
            }

            var hex = _id.ToString("x", CultureInfo.InvariantCulture);
            var sb = new StringBuilder(16);
            for (var i = hex.Length; i < 16; i++)
            {
                sb.Append('0');
            }
            sb.Append(hex);
            for (var len = 16; len > 0; len--)
            {
                if (sb[len - 1] != '0')
                {
                    return sb.ToString(0, len);
                }
            }

            throw new Exception("Shouldn't make it here");
        }

        /**
   * Returns true if (current * 10) + digit is a number too large to be
   * represented by an unsigned long.  This is useful for detecting overflow
   * while parsing a string representation of a number.
   */

        /**
   * Returns true if (current * radix) + digit is a number too large to be
   * represented by an unsigned long.  This is useful for detecting overflow
   * while parsing a string representation of a number.
   * Does not verify whether supplied radix is valid, passing an invalid radix
   * will give undefined results or an ArrayIndexOutOfBoundsException.
   */

        private static bool OverflowInParse(ulong current, int digit, int radix = 10)
        {
            if (current < MaxValueDivs[radix])
            {
                return false;
            }
            if (current > MaxValueDivs[radix])
            {
                return true;
            }
            // current == maxValueDivs[radix]
            return (digit > MaxValueMods[radix]);
        }

        // calculated as 0xffffffffffffffff / radix

        /**
   * Return the four cells that are adjacent across the cell's four edges.
   * Neighbors are returned in the order defined by S2Cell::GetEdge. All
   * neighbors are guaranteed to be distinct.
   */

        public IReadOnlyList<S2CellId> GetEdgeNeighbors()
        {
            var neighbors = new S2CellId[4];
            var i = 0;
            var j = 0;
            int? notUsed = null;

            var level = Level;
            var size = 1 << (MaxLevel - level);
            var face = ToFaceIjOrientation(ref i, ref j, ref notUsed);

            // Edges 0, 1, 2, 3 are in the S, E, N, W directions.
            neighbors[0] = FromFaceIjSame(face, i, j - size,
                                          j - size >= 0).ParentForLevel(level);
            neighbors[1] = FromFaceIjSame(face, i + size, j,
                                          i + size < MaxSize).ParentForLevel(level);
            neighbors[2] = FromFaceIjSame(face, i, j + size,
                                          j + size < MaxSize).ParentForLevel(level);
            neighbors[3] = FromFaceIjSame(face, i - size, j,
                                          i - size >= 0).ParentForLevel(level);

            return neighbors;
        }

        /**
   * Return the neighbors of closest vertex to this cell at the given level, by
   * appending them to "output". Normally there are four neighbors, but the
   * closest vertex may only have three neighbors if it is one of the 8 cube
   * vertices.
   *
   * Requires: level < this.evel(), so that we can determine which vertex is
   * closest (in particular, level == MAX_LEVEL is not allowed).
   */

        public void GetVertexNeighbors(int level, IList<S2CellId> output)
        {
            // "level" must be strictly less than this cell's level so that we can
            // determine which vertex this cell is closest to.
            // assert (level < this.level());
            var i = 0;
            var j = 0;
            int? notUsed = null;
            var face = ToFaceIjOrientation(ref i, ref j, ref notUsed);

            // Determine the i- and j-offsets to the closest neighboring cell in each
            // direction. This involves looking at the next bit of "i" and "j" to
            // determine which quadrant of this->parent(level) this cell lies in.
            var halfsize = 1 << (MaxLevel - (level + 1));
            var size = halfsize << 1;
            bool isame, jsame;
            int ioffset, joffset;
            if ((i & halfsize) != 0)
            {
                ioffset = size;
                isame = (i + size) < MaxSize;
            }
            else
            {
                ioffset = -size;
                isame = (i - size) >= 0;
            }
            if ((j & halfsize) != 0)
            {
                joffset = size;
                jsame = (j + size) < MaxSize;
            }
            else
            {
                joffset = -size;
                jsame = (j - size) >= 0;
            }

            output.Add(ParentForLevel(level));
            output
                .Add(FromFaceIjSame(face, i + ioffset, j, isame)
                         .ParentForLevel(level));
            output
                .Add(FromFaceIjSame(face, i, j + joffset, jsame)
                         .ParentForLevel(level));
            // If i- and j- edge neighbors are *both* on a different face, then this
            // vertex only has three neighbors (it is one of the 8 cube vertices).
            if (isame || jsame)
            {
                output.Add(FromFaceIjSame(face, i + ioffset,
                                          j + joffset, isame && jsame).ParentForLevel(level));
            }
        }

        /**
   * Append all neighbors of this cell at the given level to "output". Two cells
   * X and Y are neighbors if their boundaries intersect but their interiors do
   * not. In particular, two cells that intersect at a single point are
   * neighbors.
   *
   * Requires: nbr_level >= this->level(). Note that for cells adjacent to a
   * face vertex, the same neighbor may be appended more than once.
   */

        public void GetAllNeighbors(int nbrLevel, IList<S2CellId> output)
        {
            var i = 0;
            var j = 0;
            int? notUsed = null;
            var face = ToFaceIjOrientation(ref i, ref j, ref notUsed);

            // Find the coordinates of the lower left-hand leaf cell. We need to
            // normalize (i,j) to a known position within the cell because nbr_level
            // may be larger than this cell's level.
            var size = 1 << (MaxLevel - Level);
            i = (i & -size);
            j = (j & -size);

            var nbrSize = 1 << (MaxLevel - nbrLevel);
            // assert (nbrSize <= size);

            // We compute the N-S, E-W, and diagonal neighbors in one pass.
            // The loop test is at the end of the loop to avoid 32-bit overflow.
            for (var k = -nbrSize; ; k += nbrSize)
            {
                bool sameFace;
                if (k < 0)
                {
                    sameFace = (j + k >= 0);
                }
                else if (k >= size)
                {
                    sameFace = (j + k < MaxSize);
                }
                else
                {
                    sameFace = true;
                    // North and South neighbors.
                    output.Add(FromFaceIjSame(face, i + k,
                                              j - nbrSize, j - size >= 0).ParentForLevel(nbrLevel));
                    output.Add(FromFaceIjSame(face, i + k, j + size,
                                              j + size < MaxSize).ParentForLevel(nbrLevel));
                }
                // East, West, and Diagonal neighbors.
                output.Add(FromFaceIjSame(face, i - nbrSize,
                                          j + k, sameFace && i - size >= 0).ParentForLevel(
                                              nbrLevel));
                output.Add(FromFaceIjSame(face, i + size, j + k,
                                          sameFace && i + size < MaxSize).ParentForLevel(nbrLevel));
                if (k >= size)
                {
                    break;
                }
            }
        }

        // ///////////////////////////////////////////////////////////////////
        // Low-level methods.

        /**
   * Return a leaf cell given its cube face (range 0..5) and i- and
   * j-coordinates (see s2.h).
   */

        public static S2CellId FromFaceIj(int face, int i, int j)
        {
            // Optimization notes:
            // - Non-overlapping bit fields can be combined with either "+" or "|".
            // Generally "+" seems to produce better code, but not always.

            // gcc doesn't have very good code generation for 64-bit operations.
            // We optimize this by computing the result as two 32-bit integers
            // and combining them at the end. Declaring the result as an array
            // rather than local variables helps the compiler to do a better job
            // of register allocation as well. Note that the two 32-bits halves
            // get shifted one bit to the left when they are combined.
            //long[] n = {0, face << (POS_BITS - 33)};

            // Alternating faces have opposite Hilbert curve orientations; this
            // is necessary in order for all faces to have a right-handed
            // coordinate system.
            //   int bits = (face & SWAP_MASK);

            // Each iteration maps 4 bits of "i" and "j" into 8 bits of the Hilbert
            // curve position. The lookup table transforms a 10-bit key of the form
            // "iiiijjjjoo" to a 10-bit value of the form "ppppppppoo", where the
            // letters [ijpo] denote bits of "i", "j", Hilbert curve position, and
            // Hilbert curve orientation respectively.

            //for (int k = 7; k >= 0; --k) {
            //  bits = getBits(n, i, j, k, bits);
            //}

            //S2CellId s = new S2CellId((((n[1] << 32) + n[0]) << 1) + 1);
            //return s;

            var n = (ulong)face << (PosBits - 1);

            var bits = face & SwapMask;

            unchecked
            {
                for (var k = 7; k >= 0; k--)
                {
                    const int mask = (1 << LookupBits) - 1;
                    bits += ((i >> k * LookupBits) & mask) << (LookupBits + 2);
                    bits += ((j >> k * LookupBits) & mask) << 2;
                    bits = LookupPos[bits];
                    n |= (ulong)(bits >> 2) << (k * 2 * LookupBits);
                    bits &= (SwapMask | InvertMask);
                }
                return new S2CellId(n * 2 + 1);
            }
        }

        //private static int GetBits(IList<long> n, int i, int j, int k, int bits)
        //{
        //    var mask = (1 << LookupBits) - 1;
        //    bits += (((i >> (k*LookupBits)) & mask) << (LookupBits + 2));
        //    bits += (((j >> (k*LookupBits)) & mask) << 2);
        //    bits = LookupPos[bits];
        //    n[k >> 2] |= ((((long)bits) >> 2) << ((k & 3)*2*LookupBits));
        //    bits &= (SwapMask | InvertMask);
        //    return bits;
        //}


        /**
   * Return the (face, i, j) coordinates for the leaf cell corresponding to this
   * cell id. Since cells are represented by the Hilbert curve position at the
   * center of the cell, the returned (i,j) for non-leaf cells will be a leaf
   * cell adjacent to the cell center. If "orientation" is non-NULL, also return
   * the Hilbert curve orientation for the current cell.
   */

        public int ToFaceIjOrientation(ref int pi, ref int pj,
                                       ref int? orientation)
        {
            // System.out.println("Entering toFaceIjorientation");
            var face = Face;
            var bits = (face & SwapMask);

            // System.out.println("face = " + face + " bits = " + bits);

            // Each iteration maps 8 bits of the Hilbert curve position into
            // 4 bits of "i" and "j". The lookup table transforms a key of the
            // form "ppppppppoo" to a value of the form "iiiijjjjoo", where the
            // letters [ijpo] represents bits of "i", "j", the Hilbert curve
            // position, and the Hilbert curve orientation respectively.
            //
            // On the first iteration we need to be careful to clear out the bits
            // representing the cube face.
            for (var k = 7; k >= 0; --k)
            {
                bits = GetBits1(ref pi, ref pj, k, bits);
                // System.out.println("pi = " + pi + " pj= " + pj + " bits = " + bits);
            }

            if (orientation != null)
            {
                // The position of a non-leaf cell at level "n" consists of a prefix of
                // 2*n bits that identifies the cell, followed by a suffix of
                // 2*(MAX_LEVEL-n)+1 bits of the form 10*. If n==MAX_LEVEL, the suffix is
                // just "1" and has no effect. Otherwise, it consists of "10", followed
                // by (MAX_LEVEL-n-1) repetitions of "00", followed by "0". The "10" has
                // no effect, while each occurrence of "00" has the effect of reversing
                // the kSwapMask bit.
                // assert (S2.POS_TO_ORIENTATION[2] == 0);
                // assert (S2.POS_TO_ORIENTATION[0] == S2.SWAP_MASK);
                if ((LowestOnBit & 0x1111111111111110L) != 0)
                {
                    bits ^= S2.SwapMask;
                }
                orientation = bits;
            }
            return face;
        }

        private int GetBits1(ref int i, ref int j, int k, int bits)
        {
            var nbits = (k == 7) ? (MaxLevel - 7 * LookupBits) : LookupBits;

            bits += unchecked((((int)(_id >> (k * 2 * LookupBits + 1)) &
                                 ((1 << (2 * nbits)) - 1))) << 2);
            /*
     * System.out.println("id is: " + id_); System.out.println("bits is " +
     * bits); System.out.println("lookup_ij[bits] is " + lookup_ij[bits]);
     */
            bits = LookupIj[bits];
            i = i + ((bits >> (LookupBits + 2)) << (k * LookupBits));
            /*
     * System.out.println("left is " + ((bits >> 2) & ((1 << kLookupBits) -
     * 1))); System.out.println("right is " + (k * kLookupBits));
     * System.out.println("j is: " + j.intValue()); System.out.println("addition
     * is: " + ((((bits >> 2) & ((1 << kLookupBits) - 1))) << (k *
     * kLookupBits)));
     */
            j = j + ((((bits >> 2) & ((1 << LookupBits) - 1))) << (k * LookupBits));
            bits &= (SwapMask | InvertMask);
            return bits;
        }

        /** Return the lowest-numbered bit that is on for cells at the given level. */

        /**
   * Return the lowest-numbered bit that is on for this cell id, which is equal
   * to (uint64(1) << (2 * (MAX_LEVEL - level))). So for example, a.lsb() <=
   * b.lsb() if and only if a.level() >= b.level(), but the first test is more
   * efficient.
   */

        public static ulong LowestOnBitForLevel(int level)
        {
            return 1UL << (2 * (MaxLevel - level));
        }


        /**
   * Return the i- or j-index of the leaf cell containing the given s- or
   * t-value.
   */

        private static int StToIj(double s)
        {
            // Converting from floating-point to integers via static_cast is very slow
            // on Intel processors because it requires changing the rounding mode.
            // Rounding to the nearest integer using FastIntRound() is much faster.

            const int m = MaxSize / 2; // scaling multiplier
            return (int)Math
                            .Max(0, Math.Min(2 * m - 1, Math.Round(m * s + (m - 0.5))));
        }

        /**
   * Convert (face, si, ti) coordinates (see s2.h) to a direction vector (not
   * necessarily unit length).
   */

        private static S2Point FaceSiTiToXyz(int face, int si, int ti)
        {
            const double kScale = 1.0 / MaxSize;
            var u = S2Projections.StToUv(kScale * si);
            var v = S2Projections.StToUv(kScale * ti);
            return S2Projections.FaceUvToXyz(face, u, v);
        }

        /**
   * Given (i, j) coordinates that may be out of bounds, normalize them by
   * returning the corresponding neighbor cell on an adjacent face.
   */

        private static S2CellId FromFaceIjWrap(int face, int i, int j)
        {
            // Convert i and j to the coordinates of a leaf cell just beyond the
            // boundary of this face. This prevents 32-bit overflow in the case
            // of finding the neighbors of a face cell, and also means that we
            // don't need to worry about the distinction between (s,t) and (u,v).
            i = Math.Max(-1, Math.Min(MaxSize, i));
            j = Math.Max(-1, Math.Min(MaxSize, j));

            // Find the (s,t) coordinates corresponding to (i,j). At least one
            // of these coordinates will be just outside the range [0, 1].
            const double kScale = 1.0 / MaxSize;
            var s = kScale * ((i << 1) + 1 - MaxSize);
            var t = kScale * ((j << 1) + 1 - MaxSize);

            // Find the leaf cell coordinates on the adjacent face, and convert
            // them to a cell id at the appropriate level.
            var p = S2Projections.FaceUvToXyz(face, s, t);
            face = S2Projections.XyzToFace(p);
            var st = S2Projections.ValidFaceXyzToUv(face, p);
            return FromFaceIj(face, StToIj(st.X), StToIj(st.Y));
        }

        /**
   * Public helper function that calls FromFaceIJ if sameFace is true, or
   * FromFaceIJWrap if sameFace is false.
   */

        public static S2CellId FromFaceIjSame(int face, int i, int j,
                                              bool sameFace)
        {
            if (sameFace)
            {
                return FromFaceIj(face, i, j);
            }
            else
            {
                return FromFaceIjWrap(face, i, j);
            }
        }

        public override string ToString()
        {
            return "(face=" + Face + ", pos=" + Position.ToString("x") + ", level="
                   + Level + ")";
        }

        private static void InitLookupCell(int level, int i, int j,
                                           int origOrientation, int pos, int orientation)
        {
            if (level == LookupBits)
            {
                var ij = (i << LookupBits) + j;
                LookupPos[(ij << 2) + origOrientation] = (pos << 2) + orientation;
                LookupIj[(pos << 2) + origOrientation] = (ij << 2) + orientation;
            }
            else
            {
                level++;
                i <<= 1;
                j <<= 1;
                pos <<= 2;
                // Initialize each sub-cell recursively.
                for (var subPos = 0; subPos < 4; subPos++)
                {
                    var ij = S2.PosToIj(orientation, subPos);
                    var orientationMask = S2.PosToOrientation(subPos);
                    InitLookupCell(level, i + (ij >> 1), j + (ij & 1), origOrientation, pos + subPos, orientation ^ orientationMask);
                }
            }
        }
    }
}