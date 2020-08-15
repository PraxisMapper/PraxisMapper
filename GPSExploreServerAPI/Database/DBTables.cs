using NetTopologySuite.Geometries;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Threading.Tasks;

namespace GPSExploreServerAPI.Database
{
    //PlayerData table in the database
    public class PlayerData
    {
        public int PlayerDataID { get; set; }
        public string deviceID { get; set; }
        public int t10Cells { get; set; }
        public int t8Cells { get; set; }
        public int cellVisits { get; set; }
        public int distance { get; set; }
        public int score { get; set; }
        public int DateLastTrophyBought { get; set; }
        public int timePlayed { get; set; }
        public int maxSpeed { get; set; }
        public int totalSpeed { get; set; }
        public int maxAltitude { get; set; }
        public DateTime lastSyncTime { get; set; }
    }

    public class PerformanceInfo
    {
        public int PerformanceInfoID { get; set; }
        public string functionName { get; set; }
        public long runTime { get; set; }
        public DateTime calledAt { get; set; }
    }

    public class MapData
    {
        [Column(TypeName ="geography")]//might explicitly need tagged as dataType = geography for EF Core? or geometry? or geometry is the C# class and geography applies some extra stuff?
        public Geometry place { get; set; } 
    }
}
