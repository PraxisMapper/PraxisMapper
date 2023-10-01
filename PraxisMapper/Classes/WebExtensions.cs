using Microsoft.AspNetCore.Http;
using PraxisCore;
using System.Linq;
using System.Net;

namespace PraxisMapper.Classes {
    public static class WebExtensions {
        /// <summary>
        /// Determines if the requesting host is the same as the PraxisMapper server.
        /// </summary>
        /// <param name="host"></param>
        /// <returns></returns>
        public static bool IsLocalIpAddress(this HostString host) {
            IPAddress[] hostIPs = Dns.GetHostAddresses(host.Host);
            IPAddress[] localIPs = Dns.GetHostAddresses(Dns.GetHostName());

            return hostIPs.Any(h => IPAddress.IsLoopback(h) || localIPs.Any(l => h.Equals(l)));
        }

        /// <summary>
        /// Reads the body of an HttpRequest as a string.
        /// </summary>
        /// <param name="r"></param>
        /// <returns></returns>
        public static string ReadBody(this HttpRequest r) {
            return GenericData.ReadBody(r.BodyReader, r.ContentLength).ToUTF8String();
        }

        /// <summary>
        /// Reads the body of an HttpRequest, and casts it from JSON to the requested type T.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="r"></param>
        /// <returns></returns>
        public static T ReadBody<T>(this HttpRequest r) {
            return GenericData.ReadBody(r.BodyReader, r.ContentLength).ToUTF8String().FromJsonTo<T>();
        }
    }
}
