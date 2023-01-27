using Microsoft.AspNetCore.Http;
using System.Linq;
using System.Net;

namespace PraxisMapper.Classes
{
    public static class WebExtensions
    {
        public static bool IsLocalIpAddress(this HostString host)
        {
            IPAddress[] hostIPs = Dns.GetHostAddresses(host.Value);
            IPAddress[] localIPs = Dns.GetHostAddresses(Dns.GetHostName());

            return hostIPs.Any(h => IPAddress.IsLoopback(h) || localIPs.Any(l => h.Equals(l)));
        }
    }
}
