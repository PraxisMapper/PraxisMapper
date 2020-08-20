using System;

namespace Google.OpenLocationCode
{
    /// <summary>
    /// A square <see cref="GeoArea"/> for the coordinates of a decoded Open Location Code area.
    /// The <see cref="CodeLength"/> of the decoded Open Location Code is also included.
    /// </summary>
    public class CodeArea : GeoArea
    {

        internal CodeArea(double southLatitude, double westLongitude, double northLatitude, double eastLongitude, int codeLength) :
            base(southLatitude, westLongitude, northLatitude, eastLongitude)
        {
            if (southLatitude >= northLatitude || westLongitude >= eastLongitude)
            {
                throw new ArgumentException("min must be less than max");
            }

            CodeLength = codeLength;
        }

        /// <summary>
        /// Create a new copy of the provided CodeArea
        /// </summary>
        /// <param name="other">The other CodeArea to copy</param>
        public CodeArea(CodeArea other) : base(other)
        {
            CodeLength = other.CodeLength;
        }

        /// <summary>
        /// The length of the decoded Open Location Code.
        /// </summary>
        public int CodeLength { get; }

    }
}
