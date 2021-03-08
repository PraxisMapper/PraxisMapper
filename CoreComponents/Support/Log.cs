using System;

namespace CoreComponents
{
    public static class Log
    {
        //TODO: make this a service? Replace with default logging class?
        static string filename = "PraxisCore-" + DateTime.Now.ToString("yyyyMMddHHmmss") + ".txt";
        public static VerbosityLevels Verbosity = VerbosityLevels.Normal;
        public static bool WriteToFile = false;

        public enum VerbosityLevels
        {
            Off = 1, //No calls to WriteLog pass Off as their verbosity level.
            Normal,
            High
                //TODO: add ErrorsOnly, between Off and Normal?
        }

        public static void WriteLog(string message, VerbosityLevels outputLevel = VerbosityLevels.Normal)
        {
            if ((int)Verbosity < (int)outputLevel)
                return;

            Console.WriteLine(message);
            if (WriteToFile) System.IO.File.AppendAllText(filename, message + Environment.NewLine);
        }

    }
}
