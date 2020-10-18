using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime;
using System.Text;
using System.Threading.Tasks;

namespace DatabaseAccess.Support
{
    public class MemoryMonitor
    {
        //A class to write console output related to memory operations
        long maxRamUsed = 1;

        public MemoryMonitor()
        {
            //we've been called, fire stuff up.
            
            //GCSettings.LatencyMode = GCLatencyMode.Batch; //Might be best for the parser, since there's no interactivity. Might want a parameter for this.
            Log.WriteLog("GC in server mode: " + GCSettings.IsServerGC); //Cannot be set via C# code. Set via runtimeconfig.json or an MSBuild variable.
            Log.WriteLog("GC Latency Mode: " + GCSettings.LatencyMode);

            //A thread to report RAM use.
            Task.Factory.StartNew(() => { while (true) { UpdateMaxRamUsed(); System.Threading.Thread.Sleep(15000); } });
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
