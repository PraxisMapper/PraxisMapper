using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OsmXmlParser.Database
{
    //This is the table that gets returned to the app. App will ask for all InterestingPoints in an 8cell, this returns all of those (up to 200, if every block was interesting).
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
    }

    //Reference table for names of areas we care about storing.
    public class AreaType
    {
        public int AreaTypeId { get; set; }
        public string AreaName { get; set; }
        public string OsmTags { get; set; } //More important if I'm manually defining these.
    }


}
