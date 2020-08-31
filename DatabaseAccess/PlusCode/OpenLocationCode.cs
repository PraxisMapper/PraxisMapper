using System;
using System.Text;

namespace Google.OpenLocationCode
{
    //Note: the original OpenLocationCode library is Apache-2.0 licensed. This C# library doesn't seem to explicitly declare that, but it likely is also covered by it.

    /// <summary>
    /// Convert locations to and from convenient codes known as Open Location Codes
    /// or <see href="https://plus.codes/">Plus Codes</see>
    /// <para>
    /// Open Location Codes are short, ~10 character codes that can be used instead of street
    /// addresses. The codes can be generated and decoded offline, and use a reduced character set that
    /// minimises the chance of codes including words.
    /// </para>
    /// The <see href="https://github.com/google/open-location-code/blob/master/API.txt">Open Location Code API</see>
    /// is implemented through the static methods:
    /// <list type="bullet">
    /// <item><see cref="IsValid(string)"/></item>
    /// <item><see cref="IsShort(string)"/></item>
    /// <item><see cref="IsFull(string)"/></item>
    /// <item><see cref="Encode(double, double, int)"/></item>
    /// <item><see cref="Decode(string)"/></item>
    /// <item><see cref="Shorten(string, double, double)"/></item>
    /// <item><see cref="ShortCode.RecoverNearest(string, double, double)"/></item>
    /// </list>
    /// Additionally an object type is provided which can be created using the constructors:
    /// <list type="bullet">
    /// <item><see cref="OpenLocationCode(string)"/></item>
    /// <item><see cref="OpenLocationCode(double, double, int)"/></item>
    /// <item><see cref="ShortCode(string)"/></item>
    /// </list>
    /// <example><code>
    /// OpenLocationCode code = new OpenLocationCode("7JVW52GR+2V");
    /// OpenLocationCode code = new OpenLocationCode(27.175063, 78.042188);
    /// OpenLocationCode code = new OpenLocationCode(27.175063, 78.042188, 11);
    /// OpenLocationCode.ShortCode shortCode = new OpenLocationCode.ShortCode("52GR+2V");
    /// </code></example>
    /// 
    /// With a code object you can invoke the various methods such as to shorten the code
    /// or decode the <see cref="CodeArea"/> coordinates.
    /// <example><code>
    /// OpenLocationCode.ShortCode shortCode = code.shorten(27.176, 78.05);
    /// OpenLocationCode recoveredCode = shortCode.recoverNearest(27.176, 78.05);
    /// 
    /// CodeArea codeArea = code.decode()
    /// </code></example>
    /// </summary>
    public sealed class OpenLocationCode
    {

        /// <summary>
        /// Provides a normal precision code, approximately 14x14 meters.<br/>
        /// Used to specify encoded code length (<see cref="Encode(double,double,int)"/>)
        /// </summary>
        public const int CodePrecisionNormal = 10;

        /// <summary>
        /// Provides an extra precision code length, approximately 2x3 meters.<br/>
        /// Used to specify encoded code length (<see cref="Encode(double,double,int)"/>)
        /// </summary>
        public const int CodePrecisionExtra = 11;


        // A separator used to break the code into two parts to aid memorability.
        private const char SeparatorCharacter = '+';

        // The number of characters to place before the separator.
        private const int SeparatorPosition = 8;

        // The character used to pad codes.
        private const char PaddingCharacter = '0';

        // The character set used to encode the digit values.
        internal const string CodeAlphabet = "23456789CFGHJMPQRVWX";

        // The base to use to convert numbers to/from.
        private const int EncodingBase = 20; // CodeAlphabet.Length;

        // The encoding base squared also rep
        private const int EncodingBaseSquared = EncodingBase * EncodingBase;

        // The maximum value for latitude in degrees.
        private const int LatitudeMax = 90;

        // The maximum value for longitude in degrees.
        private const int LongitudeMax = 180;

        // Maximum code length using just lat/lng pair encoding.
        private const int PairCodeLength = 10;

        // Number of digits in the grid coding section.
        private const int GridCodeLength = MaxDigitCount - PairCodeLength;

        // Maximum code length for any plus code
        private const int MaxDigitCount = 15;

        // Number of columns in the grid refinement method.
        private const int GridColumns = 4;

        // Number of rows in the grid refinement method.
        private const int GridRows = 5;

        // The maximum latitude digit value for the first grid layer
        private const int FirstLatitudeDigitValueMax = 8; // lat -> 90

        // The maximum longitude digit value for the first grid layer
        private const int FirstLongitudeDigitValueMax = 17; // lon -> 180


        private const long GridRowsMultiplier = 3125; // Pow(GridRows, GridCodeLength)

        private const long GridColumnsMultiplier = 1024; // Pow(GridColumns, GridCodeLength)

        // Value to multiple latitude degrees to convert it to an integer with the maximum encoding
        // precision. I.e. ENCODING_BASE**3 * GRID_ROWS**GRID_CODE_LENGTH
        private const long LatIntegerMultiplier = 8000 * GridRowsMultiplier;

        // Value to multiple longitude degrees to convert it to an integer with the maximum encoding
        // precision. I.e. ENCODING_BASE**3 * GRID_COLUMNS**GRID_CODE_LENGTH
        private const long LngIntegerMultiplier = 8000 * GridColumnsMultiplier;

        // Value of the most significant latitude digit after it has been converted to an integer.
        private const long LatMspValue = LatIntegerMultiplier * EncodingBaseSquared;

        // Value of the most significant longitude digit after it has been converted to an integer.
        private const long LngMspValue = LngIntegerMultiplier * EncodingBaseSquared;

        // The ASCII integer of the minimum digit character used as the offset for indexed code digits
        private static readonly int IndexedDigitValueOffset = CodeAlphabet[0]; // 50

        // The digit values indexed by the character ASCII integer for efficient lookup of a digit value by its character
        private static readonly int[] IndexedDigitValues = new int[CodeAlphabet[CodeAlphabet.Length - 1] - IndexedDigitValueOffset + 1]; // int[38]

        //These indicate the boundaries of a code cell by degrees.
        public static readonly double Precision10 = .000125;
        public static readonly double Precision8 = .0025;
        public static readonly double Precision6 = .05;

        static OpenLocationCode()
        {
            for (int i = 0, digitVal = 0; i < IndexedDigitValues.Length; i++)
            {
                int digitIndex = CodeAlphabet[digitVal] - IndexedDigitValueOffset;
                IndexedDigitValues[i] = (digitIndex == i) ? digitVal++ : -1;
            }
        }


        /// <summary>
        /// Creates an <see cref="OpenLocationCode"/> object for the provided full code (or <see cref="CodeDigits"/>).
        /// Use <see cref="ShortCode(string)"/> for short codes.
        /// </summary>
        /// <param name="code">A valid full Open Location Code or <see cref="CodeDigits"/></param>
        /// <exception cref="ArgumentException">If the code is null, not valid, or not full.</exception>
        public OpenLocationCode(string code)
        {
            if (code == null)
            {
                throw new ArgumentException("code cannot be null");
            }
            Code = NormalizeCode(code.ToUpper());
            if (!IsValidUpperCase(Code) || !IsCodeFull(Code))
            {
                throw new ArgumentException($"code '{code}' is not a valid full Open Location Code (or code digits).");
            }
            CodeDigits = TrimCode(Code);
        }

        /// <summary>
        /// Creates an <see cref="OpenLocationCode"/> object encoded from the provided latitude/longitude coordinates
        /// and having the provided code length (precision).
        /// </summary>
        /// <param name="latitude">The latitude coordinate in decimal degrees.</param>
        /// <param name="longitude">The longitude coordinate in decimal degrees.</param>
        /// <param name="codeLength">The number of digits in the code (Default: <see cref="CodePrecisionNormal"/>).</param>
        /// <exception cref="ArgumentException">If the code length is invalid (valid lengths: <c>4</c>, <c>6</c>, <c>8</c>, or <c>10+</c>).</exception>
        public OpenLocationCode(double latitude, double longitude, int codeLength = CodePrecisionNormal)
        {
            Code = Encode(latitude, longitude, codeLength);
            CodeDigits = TrimCode(Code);
        }

        /// <summary>
        /// Creates an <see cref="OpenLocationCode"/> object encoded from the provided geographic point coordinates
        /// with the provided code length.
        /// </summary>
        /// <param name="point">The geographic coordinate point.</param>
        /// <param name="codeLength">The desired number of digits in the code (Default: <see cref="CodePrecisionNormal"/>).</param>
        /// /// <exception cref="ArgumentException">If the code length is not valid.</exception>
        /// <remarks>Alternative to <see cref="OpenLocationCode(double, double, int)"/></remarks>
        public OpenLocationCode(GeoPoint point, int codeLength = CodePrecisionNormal) :
            this(point.Latitude, point.Longitude, codeLength)
        { }


        /// <summary>
        /// The code which is a valid full Open Location Code (plus code).
        /// </summary>
        /// <value>The string representation of the code.</value>
        public string Code { get; }

        /// <summary>
        /// The digits of the full code which excludes the separator '+' character and any padding '0' characters.
        /// This is useful to more concisely represent or encode a full Open Location Code
        /// since the code digits can be normalized back into a valid full code.
        /// </summary>
        /// <example>"8FWC2300+" -> "8FWC23", "8FWC2345+G6" -> "8FWC2345G6"</example>
        /// <value>The string representation of the code digits.</value>
        /// <remarks>This is a nonstandard code format.</remarks>
        public string CodeDigits { get; }


        /// <summary>
        /// Decodes this full Open Location Code into a <see cref="CodeArea"/> object
        /// encapsulating the latitude/longitude coordinates of the area bounding box.
        /// </summary>
        /// <returns>The decoded CodeArea for this Open Location Code.</returns>
        public CodeArea Decode()
        {
            return DecodeValid(CodeDigits);
        }


        /// <summary>
        /// Determines if this full Open Location Code is padded which is defined by <see cref="IsPadded(string)"/>.
        /// </summary>
        /// <returns><c>true</c>, if this Open Location Code is a padded, <c>false</c> otherwise.</returns>
        public bool IsPadded()
        {
            return IsCodePadded(Code);
        }


        /// <summary>
        /// Shorten this full Open Location Code by removing four or six digits (depending on the provided reference point).
        /// It removes as many digits as possible.
        /// </summary>
        /// <returns>A new <see cref="ShortCode"/> instance shortened from this Open Location Code.</returns>
        /// <param name="referenceLatitude">The reference latitude in decimal degrees.</param>
        /// <param name="referenceLongitude">The reference longitude in decimal degrees.</param>
        /// <exception cref="InvalidOperationException">If this code is padded (<see cref="IsPadded()"/>).</exception>
        /// <exception cref="ArgumentException">If the reference point is too far from this code's center point.</exception>
        public ShortCode Shorten(double referenceLatitude, double referenceLongitude)
        {
            return ShortenValid(Decode(), Code, referenceLatitude, referenceLongitude);
        }

        /// <summary>
        /// Shorten this full Open Location Code by removing four or six digits (depending on the provided reference point).
        /// It removes as many digits as possible.
        /// </summary>
        /// <returns>A new <see cref="ShortCode"/> instance shortened from this Open Location Code.</returns>
        /// <param name="referencePoint">The reference point coordinates</param>
        /// <exception cref="InvalidOperationException">If this code is padded (<see cref="IsPadded()"/>).</exception>
        /// <exception cref="ArgumentException">If the reference point is too far from this code's center point.</exception>
        /// <remarks>Convenient alternative to <see cref="Shorten(double, double)"/></remarks>
        public ShortCode Shorten(GeoPoint referencePoint)
        {
            return Shorten(referencePoint.Latitude, referencePoint.Longitude);
        }


        /// <inheritdoc />
        /// <summary>
        /// Determines whether the provided object is an OpenLocationCode with the same <see cref="Code"/> as this OpenLocationCode.
        /// </summary>
        public override bool Equals(object obj)
        {
            return this == obj || (obj is OpenLocationCode olc && olc.Code == Code);
        }

        /// <returns>The hashcode of the <see cref="Code"/> string.</returns>
        public override int GetHashCode()
        {
            return Code.GetHashCode();
        }

        /// <returns>The <see cref="Code"/> string.</returns>
        public override string ToString()
        {
            return Code;
        }


        // API Spec Implementation

        /// <summary>
        /// Determines if the provided string is a valid Open Location Code sequence.
        /// A valid Open Location Code can be either full or short (XOR).
        /// </summary>
        /// <returns><c>true</c>, if the provided code is a valid Open Location Code, <c>false</c> otherwise.</returns>
        /// <param name="code">The code string to check.</param>
        public static bool IsValid(string code)
        {
            return code != null && IsValidUpperCase(code.ToUpper());
        }

        private static bool IsValidUpperCase(string code)
        {
            if (code.Length < 2)
            {
                return false;
            }

            // There must be exactly one separator.
            int separatorIndex = code.IndexOf(SeparatorCharacter);
            if (separatorIndex == -1)
            {
                return false;
            }
            if (separatorIndex != code.LastIndexOf(SeparatorCharacter))
            {
                return false;
            }
            // There must be an even number of at most eight characters before the separator.
            if (separatorIndex % 2 != 0 || separatorIndex > SeparatorPosition)
            {
                return false;
            }

            // Check first two characters: only some values from the alphabet are permitted.
            if (separatorIndex == SeparatorPosition)
            {
                // First latitude character can only have first 9 values.
                if (CodeAlphabet.IndexOf(code[0]) > FirstLatitudeDigitValueMax)
                {
                    return false;
                }

                // First longitude character can only have first 18 values.
                if (CodeAlphabet.IndexOf(code[1]) > FirstLongitudeDigitValueMax)
                {
                    return false;
                }
            }

            // Check the characters before the separator.
            bool paddingStarted = false;
            for (int i = 0; i < separatorIndex; i++)
            {
                if (paddingStarted)
                {
                    // Once padding starts, there must not be anything but padding.
                    if (code[i] != PaddingCharacter)
                    {
                        return false;
                    }
                }
                else if (code[i] == PaddingCharacter)
                {
                    paddingStarted = true;
                    // Short codes cannot have padding
                    if (separatorIndex < SeparatorPosition)
                    {
                        return false;
                    }
                    // Padding can start on even character: 2, 4 or 6.
                    if (i != 2 && i != 4 && i != 6)
                    {
                        return false;
                    }
                }
                else if (CodeAlphabet.IndexOf(code[i]) == -1)
                {
                    return false; // Illegal character.
                }
            }

            // Check the characters after the separator.
            if (code.Length > separatorIndex + 1)
            {
                if (paddingStarted)
                {
                    return false;
                }
                // Only one character after separator is forbidden.
                if (code.Length == separatorIndex + 2)
                {
                    return false;
                }
                for (int i = separatorIndex + 1; i < code.Length; i++)
                {
                    if (CodeAlphabet.IndexOf(code[i]) == -1)
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        /// <summary>
        /// Determines if a code is a valid short Open Location Code.
        /// <para>
        /// A short Open Location Code is a sequence created by removing an even number
        /// of characters from the start of a full Open Location Code. Short codes must
        /// include the separator character and it must be before eight or less characters.
        /// </para>
        /// </summary>
        /// <returns><c>true</c>, if the provided code is a valid short Open Location Code, <c>false</c> otherwise.</returns>
        /// <param name="code">The code string to check.</param>
        public static bool IsShort(string code)
        {
            return IsValid(code) && IsCodeShort(code);
        }

        private static bool IsCodeShort(string code)
        {
            int separatorIndex = code.IndexOf(SeparatorCharacter);
            return separatorIndex >= 0 && separatorIndex < SeparatorPosition;
        }

        /// <summary>
        /// Determines if a code is a valid full Open Location Code.
        /// <para>
        /// Full codes must include the separator character and it must be after eight characters.
        /// </para>
        /// </summary>
        /// <returns><c>true</c>, if the provided code is a valid full Open Location Code, <c>false</c> otherwise.</returns>
        /// <param name="code">The code string to check.</param>
        public static bool IsFull(string code)
        {
            return IsValid(code) && IsCodeFull(code);
        }

        private static bool IsCodeFull(string code)
        {
            return code.IndexOf(SeparatorCharacter) == SeparatorPosition;
        }

        /// <summary>
        /// Determines if a code is a valid padded Open Location Code.
        /// <para>
        /// An Open Location Code is padded when it has only 2, 4, or 6 valid digits
        /// followed by zero <c>'0'</c> as padding to form a full 8 digit code.
        /// If this returns <c>true</c> that the code is padded, then it is also implied
        /// to be full since short codes cannot be padded.
        /// </para>
        /// </summary>
        /// <returns><c>true</c>, if the provided code is a valid padded Open Location Code, <c>false</c> otherwise.</returns>
        /// <param name="code">The code string to check.</param>
        /// <remarks>
        /// This is not apart of the API specification but it is useful to check if a code can
        /// <see cref="Shorten(string, double, double)"/> since padded codes cannot be shortened.
        /// </remarks>
        public static bool IsPadded(string code)
        {
            return IsValid(code) && IsCodePadded(code);
        }

        private static bool IsCodePadded(string code)
        {
            return code.IndexOf(PaddingCharacter) >= 0;
        }


        /// <summary>
        /// Encodes latitude/longitude coordinates into a full Open Location Code of the provided length.
        /// </summary>
        /// <returns>The encoded Open Location Code.</returns>
        /// <param name="latitude">The latitude in decimal degrees.</param>
        /// <param name="longitude">The longitude in decimal degrees.</param>
        /// <param name="codeLength">The number of digits in the code (Default: <see cref="CodePrecisionNormal"/>).</param>
        /// <exception cref="ArgumentException">If the code length is not valid.</exception>
        public static string Encode(double latitude, double longitude, int codeLength = CodePrecisionNormal)
        {
            // Limit the maximum number of digits in the code.
            codeLength = Math.Min(codeLength, MaxDigitCount);
            // Check that the code length requested is valid.
            if (codeLength < 2 || (codeLength < PairCodeLength && codeLength % 2 == 1))
            {
                throw new ArgumentException($"Illegal code length {codeLength}.");
            }
            // Ensure that latitude and longitude are valid.
            latitude = ClipLatitude(latitude);
            longitude = NormalizeLongitude(longitude);

            // Latitude 90 needs to be adjusted to be just less, so the returned code can also be decoded.
            if ((int)latitude == LatitudeMax)
            {
                latitude -= 0.9 * ComputeLatitudePrecision(codeLength);
            }

            // Store the code - we build it in reverse and reorder it afterwards.
            StringBuilder reverseCodeBuilder = new StringBuilder();

            // Compute the code.
            // This approach converts each value to an integer after multiplying it by
            // the final precision. This allows us to use only integer operations, so
            // avoiding any accumulation of floating point representation errors.

            // Multiply values by their precision and convert to positive. Rounding
            // avoids/minimises errors due to floating point precision.
            long latVal = (long)(Math.Round((latitude + LatitudeMax) * LatIntegerMultiplier * 1e6) / 1e6);
            long lngVal = (long)(Math.Round((longitude + LongitudeMax) * LngIntegerMultiplier * 1e6) / 1e6);

            if (codeLength > PairCodeLength)
            {
                for (int i = 0; i < GridCodeLength; i++)
                {
                    long latDigit = latVal % GridRows;
                    long lngDigit = lngVal % GridColumns;
                    int ndx = (int)(latDigit * GridColumns + lngDigit);
                    reverseCodeBuilder.Append(CodeAlphabet[ndx]);
                    latVal /= GridRows;
                    lngVal /= GridColumns;
                }
            }
            else
            {
                latVal /= GridRowsMultiplier;
                lngVal /= GridColumnsMultiplier;
            }
            // Compute the pair section of the code.
            for (int i = 0; i < PairCodeLength / 2; i++)
            {
                reverseCodeBuilder.Append(CodeAlphabet[(int)(lngVal % EncodingBase)]);
                reverseCodeBuilder.Append(CodeAlphabet[(int)(latVal % EncodingBase)]);
                latVal /= EncodingBase;
                lngVal /= EncodingBase;
                // If we are at the separator position, add the separator.
                if (i == 0)
                {
                    reverseCodeBuilder.Append(SeparatorCharacter);
                }
            }
            // Reverse the code.
            char[] reversedCode = reverseCodeBuilder.ToString().ToCharArray();
            Array.Reverse(reversedCode);
            StringBuilder codeBuilder = new StringBuilder(new string(reversedCode));

            // If we need to pad the code, replace some of the digits.
            if (codeLength < SeparatorPosition)
            {
                codeBuilder.Remove(codeLength, SeparatorPosition - codeLength);
                for (int i = codeLength; i < SeparatorPosition; i++)
                {
                    codeBuilder.Insert(i, PaddingCharacter);
                }
            }
            return codeBuilder.ToString(0, Math.Max(SeparatorPosition + 1, codeLength + 1));
        }

        /// <summary>
        /// Encodes geographic point coordinates into a full Open Location Code of the provided length.
        /// </summary>
        /// <returns>The encoded Open Location Code.</returns>
        /// <param name="point">The geographic point coordinates.</param>
        /// <param name="codeLength">The number of digits in the code (Default: <see cref="CodePrecisionNormal"/>).</param>
        /// <exception cref="ArgumentException">If the code length is not valid.</exception>
        /// <remarks>Alternative too <see cref="Encode(double, double, int)"/></remarks>
        public static string Encode(GeoPoint point, int codeLength = CodePrecisionNormal)
        {
            return Encode(point.Latitude, point.Longitude, codeLength);
        }

        /// <summary>
        /// Decodes a full Open Location Code into a <see cref="CodeArea"/> object
        /// encapsulating the latitude/longitude coordinates of the area bounding box.
        /// </summary>
        /// <returns>The decoded CodeArea for the given location code.</returns>
        /// <param name="code">The Open Location Code to be decoded.</param>
        /// <exception cref="ArgumentException">If the code is not valid or not full.</exception>
        public static CodeArea Decode(string code)
        {
            code = ValidateCode(code);
            if (!IsCodeFull(code))
            {
                throw new ArgumentException($"{nameof(Decode)}(code: {code}) - code cannot be short.");
            }
            return DecodeValid(TrimCode(code));
        }

        public static CodeArea DecodeValid(string codeDigits)
        {
            // Initialise the values. We work them out as integers and convert them to doubles at the end.
            long latVal = -LatitudeMax * LatIntegerMultiplier;
            long lngVal = -LongitudeMax * LngIntegerMultiplier;
            // Define the place value for the digits. We'll divide this down as we work through the code.
            long latPlaceVal = LatMspValue;
            long lngPlaceVal = LngMspValue;

            int pairPartLength = Math.Min(codeDigits.Length, PairCodeLength);
            int codeLength = Math.Min(codeDigits.Length, MaxDigitCount);
            for (int i = 0; i < pairPartLength; i += 2)
            {
                latPlaceVal /= EncodingBase;
                lngPlaceVal /= EncodingBase;
                latVal += DigitValueOf(codeDigits[i]) * latPlaceVal;
                lngVal += DigitValueOf(codeDigits[i + 1]) * lngPlaceVal;
            }
            for (int i = PairCodeLength; i < codeLength; i++)
            {
                latPlaceVal /= GridRows;
                lngPlaceVal /= GridColumns;
                int digit = DigitValueOf(codeDigits[i]);
                int row = digit / GridColumns;
                int col = digit % GridColumns;
                latVal += row * latPlaceVal;
                lngVal += col * lngPlaceVal;
            }
            return new CodeArea(
                (double)latVal / LatIntegerMultiplier,
                (double)lngVal / LngIntegerMultiplier,
                (double)(latVal + latPlaceVal) / LatIntegerMultiplier,
                (double)(lngVal + lngPlaceVal) / LngIntegerMultiplier,
                codeLength
            );
        }

        /// <summary>
        /// Shorten a full Open Location Code by removing four or six digits (depending on the provided reference point).
        /// It removes as many digits as possible.
        /// </summary>
        /// <returns>A new <see cref="ShortCode"/> instance shortened from the the provided Open Location Code.</returns>
        /// <param name="code">The Open Location Code to shorten.</param>
        /// <param name="referenceLatitude">The reference latitude in decimal degrees.</param>
        /// <param name="referenceLongitude">The reference longitude in decimal degrees.</param>
        /// <exception cref="ArgumentException">If the code is not valid, not full, or is padded.</exception>
        /// <exception cref="ArgumentException">If the reference point is too far from the code's center point.</exception>
        public static ShortCode Shorten(string code, double referenceLatitude, double referenceLongitude)
        {
            code = ValidateCode(code);
            if (!IsCodeFull(code))
            {
                throw new ArgumentException($"{nameof(Shorten)}(code: \"{code}\") - code cannot be short.");
            }
            if (IsCodePadded(code))
            {
                throw new ArgumentException($"{nameof(Shorten)}(code: \"{code}\") - code cannot be padded.");
            }
            return ShortenValid(Decode(code), code, referenceLatitude, referenceLongitude);
        }

        private static ShortCode ShortenValid(CodeArea codeArea, string code, double referenceLatitude, double referenceLongitude)
        {
            GeoPoint center = codeArea.Center;
            double range = Math.Max(
                Math.Abs(referenceLatitude - center.Latitude),
                Math.Abs(referenceLongitude - center.Longitude)
            );
            // We are going to check to see if we can remove three pairs, two pairs or just one pair of
            // digits from the code.
            for (int i = 4; i >= 1; i--)
            {
                // Check if we're close enough to shorten. The range must be less than 1/2
                // the precision to shorten at all, and we want to allow some safety, so
                // use 0.3 instead of 0.5 as a multiplier.
                if (range < (ComputeLatitudePrecision(i * 2) * 0.3))
                {
                    // We're done.
                    return new ShortCode(code.Substring(i * 2), valid: true);
                }
            }
            throw new ArgumentException("Reference location is too far from the Open Location Code center.");
        }

        private static string ValidateCode(string code)
        {
            if (code == null)
            {
                throw new ArgumentException("code cannot be null");
            }
            code = code.ToUpper();
            if (!IsValidUpperCase(code))
            {
                throw new ArgumentException($"code '{code}' is not a valid Open Location Code.");
            }
            return code;
        }


        // Private static utility methods.

        internal static int DigitValueOf(char digitChar)
        {
            return IndexedDigitValues[digitChar - IndexedDigitValueOffset];
        }

        private static double ClipLatitude(double latitude)
        {
            return Math.Min(Math.Max(latitude, -LatitudeMax), LatitudeMax);
        }

        private static double NormalizeLongitude(double longitude)
        {
            while (longitude < -LongitudeMax)
            {
                longitude += LongitudeMax * 2;
            }
            while (longitude >= LongitudeMax)
            {
                longitude -= LongitudeMax * 2;
            }
            return longitude;
        }

        /// <summary>
        /// Normalize a location code by adding the separator '+' character and any padding '0' characters
        /// that are necessary to form a valid location code.
        /// </summary>
        private static string NormalizeCode(string code)
        {
            if (code.Length < SeparatorPosition)
            {
                return code + new string(PaddingCharacter, SeparatorPosition - code.Length) + SeparatorCharacter;
            }
            else if (code.Length == SeparatorPosition)
            {
                return code + SeparatorCharacter;
            }
            else if (code[SeparatorPosition] != SeparatorCharacter)
            {
                return code.Substring(0, SeparatorPosition) + SeparatorCharacter + code.Substring(SeparatorPosition);
            }
            return code;
        }

        /// <summary>
        /// Trim a location code by removing the separator '+' character and any padding '0' characters
        /// resulting in only the code digits.
        /// </summary>
        internal static string TrimCode(string code)
        {
            StringBuilder codeBuilder = new StringBuilder();
            foreach (char c in code)
            {
                if (c != PaddingCharacter && c != SeparatorCharacter)
                {
                    codeBuilder.Append(c);
                }
            }
            return codeBuilder.Length != code.Length ? codeBuilder.ToString() : code;
        }

        /// <summary>
        /// Compute the latitude precision value for a given code length. Lengths &lt;= 10 have the same
        /// precision for latitude and longitude, but lengths > 10 have different precisions due to the
        /// grid method having fewer columns than rows.
        /// </summary>
        /// <remarks>Copied from the JS implementation.</remarks>
        public static double ComputeLatitudePrecision(int codeLength)
        {
            if (codeLength <= CodePrecisionNormal)
            {
                return Math.Pow(EncodingBase, codeLength / -2 + 2);
            }
            return Math.Pow(EncodingBase, -3) / Math.Pow(GridRows, codeLength - PairCodeLength);
        }


        /// <summary>
        /// A class representing a short Open Location Code which is defined by <see cref="IsShort(string)"/>.
        /// <para>
        /// A ShortCode instance can be created the following ways:
        /// <list type="bullet">
        /// <item><see cref="Shorten(double, double)"/> - Shorten a full Open Location Code</item>
        /// <item><see cref="ShortCode(string)"/> - Construct for a valid short Open Location Code</item>
        /// </list>
        /// </para>
        /// A ShortCode can be recovered back to a full Open Location Code using <see cref="RecoverNearest(double, double)"/>
        /// or using the static method <see cref="RecoverNearest(string, double, double)"/> (as defined by the spec).
        /// </summary>
        public class ShortCode
        {

            /// <summary>
            /// Creates a <see cref="ShortCode"/> object for the provided short Open Location Code.
            /// Use <see cref="OpenLocationCode(string)"/> for full codes.
            /// </summary>
            /// <param name="shortCode">A valid short Open Location Code.</param>
            /// <exception cref="ArgumentException">If the code is null, not valid, or not short.</exception>
            public ShortCode(string shortCode)
            {
                Code = ValidateShortCode(ValidateCode(shortCode));
            }

            // Used internally for short codes which are guaranteed to be valid
            // ReSharper disable once UnusedParameter.Local - because public constructor 
            internal ShortCode(string shortCode, bool valid)
            {
                Code = shortCode;
            }

            /// <summary>
            /// The code which is a valid short Open Location Code (plus code).
            /// </summary>
            /// <example>9QCJ+2VX</example>
            /// <value>The string representation of the short code.</value>
            public string Code { get; }


            /// <returns>
            /// A new OpenLocationCode instance representing a full Open Location Code
            /// recovered from this (short) Open Location Code, given the reference location.
            /// </returns>
            /// <param name="referenceLatitude">The reference latitude in decimal degrees.</param>
            /// <param name="referenceLongitude">The reference longitude in decimal degrees.</param>
            public OpenLocationCode RecoverNearest(double referenceLatitude, double referenceLongitude)
            {
                return RecoverNearestValid(Code, referenceLatitude, referenceLongitude);
            }


            /// <inheritdoc />
            /// <summary>
            /// Determines whether the provided object is a ShortCode with the same <see cref="Code"/> as this ShortCode.
            /// </summary>
            public override bool Equals(object obj)
            {
                return obj == this || (obj is ShortCode shortCode && shortCode.Code == Code);
            }

            /// <returns>The hashcode of the <see cref="Code"/> string.</returns>
            public override int GetHashCode()
            {
                return Code.GetHashCode();
            }

            /// <returns>The <see cref="Code"/> string.</returns>
            public override string ToString()
            {
                return Code;
            }


            /// <remarks>
            /// Note: if shortCode is already a valid full code,
            /// this will immediately return a new OpenLocationCode instance with that code
            /// </remarks>
            /// <returns>
            /// A new OpenLocationCode instance representing a full Open Location Code
            /// recovered from the provided short Open Location Code, given the reference location.
            /// </returns>
            /// <param name="shortCode">The valid short Open Location Code to recover</param>
            /// <param name="referenceLatitude">The reference latitude in decimal degrees.</param>
            /// <param name="referenceLongitude">The reference longitude in decimal degrees.</param>
            /// <exception cref="ArgumentException">If the code is null or not valid.</exception>
            public static OpenLocationCode RecoverNearest(string shortCode, double referenceLatitude, double referenceLongitude)
            {
                string validCode = ValidateCode(shortCode);
                if (IsCodeFull(validCode)) return new OpenLocationCode(validCode);

                return RecoverNearestValid(ValidateShortCode(validCode), referenceLatitude, referenceLongitude);
            }

            private static OpenLocationCode RecoverNearestValid(string shortCode, double referenceLatitude, double referenceLongitude)
            {
                referenceLatitude = ClipLatitude(referenceLatitude);
                referenceLongitude = NormalizeLongitude(referenceLongitude);

                int digitsToRecover = SeparatorPosition - shortCode.IndexOf(SeparatorCharacter);
                // The precision (height and width) of the missing prefix in degrees.
                double prefixPrecision = Math.Pow(EncodingBase, 2 - (digitsToRecover / 2));

                // Use the reference location to generate the prefix.
                string recoveredPrefix =
                    new OpenLocationCode(referenceLatitude, referenceLongitude).Code.Substring(0, digitsToRecover);
                // Combine the prefix with the short code and decode it.
                OpenLocationCode recovered = new OpenLocationCode(recoveredPrefix + shortCode);
                GeoPoint recoveredCodeAreaCenter = recovered.Decode().Center;
                // Work out whether the new code area is too far from the reference location. If it is, we
                // move it. It can only be out by a single precision step.
                double recoveredLatitude = recoveredCodeAreaCenter.Latitude;
                double recoveredLongitude = recoveredCodeAreaCenter.Longitude;

                // Move the recovered latitude by one precision up or down if it is too far from the reference,
                // unless doing so would lead to an invalid latitude.
                double latitudeDiff = recoveredLatitude - referenceLatitude;
                if (latitudeDiff > prefixPrecision / 2 && recoveredLatitude - prefixPrecision > -LatitudeMax)
                {
                    recoveredLatitude -= prefixPrecision;
                }
                else if (latitudeDiff < -prefixPrecision / 2 && recoveredLatitude + prefixPrecision < LatitudeMax)
                {
                    recoveredLatitude += prefixPrecision;
                }

                // Move the recovered longitude by one precision up or down if it is too far from the reference.
                double longitudeDiff = recoveredCodeAreaCenter.Longitude - referenceLongitude;
                if (longitudeDiff > prefixPrecision / 2)
                {
                    recoveredLongitude -= prefixPrecision;
                }
                else if (longitudeDiff < -prefixPrecision / 2)
                {
                    recoveredLongitude += prefixPrecision;
                }

                return new OpenLocationCode(recoveredLatitude, recoveredLongitude, recovered.CodeDigits.Length);
            }

            private static string ValidateShortCode(string shortCode)
            {
                if (!IsCodeShort(shortCode))
                {
                    throw new ArgumentException($"code '{shortCode}' is not a valid short Open Location Code.");
                }
                return shortCode;
            }

        }

    }
}
