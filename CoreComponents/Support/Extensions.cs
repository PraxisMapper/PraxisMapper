using Google.OpenLocationCode;
using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Utilities;
using PraxisCore.Support;
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
            Int32.TryParse(s, out var temp);
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
            Decimal.TryParse(s, out var temp);
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
            long.TryParse(s, out long result);
            return result;
        }

        /// <summary>
        /// Turns a Span into a Long using Parse.
        /// </summary>
        /// <param name="s">the span Long.Parse() is called on</param>
        /// <returns>A long form of s</returns>
        public static long ToLong(this ReadOnlySpan<char> s)
        {
            long.TryParse(s, out long result);
            return result;
        }

        /// <summary>
        /// Turns a string into a date using TryParse.
        /// </summary>
        /// <param name="s">the string .ToDate() is called on</param>
        /// <returns>A DateTime form of s, or 1/1/1900 if TryParse failed </returns>
        public static DateTime ToDate(this string s)
        {
            if (DateTime.TryParse(s, out var temp))
                return temp;
            return new DateTime(1900, 1, 1); //converts to datetime in SQL Server, rather than datetime2, which causes problems if a column is only datetime
        }

        /// <summary>
        /// Advances the given DateTime to midnight (0:00:00 on the next day).
        /// </summary>
        /// <param name="d"></param>
        /// <returns></returns>
        public static DateTime ForwardToMidnight(this DateTime d) {
            return d.AddHours(23 - d.Hour).AddMinutes(59 - d.Minute).AddSeconds(60 - d.Second);
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
        /// Converts a DateTime to a format accepted by most JSON-parsing applications, following ISO 8601
        /// </summary>
        /// <param name="dateTime"></param>
        /// <returns>a string that looks like 2022-01-13T16:25:35Z</returns>
        public static string ToIso8601(this DateTime dateTime)
        {
            return dateTime.ToString("u").Replace(" ", "T");
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
        /// <param name="separator">The separator character to use as the split indicator.</param>
        /// <returns></returns>
        public static ReadOnlySpan<char> SplitNext(this ref ReadOnlySpan<char> span, char separator)
        {
            int pos = span.IndexOf(separator);
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

        public static CodeArea ToGeoArea(this ReadOnlySpan<char> s)
        {
            return OpenLocationCode.DecodeValid(s);
        }

        public static GeoArea ToGeoArea(this Geometry g)
        {
            return Converters.GeometryToGeoArea(g);
        }

        //TODO: in NET 8, Might change these to use System.Random.GetItems<T>()
        public static T PickOneRandom<T>(this List<T> parent)
        {
            if (parent == null || parent.Count == 0)
                return default(T);
            return parent[Random.Shared.Next(parent.Count)];
        }

        public static T PickOneRandom<T>(this IEnumerable<T> parent)
        {
            if (parent == null || !parent.Any())
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

        //Note: .NET Core's built-in GetHashCode() returns different values for every execution, because not doing so is a potential DDOS vector for IIS via hash collisions.
        //So I found this on Andrew Lock's site and added this function here to use for procedural generation logic based on a plusCode.
        public static int GetDeterministicHashCode(this string str)
        {
            unchecked
            {
                int hash1 = (5381 << 16) + 5381;
                int hash2 = hash1;

                for (int i = 0; i < str.Length; i += 2)
                {
                    hash1 = ((hash1 << 5) + hash1) ^ str[i];
                    if (i == str.Length - 1)
                        break;
                    hash2 = ((hash2 << 5) + hash2) ^ str[i + 1];
                }

                return hash1 + (hash2 * 1566083941);
            }
        }

        public static ulong GetDeterministicHashCodeForMersenne(this string str) {
            unchecked {
                ulong hash1 = (5381 << 16) + 5381;
                ulong hash2 = hash1;

                for (int i = 0; i < str.Length; i += 2) {
                    hash1 = ((hash1 << 5) + hash1) ^ str[i];
                    if (i == str.Length - 1)
                        break;
                    hash2 = ((hash2 << 5) + hash2) ^ str[i + 1];
                }

                return hash1 + (hash2 * 1566083941);
            }
        }


        /// <summary>
        /// Seeds a new Random instance based on the given PlusCode, meant for procedural generation. Will usually be unique at Cell6 or bigger cells, collisions ensured at Cell8 or smaller.
        /// </summary>
        /// <param name="plusCode">PlusCode string</param>
        /// <returns>Random seeded with a value based on the PlusCode</returns>
        public static Random GetSeededRandom(this string plusCode)
        {
            var hash = plusCode.GetDeterministicHashCode();
            return new Random(hash);
        }

        public static RandomNumberGenerator GetSeededRandomMersenne(this string plusCode) {
            var hash = plusCode.GetDeterministicHashCodeForMersenne();
            return new RandomNumberGenerator(hash);
        }

        /// <summary>
        /// Seeds a new Random instance based on the given PlusCode, meant for procedural generation. Will usually be unique at Cell6, 4, or 2 cells, collisions ensured at Cell8, 10, and smaller.
        /// </summary>
        /// <param name="plusCode">PlusCode string</param>
        /// <returns>Random seeded with a value based on the PlusCode</returns>
        public static Random GetSeededRandom(this OpenLocationCode plusCode)
        {
            var hash = plusCode.CodeDigits.GetDeterministicHashCode();
            return new Random(hash);
        }

        public static RandomNumberGenerator GetSeededRandomMersenne(this OpenLocationCode plusCode) {
            var hash = plusCode.CodeDigits.GetDeterministicHashCodeForMersenne();
            return new RandomNumberGenerator(hash);
        }

        /// <summary>
        /// Adds the padding value to all sides of a GeoArea
        /// </summary>
        /// <param name="g">the original GeoArea</param>
        /// <param name="padding">size in degrees to pad the GeoArea</param>
        /// <returns>new GeoArea with the padding added to the original's area</returns>
        public static GeoArea PadGeoArea(this GeoArea g, double padding)
        {
            return new GeoArea(Math.Max(-90, g.SouthLatitude - padding), Math.Max(-180, g.WestLongitude - padding), Math.Min(90, g.NorthLatitude + padding), Math.Min(180, g.EastLongitude + padding));
        }

        /// <summary>
        /// Given a PlusCode, this adds the next set of characters to create all child entries. (EX:22 gives a list of 2222, 2223, 2224, etc)
        /// </summary>
        /// <param name="plusCode">A PlusCode</param>
        /// <returns>A list of the PlusCodes contained in the given PlusCode one step down</returns>
        public static List<string> GetSubCells(this string plusCode)
        {
            var list = new List<string>(400);
            if (plusCode.Length < 10)
            {
                foreach (var Yletter in OpenLocationCode.CodeAlphabet)
                    foreach (var Xletter in OpenLocationCode.CodeAlphabet)
                    {
                        list.Add(plusCode + Yletter + Xletter);
                    }
            }
            else
            {
                foreach (var letter in OpenLocationCode.CodeAlphabet)
                    list.Add(plusCode + letter);
            }
            return list;
        }

        /// <summary>
        /// returns the distance in meters between 2 GeoPoints
        /// </summary>
        /// <param name="p">the first GeoPoint</param>
        /// <param name="otherPoint">the GeoPoint to measure to</param>
        /// <returns>distance in meters between the 2 GeoPoints</returns>
        public static double MetersDistanceTo(this GeoPoint p, GeoPoint otherPoint)
        {
            return GeometrySupport.MetersDistanceTo(p, otherPoint);
        }

        /// <summary>
        /// returns the distance in meters between the center of 2 GeoAreas
        /// </summary>
        /// <param name="g">the first GeoArea</param>
        /// <param name="otherArea">the GeoArea to measure to</param>
        /// <returns>the distance in meters between the 2 GeoArea's centers</returns>
        public static double MetersDistanceTo(this GeoArea g, GeoArea otherArea)
        {
            return GeometrySupport.MetersDistanceTo(g.Center, otherArea.Center);
        }

        /// <summary>
        /// returns the distance in meters between the center of 2 PlusCodes
        /// </summary>
        /// <param name="g">the first PlusCode</param>
        /// <param name="otherPlusCode">the PlusCode to measure to.</param>
        /// <returns>the distance in meters between the center of the 2 PlusCodes</returns>
        public static double MetersDistanceTo(this string g, string otherPlusCode)
        {
            return GeometrySupport.MetersDistanceTo(g.ToGeoArea().Center, otherPlusCode.ToGeoArea().Center);
        }

        public static double MetersDistanceTo(this Point g, Point otherPoint) {
            return GeometrySupport.MetersDistanceTo(g, otherPoint);
        }

        public static Geometry Fix(this Geometry g) {
            return GeometryFixer.Fix(g);
        }
    }
}
