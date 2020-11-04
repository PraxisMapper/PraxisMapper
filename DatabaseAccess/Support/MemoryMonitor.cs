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
            Log.WriteLog("GC Max Generation: " + GC.MaxGeneration);

            //A thread to report RAM use.
            //GC.RegisterForFullGCNotification(99, 99);//This shows up way below maximum memory on the system. I want to only do this when I'm using swap file. Reports too often, isn't useful the way I want it to be.
            Task.Factory.StartNew(() => { while (true) { UpdateMaxRamUsed(); System.Threading.Thread.Sleep(15000); } });
            //Task.Factory.StartNew(() => { while (true) { CheckForGCNotification(); System.Threading.Thread.Sleep(1000); } }); //Shows up way too much

            
        }

        ~MemoryMonitor()
        {
            GC.CancelFullGCNotification();
        }


        public void UpdateMaxRamUsed()
        {
            Process proc = Process.GetCurrentProcess();
            long currentRAM = proc.PrivateMemorySize64;
            if (currentRAM > maxRamUsed)
            {
                maxRamUsed = currentRAM;
                Log.WriteLog("Peak RAM is now " + String.Format("{0:n0}", maxRamUsed));
                var data = GC.GetGCMemoryInfo();
                Log.WriteLog("Heap Size: " + String.Format("{0:n0}", data.HeapSizeBytes), Log.VerbosityLevels.High);
                Log.WriteLog("High Memory Threshold: " + String.Format("{0:n0}", data.HighMemoryLoadThresholdBytes), Log.VerbosityLevels.High);
                Log.WriteLog("Memory Load: " + String.Format("{0:n0}", data.MemoryLoadBytes), Log.VerbosityLevels.High);
                Log.WriteLog("Committed Size: " + String.Format("{0:n0}", data.TotalCommittedBytes), Log.VerbosityLevels.High);
                Log.WriteLog("Total Available Size: " + String.Format("{0:n0}", data.TotalAvailableMemoryBytes), Log.VerbosityLevels.High);
            }
            proc.Dispose();
        }

        public void CheckForGCNotification()
        {
            //This show up a lot. None of the documentation on GC currently seems to adjust this the way I expect it to.
            GCNotificationStatus s = GC.WaitForFullGCApproach();
            if (s == GCNotificationStatus.Succeeded)
            {
                Log.WriteLog("Garbage Collection Impeding!");
                Log.WriteLog("Gen0 collections:" + GC.CollectionCount(0), Log.VerbosityLevels.High);
                Log.WriteLog("Gen1 collections:" + GC.CollectionCount(1), Log.VerbosityLevels.High);
                Log.WriteLog("Gen2 collections:" + GC.CollectionCount(2), Log.VerbosityLevels.High);
            }

            s = GC.WaitForFullGCComplete();
            if (s == GCNotificationStatus.Succeeded)
            {
                Log.WriteLog("Garbage Collection Completed. Processing has resumed.");
            }
        }
    }
}
