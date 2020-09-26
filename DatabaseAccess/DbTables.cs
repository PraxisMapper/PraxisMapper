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
            public long WayId { get; set; }

            [Column(TypeName = "geography")]
            public Geometry place { get; set; } //allows any sub-type of Geometry to be used
            public string type { get; set; }
                
        }       

        //Reference table for names of areas we care about storing.
        public class AreaType
        {
            public int AreaTypeId { get; set; }
            public string AreaName { get; set; }
            public string OsmTags { get; set; } //More important if I'm manually defining these.
        }

        public class SinglePointsOfInterest
        {
            public long SinglePointsOfInterestId { get; set; }
            public long NodeID { get; set; }
            public string name { get; set; }
            public double lat { get; set; }
            public double lon { get; set; }
            public string NodeType { get; set; } //same possible results as AreaType, same function. same possible FK value.
            public string PlusCode { get; set; } //10 digit code, no plus sign.
            public string PlusCode8 { get; set; } //8 digit code, no plus sign, for indexing purposes.
            public string PlusCode6 { get; set; } //6 digit code, no plus sign, for indexing purposes.
        }

        public class PremadeResults
        {
            public long PremadeResultsId { get; set; }
            public string PlusCode6 { get; set; }
            public string Data { get; set; } 
        }

        public class OsmRelation
        {
            public long OsmRelationId{ get; set; }
        }

        public class OsmWay
        {
            public long OsmWayId { get; set; }
            public List<OsmNode> Nodes { get; set; }
        }

        public class OsmNode
        {
            public long OsmNodeId { get; set; }
            public long Lat { get; set; }
            public long Lon { get; set; }
        }

    }
}

