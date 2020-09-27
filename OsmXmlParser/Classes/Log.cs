using RTools_NTS.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OsmXmlParser.Classes
{
    public static class Log
    {
        static string filename = "OsmXmlParser" + DateTime.Now.ToString("yyyyMMddHHmmss") + ".txt";
        public static VerbosityLevels Verbosity = VerbosityLevels.Normal;

        public enum VerbosityLevels
        {
            Off = 1,
            Normal,
            High
        }

        public static void WriteLog(string message, VerbosityLevels outputLevel = VerbosityLevels.Normal)
        {
            //if (Verbosity != VerbosityLevels.Off)
            //    return;

            if ((int)Verbosity < (int)outputLevel)
                return;

            Console.WriteLine(message);
            System.IO.File.AppendAllText(filename, message + Environment.NewLine);
        }

    }
}
