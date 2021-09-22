using System;
using System.IO;

namespace PraxisCore
{
    public static class Log
    {
        //TODO: make this a service? Replace with default logging class?
        static string filename = "PraxisCore-" + DateTime.Now.ToString("yyyyMMddHHmmss") + ".txt";
        static StreamWriter sw = new StreamWriter(filename);
        /// <summary>
        /// The level of logging to display. Ignores messages if their output level is higher than this.
        /// </summary>
        public static VerbosityLevels Verbosity = VerbosityLevels.Normal;
        /// <summary>
        /// If true, writes all console output to a file as well.
        /// </summary>
        public static bool WriteToFile = false;

        private static object fileLock = new object();
        public enum VerbosityLevels
        {
            Off = 1, //No calls to WriteLog pass Off as their verbosity level.
            Normal,
            High
                //TODO: add ErrorsOnly, between Off and Normal?
        }

        /// <summary>
        /// Write a log message to the console, and a file if WriteToFile is true
        /// </summary>
        /// <param name="message">The string to write to the console/logfile</param>
        /// <param name="outputLevel">What level of alert this log is. Will not be displayed/written if Verbosity is lower than this value.</param>
        public static void WriteLog(string message, VerbosityLevels outputLevel = VerbosityLevels.Normal)
        {
            if ((int)Verbosity < (int)outputLevel)
                return;

            Console.WriteLine(message);
            if (WriteToFile)
                lock (fileLock)
                    sw.WriteLine(message);
        }

    }
}
