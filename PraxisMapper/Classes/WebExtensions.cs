using Microsoft.AspNetCore.Http;
using PraxisCore;
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

        public static string ReadBody(this HttpRequest r)
        {
            return GenericData.ReadBody(r.BodyReader, r.ContentLength).ToUTF8String();
        }

        public static T ReadBody<T>(this HttpRequest r)
        {
            return GenericData.ReadBody(r.BodyReader, r.ContentLength).ToUTF8String().FromJsonTo<T>();
        }
    }
}
