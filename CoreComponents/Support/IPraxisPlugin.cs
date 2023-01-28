using System;

namespace PraxisCore.Support
{
    public interface IPraxisPlugin
    {
    }

    public interface IPraxisStartup
    {
        public static void Startup() => throw new NotImplementedException();
    }

}
