using Google.OpenLocationCode;
using NetTopologySuite.Geometries;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace PraxisCore
{
    //A bunch of exention methods I find useful.
    public static class Extensions
    {
        /// <summary>
        /// Turns a string into an integer using TryParse.
        /// </summary>
        /// <param name="s">the string Int32.TryParse() is called on</param>
        /// <returns>An integer form of s, or 0 if TryParse failed </returns>
        public static int ToTryInt(this string s)
        {
            int temp = 0;
            Int32.TryParse(s, out temp);
            return temp;
        }

        /// <summary>
        /// Turns a string into a int using Parse.
        /// </summary>
        /// <param name="s">the string Int32.Parse() is called on</param>
        /// <returns>An integer form of s</returns>
        public static int ToInt(this string s)
        {
            return Int32.Parse(s);
        }

        /// <summary>
        /// Turns a Span into a Int using Parse.
        /// </summary>
        /// <param name="s">the span Int32.Parse() is called on</param>
        /// <returns>An Int form of s</returns>
        public static int ToInt(this ReadOnlySpan<char> s)
        {
            return Int32.Parse(s);
        }

        /// <summary>
        /// Turns a string into a decimal using TryParse.
        /// </summary>
        /// <param name="s">the string .ToDecimal() is called on</param>
        /// <returns>A decimal form of s, or 0 if TryParse failed </returns>
        public static decimal ToTryDecimal(this string s)
        {
            decimal temp = 0;
            Decimal.TryParse(s, out temp);
            return temp;
        }

        /// <summary>
        /// Turns a string into a double using Parse.
        /// </summary>
        /// <param name="s">the string Double.Parse() is called on</param>
        /// <returns>A double form of s</returns>
        public static double ToDouble(this string s)
        {
            return Double.Parse(s);
        }

        /// <summary>
        /// Turns a Span into a Double using Parse.
        /// </summary>
        /// <param name="s">the span Double.Parse() is called on</param>
        /// <returns>a double form of s</returns>
        public static double ToDouble(this ReadOnlySpan<char> s)
        {
            return Double.Parse(s);
        }

        /// <summary>
        /// Turns a string into a long using Parse.
        /// </summary>
        /// <param name="s">the string .ToLong() is called on</param>
        /// <returns>A long form of s</returns>
        public static long ToLong(this string s)
        {
            return long.Parse(s);  //temp;
        }

        /// <summary>
        /// Turns a Span into a Long using Parse.
        /// </summary>
        /// <param name="s">the span Long.Parse() is called on</param>
        /// <returns>A long form of s</returns>
        public static long ToLong(this ReadOnlySpan<char> s)
        {
            return long.Parse(s);
        }

        /// <summary>
        /// Turns a string into a date using TryParse.
        /// </summary>
        /// <param name="s">the string .ToDate() is called on</param>
        /// <returns>A DateTime form of s, or 1/1/1900 if TryParse failed </returns>
        public static DateTime ToDate(this string s)
        {
            DateTime temp = new DateTime(1900, 1, 1); //get overwritten to 1/1/1 if tryparse fails.
            if (DateTime.TryParse(s, out temp))
                return temp;
            return new DateTime(1900, 1, 1); //converts to datetime in SQL Server, rather than datetime2, which causes problems if a column is only datetime
        }

        /// <summary>
        /// Removes accent marks and other non-character characters from a Unicode text string. EX: Ü becomes U instead.
        /// </summary>
        /// <param name="text">the string this is called on</param>
        /// <returns>The text string without accent marks or other diacritical marks.</returns>
        public static string RemoveDiacritics(this string text)
        {
            if (text == null)
                return null;

            var normalizedString = text.Normalize(NormalizationForm.FormD);
            var stringBuilder = new StringBuilder();

            foreach (var c in normalizedString)
            {
                var unicodeCategory = CharUnicodeInfo.GetUnicodeCategory(c);
                if (unicodeCategory != UnicodeCategory.NonSpacingMark)
                {
                    stringBuilder.Append(c);
                }
            }

            return stringBuilder.ToString().Normalize(NormalizationForm.FormC);
        }

        /// <summary>
        /// Title-cases a string
        /// </summary>
        /// <param name="input">the string to change to title-case</param>
        /// <returns>A Title-cased Version Of A String</returns>
        public static string TitleCase(this string input)
        {
            return CultureInfo.CurrentCulture.TextInfo.ToTitleCase(input.ToLower());
        }

        /// <summary>
        /// Convert a string to its Unicode(UTF-16) byte format.
        /// </summary>
        /// <param name="s">input string</param>
        /// <returns>byte array of unicode values for the string</returns>
        public static byte[] ToByteArrayUnicode(this string s)
        {
            return Encoding.Unicode.GetBytes(s);
        }

        /// <summary>
        /// Convert a string to its UTF8 byte format.
        /// </summary>
        /// <param name="s">input string</param>
        /// <returns>byte array of UTF8 values for the string</returns>
        public static byte[] ToByteArrayUTF8(this string s)
        {
            return Encoding.UTF8.GetBytes(s);
        }

        /// <summary>
        /// Convert a string to its ASCII byte format
        /// </summary>
        /// <param name="s">input string</param>
        /// <returns>byte array of ASCII values for the string</returns>
        public static byte[] ToByteArrayASCII(this string s)
        {
            return Encoding.ASCII.GetBytes(s);
        }

        /// <summary>
        /// Convert a byte array to a string
        /// </summary>
        /// <param name="b">input byte array</param>
        /// <returns>the string represented by the byte array</returns>
        public static string ToByteString(this byte[] b)
        {
            return BitConverter.ToString(b).Replace("-", "");
        }

        public static string ToUTF8String(this byte[] b)
        {
            return Encoding.UTF8.GetString(b);
        }

        /// <summary>
        /// Convert degrees to radians
        /// </summary>
        /// <param name="val">input in degrees</param>
        /// <returns>radian value</returns>
        public static double ToRadians(this double val)
        {
            return (Math.PI / 180) * val;
        }

        /// <summary>
        /// Returns the part of a Span between the start and the presence of the next separator character. Is roughly twice as fast as String.Split(char) and allocates no memory. Removes the found part from the original span.
        /// </summary>
        /// <param name="span"> the ReadOnlySpan to work on.</param>
        /// <param name="seperator">The separator character to use as the split indicator.</param>
        /// <returns></returns>
        public static ReadOnlySpan<char> SplitNext(this ref ReadOnlySpan<char> span, char seperator)
        {
            int pos = span.IndexOf(seperator);
            if (pos > -1)
            {
                var part = span.Slice(0, pos);
                span = span.Slice(pos + 1);
                return part;
            }
            else
            {
                var part = span;
                span = span.Slice(span.Length);
                return part;
            }
        }

        public static Polygon ToPolygon(this GeoArea g)
        {
            return (Polygon)Converters.GeoAreaToPolygon(g);
        }

        public static Polygon ToPolygon(this string s)
        {
            return OpenLocationCode.DecodeValid(s).ToPolygon();
        }

        public static CodeArea ToGeoArea(this string s)
        {
            return OpenLocationCode.DecodeValid(s);
        }

        public static GeoArea ToGeoArea(this Geometry g)
        {
            return Converters.GeometryToGeoArea(g);
        }

        public static T PickOneRandom<T>(this List<T> parent)
        {
            if (parent == null || parent.Count == 0)
                return default(T);
            return parent[Random.Shared.Next(parent.Count())];
        }

        public static T PickOneRandom<T>(this IEnumerable<T> parent)
        {
            if (parent == null || parent.Count() == 0)
                return default(T);
            return parent.OrderBy(r => Random.Shared.Next()).First();
        }

        public static T FromJsonBytesTo<T>(this byte[] data)
        {
            if (data.Length == 0)
                return default(T);
            return JsonSerializer.Deserialize<T>(data.ToUTF8String());
        }

        public static T FromJsonTo<T>(this string data)
        {
            return JsonSerializer.Deserialize<T>(data);
        }

        public static string ToJson(this object data)
        {
            return JsonSerializer.Serialize(data);
        }

        public static byte[] ToJsonByteArray(this object data)
        {
            return JsonSerializer.Serialize(data).ToByteArrayUTF8();
        }

        public static Point ToPoint(this GeoArea g)
        {
            return new Point(g.CenterLongitude, g.CenterLatitude);
        }

        /// <summary>
        /// Seeds a new Random instance based on the given PlusCode, meant for procedural generation. Will usually be unique at Cell6 or bigger cells, collisions ensured at Cell8 or smaller.
        /// </summary>
        /// <param name="plusCode">PlusCode string</param>
        /// <returns>Random seeded with a value based on the PlusCode</returns>
        public static Random GetSeededRandom(this string plusCode)
        {
            var hash = plusCode.GetHashCode();
            return new Random(hash);
        }

        /// <summary>
        /// Seeds a new Random instance based on the given PlusCode, meant for procedural generation. Will usually be unique at Cell6, 4, or 2 cells, collisions ensured at Cell8, 10, and smaller.
        /// </summary>
        /// <param name="plusCode">PlusCode string</param>
        /// <returns>Random seeded with a value based on the PlusCode</returns>
        public static Random GetSeededRandom(this OpenLocationCode plusCode)
        {
            var hash = plusCode.CodeDigits.GetHashCode();
            return new Random(hash);
        }

        public static GeoArea PadGeoArea(this GeoArea g, double padding)
        {
            return new GeoArea(g.SouthLatitude - padding, g.WestLongitude - padding, g.NorthLatitude + padding, g.EastLongitude + padding);
        }
    }
}
