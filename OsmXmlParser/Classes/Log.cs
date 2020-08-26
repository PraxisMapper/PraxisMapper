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
        public static void WriteLog(string message)
        {
            Console.WriteLine(message);
            System.IO.File.AppendAllText(filename, message + Environment.NewLine);
        }

    }
}
