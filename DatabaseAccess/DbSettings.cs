using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DatabaseAccess
{
    public static class DbSettings
    {
        public static bool processAdmin { get; set; } = true;
        public static bool processBeach { get; set; } = true;
        public static bool processCemetery { get; set; } = true;
        public static bool processHistorical { get; set; } = true;
        public static bool processMall { get; set; } = true;
        public static bool processNatureReserve { get; set; } = true;
        public static bool processPark { get; set; } = true;
        public static bool processRetail { get; set; } = true;
        public static bool processTourism { get; set; } = true;
        public static bool processTrail { get; set; } = true;
        public static bool processUniversity { get; set; } = true;
        public static bool processWater { get; set; } = true;
        public static bool processWetland { get; set; } = true;
        public static bool UseCaching { get; set; } = true;

        //public static bool UsePerformanceTracker { get; set; } = true;  //not used in the DLL project.
    }
}

