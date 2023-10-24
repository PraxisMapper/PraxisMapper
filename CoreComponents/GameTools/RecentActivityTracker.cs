using System;
using System.Collections.Generic;
using System.Linq;

namespace PraxisCore.GameTools {
    /// <summary>
    /// Keeps a record of places that have recently been visited. Useful for tracking daily reward grants for entering Areas, or to set cooldowns on when an Area can be flipped for teams.
    /// If this is used to track a player's location, this MUST be saved with SetSecurePlayerData.
    /// </summary>
    public sealed class RecentActivityTracker {
        /// <summary>
        /// How many minutes to treat an entry as recent. Defaults to 22 hours to allow for a daily routine.
        /// </summary>
        public int minuteDelay { get; set; } = 22 * 60;
        /// <summary>
        /// The actual location history. Tracks PlusCodes by Cell name, and the time that PlusCode stops being considered as recent. Timestamp is NOT updated if the same PlusCode has been recently visited.
        /// </summary>
        public Dictionary<string, DateTime> history { get; set; } = new Dictionary<string, DateTime>();
        /// <summary>
        /// The next time to clear out the actual history entries. All expired entries are removed upon detection of one, or 10 minutes have passed since a purge occurred.
        /// </summary>
        public DateTime nextPurge { get; set; } = DateTime.UtcNow;


        /// <summary>
        /// Determines if the player has recently entered this PlusCode. Returns 'true' if this is the first time in the last $hourDelay they have entered this Cell10.
        /// </summary>
        /// <param name="plusCode10">The PlusCode to check in the database. Will not detect child PlusCode cells (EX: passing in a Cell8 will return false even in Cell10s in that Cell8 are stored)</param>
        /// <returns>true if this PlusCode has not been visited in minuteDelay minutes, false if it has.</returns>
        public bool IsRecent(string plusCode10)
        {
            bool grant = true;
            bool purge = false;
            if (history.TryGetValue(plusCode10, out var expiry))
            {
                if (expiry > DateTime.UtcNow)
                    grant = false;
                else
                {
                    purge = true; //We have at least 1 expired entry in history.
                    history[plusCode10] = DateTime.UtcNow.AddMinutes(minuteDelay); //but we'll update the current one now, instead of next time.
                }
            }
            else
                history.Add(plusCode10, DateTime.UtcNow.AddMinutes(minuteDelay));

            if (purge || nextPurge < DateTime.UtcNow) //This is a pretty heavy duty effort, so we shouldn't do it on every call.
            {
                history = history.Where(h => h.Value > DateTime.UtcNow).ToDictionary(k => k.Key, v => v.Value);
                nextPurge = DateTime.UtcNow.AddMinutes(10);
            }
            return grant;
        }
    }
}
