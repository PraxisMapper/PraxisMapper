using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Caching.Memory;
using System;

namespace PraxisMapper.Classes
{
    public static class PraxisCacheHelper
    {
        public static IMemoryCache cache; //Set during startup.
        public static bool CheckCache<T>(string key, out T results) where T : class
        {
            if (cache.TryGetValue(key, out results))
                return true;

            return false;
        }

        public static void CheckCache(this ActionExecutingContext context, string url, string accountId)
        {
            //Results can be stored by URL or by User+URL, so do 2 checks.

            if (cache.TryGetValue(url, out IActionResult results))
                context.Result = results;

            if (cache.TryGetValue(accountId + "-" + url, out results))
                context.Result = results;
        }

        public static void SetCache(string key, object value, int secondsToSave)
        {
            cache.Set(key, value, new DateTimeOffset(DateTime.Now.AddSeconds(secondsToSave)));
        }
    }
}
