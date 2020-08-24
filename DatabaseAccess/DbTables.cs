using NetTopologySuite.Geometries;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DatabaseAccess
{
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
        }

        public class MapData
        {
            public long MapDataId { get; set; }
            public string name { get; set; }
            public long WayId { get; set; }

            [Column(TypeName = "geography")]//might explicitly need tagged as dataType = geography for EF Core? or geometry? or geometry is the C# class and geography applies some extra stuff?
            public Geometry place { get; set; } //allows any sub-type of Geometry to be used
            public string type { get; set; }
                
        }

        //These are the individual 10-cell values.
        public class InterestingPoint
        {
            //Unique ID
            public long InterestingPointId { get; set; }
            //the Way ID in OSM data.
            public long OsmWayId { get; set; }
            //8 character Plus Code string, city-block sized. EX: 8GC4RVM2
            public string PlusCode8 { get; set; }
            //2 characters in a Plus Code after the plus EX: 23
            public string PlusCode2 { get; set; }
            //Total plus code would be PlusCode8 + "+" + PlusCode2
            public long ProcessedWayID { get; set; }  //FK to the other table, in case I need to recalculate these?
            public string areaType { get; set; } //what type of tile to display in this square. Matches up to some OSM tag combo.
        }

        //An attempt at storing the data as efficiently as possible.
        //Ways from OSM are reduced to rectangular abstractions.
        //These get read and processed into InterestingPoint entries.
        public class ProcessedWay
        {
            public long ProcessedWayId { get; set; }
            public long OsmWayId { get; set; } //possibly optiona, if space efficiency is an issue.
            public double latitudeS { get; set; }
            public double longitudeW { get; set; }
            public double distanceE { get; set; }
            public double distanceN { get; set; }
            public DateTime lastUpdated { get; set; } //potentially optional, if space efficiency is an issue.
            [ForeignKey("AreaType")]
            public int AreaTypeId { get; set; }  //FK to what area type this row is.
            public string AreaType { get; set; } //placeholder data until I get FKs set up and worked out.
            public string name { get; set; } //keeping for reference, in case i want to see WHAT an area is.

            //Might want a ModifiedLat / ModifiedLon column(s) for searching, so I could pass in coordinate points and pull back 
            //anything in bounds, bounds being (lat/lon + distance)
            //EX: my search range is 40, -80 to 41, -79
            //Pull everything where 
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

        }
    }
}
