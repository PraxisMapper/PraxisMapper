using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OsmXmlParser.Database
{
    //This is the table that gets returned to the app. App will ask for all InterestingPoints in an 8cell, this returns all of those (up to 200, if every block was interesting).
    public class InterestingPoints
    {
        //Unique ID
        public long InterestingPointID;
        //the Way ID in OSM data.
        public long OsmWayId;
        //8 character Plus Code string, city-block sized. EX: 8GC4RVM2
        public string PlusCode8;
        //2 characters in a Plus Code after the plus EX: 23
        public string PlusCode2;
        //Total plus code would be PlusCode8 + "+" + PlusCode2
        public long ProcessedWayID;  //FK to the other table, in case I need to recalculate these?
    }

    //An attempt at storing the data as efficiently as possible.
    //Ways from OSM are reduced to rectangular abstractions.
    //These get read and processed into InterestingPoint entries.
    public class ProcessedWays
    {
        public long ProccessedWayId;
        public long OsmWayId; //possibly optiona, if space efficiency is an issue.
        public double latitudeS;
        public double longitudeW;
        public double distanceE;
        public double distanceN;
        public DateTime lastUpdated; //potentially optional, if space efficiency is an issue.
    }

}
