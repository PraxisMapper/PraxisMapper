using System;
using System.IO;

namespace PraxisCore
{
    public static class Log
    {
        static string filename = "PraxisCore-" + DateTime.Now.ToString("yyyyMMddHHmmss") + ".txt";
        /// <summary>
        /// The level of logging to display. Ignores messages if their output level is higher than this.
        /// </summary>
        public static VerbosityLevels Verbosity = VerbosityLevels.Normal;
        /// <summary>
        /// If true, writes all console output to a file as well.
        /// </summary>
        public static bool SaveToFile = false;

        private static object fileLock = new object();
        public enum VerbosityLevels
        {
            Off = 1, //No calls to WriteLog pass Off as their verbosity level.
            Errors,
            Normal,
            High
        }

        /// <summary>
        /// Write a log message to the console, and a file if WriteToFile is true
        /// </summary>
        /// <param name="message">The string to write to the console/logfile</param>
        /// <param name="outputLevel">What level of alert this log is. Will not be displayed/written if Verbosity is lower than this value.</param>
        public static void WriteLog(string message, VerbosityLevels outputLevel = VerbosityLevels.Normal)
        {
            if (Verbosity < outputLevel)
                return;

            Console.WriteLine(message);
            if (SaveToFile)
                lock (fileLock)
                    File.AppendAllText(filename, message + Environment.NewLine);
        }
    }
}
