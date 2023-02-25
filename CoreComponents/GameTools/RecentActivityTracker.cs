using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PraxisCore.GameTools
{
    public class RecentActivityTracker
    {
        //Stores Cell10s a player has entered within the last $hourDelay, with a datestamp for when that entry will be removed
        public int hourDelay { get; set; } = 22;
        public Dictionary<string, DateTime> history { get; set; } = new Dictionary<string, DateTime>();

        /// <summary>
        /// Determines if the player has recently entered this PlusCode. Returns 'true' if this is the first time in the last $hourDelay they have entered this Cell10.
        /// </summary>
        /// <param name="plusCode10"></param>
        /// <returns></returns>
        public bool IsRecent(string plusCode10)
        {
            bool grant = true;
            if (history.TryGetValue(plusCode10, out var expiry))
            {
                if (expiry > DateTime.UtcNow)
                    grant = false;
            }
            else
                history.Add(plusCode10, DateTime.UtcNow.AddHours(hourDelay));

            history = history.Where(h => h.Value > DateTime.UtcNow).ToDictionary(k => k.Key, v => v.Value);
            return grant;
        }
    }
}
