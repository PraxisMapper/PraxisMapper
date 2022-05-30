using CryptSharp;
using Google.OpenLocationCode;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using NetTopologySuite.Geometries.Prepared;
using PraxisCore;
using System;
using System.Buffers;
using System.Linq;
using System.Threading.Tasks;

namespace PraxisMapper.Controllers
{
    [Route("[controller]")]
    [ApiController]
    public class SecureDataController : Controller
    {
        private readonly IConfiguration Configuration;
        private static IMemoryCache cache;
        private static bool perfTrackerEnabled;

        public SecureDataController(IConfiguration config, IMemoryCache memoryCacheSingleton)
        {
            Configuration = config;
            cache = memoryCacheSingleton;
            perfTrackerEnabled = Configuration.GetValue<bool>("enablePerformanceTracker");
        }

        [HttpPut]
        [Route("/[controller]/SetSecureElementData/{elementId}/{key}/{value}/{password}")]
        [Route("/[controller]/SetSecureElementData/{elementId}/{key}/{value}/{password}/{expiresIn}")]
        [Route("/[controller]/Element/{elementId}/{key}/{value}/{password}")]
        [Route("/[controller]/Element/{elementId}/{key}/{value}/{password}/{expiresIn}")]
        [Route("/[controller]/Place/{elementId}/{key}/{password}")]
        [Route("/[controller]/Place/{elementId}/{key}/{value}/{password}")]
        [Route("/[controller]/Place/{elementId}/{key}/{value}/{password}/{expiresIn}")]

        public bool SetSecureElementData(Guid elementId, string key, string value, string password, double? expiresIn = null)
        {
            if (perfTrackerEnabled)
            {
                Response.Headers.Add("X-noPerfTrack", "SecureData/Place/" + elementId.ToString() + "/VALUESREMOVED-PUT");
            }
            if (value == null)
            {
                var endData = GenericData.ReadBody(Request.BodyReader, (int)Request.ContentLength);
                return GenericData.SetSecurePlaceData(elementId, key, endData, password, expiresIn);
            }
            return GenericData.SetSecurePlaceData(elementId, key, value, password, expiresIn);
        }

        [HttpGet]
        [Route("/[controller]/Element/{elementId}/{key}/{password}")]
        [Route("/[controller]/Place/{elementId}/{key}/{password}")]
        public void GetSecureElementData(Guid elementId, string key, string password)
        {

            Response.Headers.Add("X-noPerfTrack", "SecureData/Place/VALUESREMOVED-GET");
            byte[] rawData = GenericData.GetSecurePlaceData(elementId, key, password);
            Response.BodyWriter.Write(rawData);
            return;
        }

        [HttpPut]
        [Route("/[controller]/SetSecurePlayerData/{deviceId}/{key}/{value}/{password}")]
        [Route("/[controller]/SetSecurePlayerData/{deviceId}/{key}/{value}/{password}/{expiresIn}")]
        [Route("/[controller]/Player/{deviceId}/{key}/{password}")]
        [Route("/[controller]/Player/{deviceId}/{key}/{value}/{password}")]
        [Route("/[controller]/Player/{deviceId}/{key}/{value}/{password}/{expiresIn}")]
        public bool SetSecurePlayerData(string deviceId, string key, string value, string password, double? expiresIn = null)
        {
            Response.Headers.Add("X-noPerfTrack", "SecureData/Player/" + deviceId.ToString() + "/VALUESREMOVED-PUT");

            if (value == null)
            {
                var endData = GenericData.ReadBody(Request.BodyReader, (int)Request.ContentLength);
                return GenericData.SetSecurePlayerData(deviceId, key, endData, password, expiresIn);
            }
            return GenericData.SetSecurePlayerData(deviceId, key, value, password, expiresIn);
        }

        [HttpGet]
        [Route("/[controller]/Player/{deviceId}/{key}/{password}")]
        public void GetSecurePlayerData(string deviceId, string key, string password)
        {
            Response.Headers.Add("X-noPerfTrack", "SecureData/Player/VALUESREMOVED-GET");
            byte[] rawData = GenericData.GetSecurePlayerData(deviceId, key, password);
            Response.BodyWriter.Write(rawData);
            return;
        }

        [HttpPut]
        [Route("/[controller]/SetSecurePlusCodeData/{plusCode}/{key}/{password}")] //for when value is part of the body
        [Route("/[controller]/SetSecurePlusCodeData/{plusCode}/{key}/{value}/{password}")]
        [Route("/[controller]/SetSecurePlusCodeData/{plusCode}/{key}/{value}/{password}/{expiresIn}")]
        [Route("/[controller]/PlusCode/{plusCode}/{key}/{password}")]
        [Route("/[controller]/PlusCode/{plusCode}/{key}/{value}/{password}")]
        [Route("/[controller]/PlusCode/{plusCode}/{key}/{value}/{password}/{expiresIn}")]
        [Route("/[controller]/Area/{plusCode}/{key}/{password}")]
        [Route("/[controller]/Area/{plusCode}/{key}/{value}/{password}")]
        [Route("/[controller]/Area/{plusCode}/{key}/{value}/{password}/{expiresIn}")]
        public bool SetSecurePlusCodeData(string plusCode, string key, string value, string password, double? expiresIn = null)
        {
            if (!DataCheck.IsInBounds(cache.Get<IPreparedGeometry>("serverBounds"), OpenLocationCode.DecodeValid(plusCode)))
                return false;

            Response.Headers.Add("X-noPerfTrack", "SecureData/Area/" + plusCode + "/VALUESREMOVED-PUT");
            if (value == null)
            {
                var endData = GenericData.ReadBody(Request.BodyReader, (int)Request.ContentLength);
                return GenericData.SetSecureAreaData(plusCode, key, endData, password, expiresIn);
            }
            return GenericData.SetSecureAreaData(plusCode, key, value, password, expiresIn);
        }

        [HttpGet]
        [Route("/[controller]/GetSecurePlusCodeData/{plusCode}/{key}/{password}")]
        [Route("/[controller]/GetPlusCode/{plusCode}/{key}/{password}")]
        [Route("/[controller]/Area/{plusCode}/{key}/{password}")]
        public void GetSecurePlusCodeData(string plusCode, string key, string password)
        {
            if (!DataCheck.IsInBounds(cache.Get<IPreparedGeometry>("serverBounds"), OpenLocationCode.DecodeValid(plusCode)))
                return;

            Response.Headers.Add("X-noPerfTrack", "SecureData/Area/" + plusCode + "/VALUESREMOVED-GET");
            byte[] rawData = GenericData.GetSecureAreaData(plusCode, key, password);
            Response.BodyWriter.Write(rawData);
            return;
        }
    }
}