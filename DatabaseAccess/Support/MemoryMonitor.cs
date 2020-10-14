using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime;
using System.Text;
using System.Threading.Tasks;

namespace DatabaseAccess.Support
{
    class MemoryMonitor
    {
        //A class to write console output related to memory operations
        long maxRamUsed = 1;

        public MemoryMonitor()
        {
            //we've been called, fire stuff up.
            //A thread to watch RAM use.
            Log.WriteLog("GC in server mode: " + GCSettings.IsServerGC);
            Log.WriteLog("GC Latency Mode: " + GCSettings.LatencyMode);
        }


        public void UpdateMaxRamUsed()
        {
            Process proc = Process.GetCurrentProcess();
            long currentRAM = proc.PrivateMemorySize64;
            if (currentRAM > maxRamUsed)
            {
                maxRamUsed = currentRAM;
                Log.WriteLog("Peak RAM is now " + String.Format("{0:n0}", maxRamUsed));
            }
            proc.Dispose();
        }
    }
}
