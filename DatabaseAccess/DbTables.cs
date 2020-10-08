using NetTopologySuite.Geometries;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

namespace DatabaseAccess
{

    //TODO possible changes:


    public class DbTables
    {
        //PlayerData table in the database
        public class PlayerData
        {
            public int PlayerDataID { get; set; }
            public string deviceID { get; set; }
            public int t10Cells { get; set; }
            public int t8Cells { get; set; }
            public int cellVisits { get; set; }
            public double distance { get; set; }
            public int score { get; set; }
            public int DateLastTrophyBought { get; set; }
            public int timePlayed { get; set; }
            public double maxSpeed { get; set; }
            public double totalSpeed { get; set; }
            public int altitudeSpread { get; set; }
            public DateTime lastSyncTime { get; set; }
        }

        public class PerformanceInfo
        {
            public int PerformanceInfoID { get; set; }
            public string functionName { get; set; }
            public long runTime { get; set; }
            public DateTime calledAt { get; set; }
            public string notes { get; set; }
        }

        public class MapData
        {
            public long MapDataId { get; set; }
            public string name { get; set; }

            [Column(TypeName = "geography")]
            public Geometry place { get; set; } //allows any sub-type of Geometry to be used
            public string type { get; set; }
            public long? WayId { get; set; }
            public long? NodeId { get; set; }
            public long? RelationId { get; set; }

            //Temporarily removing these: adding this to the global data set takes an hour and creates a log file the size of the DB.
            //public AreaType AreaType { get; set; }
            public int AreaTypeId { get; set; }

        }       

        //Reference table for names of areas we care about storing.
        public class AreaType
        {
            public int AreaTypeId { get; set; }
            public string AreaName { get; set; }
            public string OsmTags { get; set; } //These are not 1:1, so this column may not be useful after all.
            public string HtmlColorCode { get; set; } //for potential tile-drawing operations.
        }

        //public class SinglePointsOfInterest
        //{
        //    public long SinglePointsOfInterestId { get; set; }
        //    public long NodeID { get; set; }
        //    public string name { get; set; }
        //    public double lat { get; set; }
        //    public double lon { get; set; }
        //    public string NodeType { get; set; } //same possible results as AreaType, same function. same possible FK value.
        //    public string PlusCode { get; set; } //10 digit code, no plus sign.
        //    public string PlusCode8 { get; set; } //8 digit code, no plus sign, for indexing purposes.
        //    public string PlusCode6 { get; set; } //6 digit code, no plus sign, for indexing purposes.
        //}

        public class PremadeResults
        {
            public long PremadeResultsId { get; set; }
            public string PlusCode6 { get; set; }
            public string Data { get; set; } 
        }

        public class MinimumRelation
        {
            public long MinimumRelationId{ get; set; }
            public long RelationId { get; set; }
        }

        public class MinimumWay
        {
            public long? MinimumWayId { get; set; }
            public long WayId { get; set; }
            public ICollection<MinimumNode> Nodes { get; set; } //ICollection lets EF Core 5 generate join tables automatically this way.
        }

        public class MinimumNode
        {
            public long? MinimumNodeId { get; set; }
            public long NodeId { get; set; }
            public double? Lat { get; set; }
            public double? Lon { get; set; }
            public ICollection<MinimumWay> Ways { get; set; } //ICollection lets EF Core 5 generate join tables automatically this way.
        }
    }
}

