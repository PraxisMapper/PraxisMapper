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

        [HttpPut]
        [Route("/[controller]/SetSecureElementData/{elementId}/{key}/{value}/{password}")]
        [Route("/[controller]/SetSecureElementData/{elementId}/{key}/{value}/{password}/{expiresIn}")]
        [Route("/[controller]/Element/{elementId}/{key}/{value}/{password}")]
        [Route("/[controller]/Element/{elementId}/{key}/{value}/{password}/{expiresIn}")]
        [Route("/[controller]/Place/{elementId}/{key}/{password}")]
        [Route("/[controller]/Place/{elementId}/{key}/{value}/{password}")]
        [Route("/[controller]/Place/{elementId}/{key}/{value}/{password}/{expiresIn}")]
        
        public bool SetSecureElementData(Guid elementId, string key, string value,string password, double? expiresIn = null)
        {
            if (value == null)
            {
                var br = Request.BodyReader;
                var rr = br.ReadAtLeastAsync((int)Request.ContentLength);
                var endData = rr.Result.Buffer.ToArray();
                return GenericData.SetSecurePlaceData(elementId, key, endData, password, expiresIn);
            }
            return GenericData.SetSecurePlaceData(elementId, key, value, password, expiresIn);
        }

        [HttpGet]
        [Route("/[controller]/Element/{elementId}/{key}/{password}")]
        [Route("/[controller]/Place/{elementId}/{key}/{password}")]
        public void GetSecureElementData(Guid elementId, string key, string password)
        {
            byte[] rawData = GenericData.GetSecurePlaceData(elementId, key, password);
            Response.BodyWriter.Write(rawData);
            Response.CompleteAsync();
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
            if (value == null)
            {
                var br = Request.BodyReader;
                var rr = br.ReadAtLeastAsync((int)Request.ContentLength);
                var endData = rr.Result.Buffer.ToArray();
                return GenericData.SetSecurePlayerData(deviceId, key, endData, password, expiresIn);
            }
            return GenericData.SetSecurePlayerData(deviceId, key, value, password, expiresIn);
        }

        [HttpGet]
        [Route("/[controller]/Player/{deviceId}/{key}/{password}")]
        public void GetSecurePlayerData(string deviceId, string key, string password)
        {
            byte[] rawData = GenericData.GetSecurePlayerData(deviceId, key, password);
            Response.BodyWriter.Write(rawData);
            Response.CompleteAsync();
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
            if (value == null)
            {
                var br = Request.BodyReader;
                var rr = br.ReadAtLeastAsync((int)Request.ContentLength);
                var endData = rr.Result.Buffer.ToArray();
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

            byte[] rawData = GenericData.GetSecureAreaData(plusCode, key, password);
            Response.BodyWriter.Write(rawData);
            Response.CompleteAsync();
            return;
        }

        [HttpPut]
        [Route("/[controller]/EncryptUserPassword/{devicedId}/{password}")]
        [Route("/[controller]/Password/{devicedId}/{password}")]
        public bool EncryptUserPassword(string deviceId, string password)
        {
            var options = new CrypterOptions() {
                { CrypterOption.Rounds, Configuration["PasswordRounds"]}
            };
            BlowfishCrypter crypter = new BlowfishCrypter();
            var salt = crypter.GenerateSalt(options);
            var results = crypter.Crypt(password, salt);
            GenericData.SetPlayerData(deviceId, "password", results);
            return true;
        }

        [HttpGet]
        [Route("/[controller]/CheckPassword/{devicedId}/{password}")]
        [Route("/[controller]/Password/{devicedId}/{password}")]
        public bool CheckPassword(string deviceId, string password)
        {
            BlowfishCrypter crypter = new BlowfishCrypter();
            string existingPassword = GenericData.GetPlayerData(deviceId, "password").ToUTF8String();
            string checkedPassword = crypter.Crypt(password, existingPassword);
            return existingPassword == checkedPassword;
        }
    }
}