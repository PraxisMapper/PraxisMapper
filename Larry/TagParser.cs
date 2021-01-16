using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Larry
{
    class TagParser
    {
        //For reading values out of the database and parsing them against the PBF data source.
        //Tag values are stored in a TagToParse class.
        

        //string 
    }

    class TagToParse
    {
        //Needs:
        //standard tag syntax per OSM (highway:road)
        //Wildcard values use * (highway:*)
        //needs AND support (natural:water&&water:lake)
        //needs OR support (natural:forest||natural:woodland)
        //needs NOT support(highway:path!!highway:sidewalk)
        //nesting these is harder. might have to split nested setups into multiple entries with the same name and type.
        string rawData;
        string includes;
        string excludes;
    }

}
