using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PraxisCore.Support
{
    public interface IPraxisPlugin
    {
        public abstract void Startup();

        //derived classes must ALSO have a parameterless public constructor, so that Startup() can be found by PraxisMapper.
    }
}
