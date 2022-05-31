using System;
using System.Collections.Generic;

namespace PraxisCore
{
    public class StandaloneDbTables
    {
        //DB Tables below here
        public class MapTileDB //read-only for the destination app
        {
            public long id { get; set; }
            public string PlusCode { get; set; }
            public byte[] image { get; set; }
            public int layer { get; set; } //I might want to do variant maptiles where each area cliamed adds an overlay to the base map tile, this tracks which stacking order this goes in.
        }

        public class TerrainInfo //read-only for the destination app.
        {
            public long id { get; set; }
            public string PlusCode { get; set; } //now is a Cell6 instead of a Cell10
            public List<TerrainDataSmall> TerrainDataSmall { get; set; }

        }

        public class TerrainDataStandalone //read-only for the destination app. Reduces storage space on big areas.
        {
            public long id { get; set; }
            public string Name { get; set; }
            public string areaType { get; set; } //the game element name
            //These column(s) are used by MapDataController.LearnCell8, so they stay, even though I'm not using them in the self contained DB.
            public Guid PrivacyId { get; set; } //Might be irrelevant on self-contained DB. Matches PrivacyId in main DB

            public override string ToString()
            {
                return Name + ":" + areaType;
            }
        }

        public class TerrainDataSmall //read-only for the destination app. As above, but only stores names/area types instead of elements.
        {
            public long id { get; set; }
            public string Name { get; set; }
            public string areaType { get; set; } //the game element name
            public List<TerrainInfo> TerrainInfo { get; set; }
            public override string ToString()
            {
                return Name + ":" + areaType;
            }
        }

        public class Bounds //readonly for the destination app. Should be a common 'settings' or baseline data table.
        {
            public int id { get; set; }
            public double NorthBound { get; set; }
            public double SouthBound { get; set; }
            public double EastBound { get; set; }
            public double WestBound { get; set; }
            public double length { get; set; }
            public double height { get; set; }
            public string commonCodeLetters { get; set; }
            public long LastPlayedOn { get; set; } //for idle game offline gains.
            public long StartedCurrentIdleRun { get; set; }
            public long BestIdleCompletionTime { get; set; } //your best idle game completion time.

        }

        public class PlusCodesVisited //read-write. has bool for any visit, last visit date, is used for daily/weekly checks, and PaintTheTown mode.
        {
            public int id { get; set; }
            public string PlusCode { get; set; }
            public int visited { get; set; } //0 for false, 1 for true
                                             //Times in Lua are longs, so store that instead of a string
            public long lastVisit { get; set; }
            public long nextDailyBonus { get; set; }
            public long nextWeeklyBonus { get; set; }
        }

        public class PlayerStats //read-write, doesn't leave the device
        {
            public int id { get; set; }
            public int timePlayed { get; set; }
            public double distanceWalked { get; set; }
            public long score { get; set; }
        }

        public class ScavengerHuntStandalone
        {
            public int id { get; set; }
            //public int listId { get; set; } //Which list does this entry appear on. A 
            public string listName { get; set; } //using name as an ID, to avoid needed a separate table thats just ids and names.
            public string description { get; set; } //Defaults to element name on auto-generated lists. Users could make this hints or clues instead.
            public bool playerHasVisited { get; set; } //Single player mode means I can store this inline.
            public long PlaceId { get; set; } //Reference to see what this thing is in the source data. Empty for user-created items.
        }

        public class PlaceInfo2
        {
            //The new way to store data in the mobile app for places to visit.
            //starting off with terrainData and improving from that.
            public long id { get; set; }
            public string Name { get; set; }
            public string areaType { get; set; } //the game element name
            //These 2 columns are used by MapDataController.LearnCell8, so they stay, even though I'm not using them in the self contained DB.
            public long PlaceId { get; set; } //Might need to be a long. Might be irrelevant on self-contained DB (except maybe for loading an overlay image on a maptile?)
            public double latCenter { get; set; }
            public double lonCenter { get; set; }
            public double height { get; set; } //rectangle estimates, should be better.
            public double width { get; set; }

            public override string ToString()
            {
                return Name + ":" + areaType;
            }
        }

        public class PlaceIndex
        {
            //Rename this
            //Which areas to look for in any given Cell6

            public long id { get; set; }
            public string PlusCode { get; set; } //now is a Cell6 instead of a Cell10
            public long placeInfoId { get; set; } //doing this manually for some reason.

        }

        public class IdleStats
        {
            //Only 1 row, with the values as columns
            public long emptySpacePerSecond { get; set; }
            public long emptySpaceTotal { get; set; }
            public long parkSpacePerSecond { get; set; }
            public long parkSpaceTotal { get; set; }
            public long graveyardSpacePerSecond { get; set; }
            public long graveyardSpaceTotal { get; set; }
            public long touristSpacePerSecond { get; set; }
            public long touristSpaceTotal { get; set; }
            public long natureReserveSpacePerSecond { get; set; }
            public long natureReserveSpaceTotal { get; set; }
            public long trailSpacePerSecond { get; set; }
            public long trailSpaceTotal { get; set; }
        }

    }
}
