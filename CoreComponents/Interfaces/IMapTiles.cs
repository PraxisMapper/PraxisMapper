using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CoreComponents.Interfaces
{
    interface IMapTiles
    {
        //Currently PraxisMapper draws map tiles itself.
        //This interface would allow for other ways to acquire map tiles.
        //No curernt plans to replace my current setup, and no idea what other package would generate nice map tiles for this in a way PraxisMapper can use.
        //But I'm currently planning out future enhancement options, and this is one, so the empty class to remind me was created.
    }
}
