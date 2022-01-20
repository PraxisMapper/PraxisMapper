using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace PraxisCore
{
    //A bunch of exention methods I find useful.
    public static class Extensions
    {
        /// <summary>
        /// Turns a string into an integer using TryParse.
        /// </summary>
        /// <param name="s">the string .ToInt() is called on</param>
        /// <returns>An integer form of s, or 0 if TryParse failed </returns>
        public static int ToInt(this string s)
        {
            int temp = 0;
            Int32.TryParse(s, out temp);
            return temp;
        }

        /// <summary>
        /// Turns a string into a decimal using TryParse.
        /// </summary>
        /// <param name="s">the string .ToDecimal() is called on</param>
        /// <returns>A decimal form of s, or 0 if TryParse failed </returns>
        public static decimal ToDecimal(this string s)
        {
            decimal temp = 0;
            Decimal.TryParse(s, out temp);
            return temp;
        }

        /// <summary>
        /// Turns a string into a double using TryParse.
        /// </summary>
        /// <param name="s">the string .ToDouble() is called on</param>
        /// <returns>A double form of s, or 0 if TryParse failed </returns>
        public static double ToDouble(this string s)
        {
            //This function uses a full 10% of the CPU time with TryParse. Switching to regular parse for speed.
            //double temp = 0;
            return Double.Parse(s);
            //return temp;
        }

        /// <summary>
        /// Turns a string into a long using TryParse.
        /// </summary>
        /// <param name="s">the string .ToLong() is called on</param>
        /// <returns>A long form of s, or 0 if TryParse failed </returns>
        public static long ToLong(this string s)
        {
            //Additional small speed improvement
            //long temp = 0;
            //long.TryParse(s, out temp);
            return long.Parse(s);  //temp;
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
        /// Convert a string to its Unicode byte format.
        /// </summary>
        /// <param name="s">input string</param>
        /// <returns>byte array of unicode values for the string</returns>
        public static byte[] ToByteArrayUnicode(this string s)
        {
            return Encoding.Unicode.GetBytes(s);
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

        /// <summary>
        /// Take a single list of OSM elements and split it into a given number of sub-lists. [This can be replaced with .Chunk(this.Count() % splitIntoLists)]
        /// </summary>
        /// <param name="mainlist">the source list</param>
        /// <param name="splitIntoCount">how many new lists to create</param>
        /// <returns>a List of arrays of OSM elements</returns>
        public static IEnumerable<DbTables.StoredOsmElement>[] SplitListToMultiple(this IEnumerable<DbTables.StoredOsmElement> mainlist, int splitIntoCount)
        {
            List<DbTables.StoredOsmElement>[] results = new List<DbTables.StoredOsmElement>[splitIntoCount];
            for (int i = 0; i < splitIntoCount; i++)
                results[i] = new List<DbTables.StoredOsmElement>(2600);

            int splitCount = 0;
            foreach(var i in mainlist)
            {
                results[splitCount % splitIntoCount].Add(i);
                splitCount++;
            }
            return results;
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
    }
}
