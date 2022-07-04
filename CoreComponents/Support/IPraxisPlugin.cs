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
        //public abstract List<string> AuthWhiteList { get; set; }
    }

    public interface IPraxisStartup
    {
        public static void Startup() => throw new NotImplementedException();
    }

}
