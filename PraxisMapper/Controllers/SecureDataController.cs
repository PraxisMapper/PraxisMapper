using PraxisCore;
using Google.OpenLocationCode;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using NetTopologySuite.Geometries.Prepared;
using PraxisMapper.Classes;
using System;
using System.Text;
using static PraxisCore.DbTables;
using static PraxisCore.Place;
using System.Linq;

namespace PraxisMapper.Controllers
{
    [Route("[controller]")]
    [ApiController]
    public class SecureDataController : Controller
    {
        private readonly IConfiguration Configuration;
        private static IMemoryCache cache;

        public SecureDataController(IConfiguration config, IMemoryCache memoryCacheSingleton)
        {
            Configuration = config;
            cache = memoryCacheSingleton;
        }


        //Reminder: BCrypt is for passwords, and is a one-way hash. This can't retreive an unknown value. Need a symmetric crypto plan for that.


    }
}
