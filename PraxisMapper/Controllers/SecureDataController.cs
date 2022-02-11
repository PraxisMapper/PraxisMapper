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
using CryptSharp;
using System.IO.Pipelines;
using System.Threading.Tasks;

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

        [HttpGet]
        [Route("/[controller]/SetSecureElementData/{elementId}/{key}/{value}/{password}")]
        [Route("/[controller]/SetSecureElementData/{elementId}/{key}/{value}/{password}/{expiresIn}")]
        [Route("/[controller]/SetElement/{elementId}/{key}/{value}/{password}")]
        [Route("/[controller]/SetElement/{elementId}/{key}/{value}/{password}/{expiresIn}")]
        public bool SetSecureElementData(Guid elementId, string key, string value,string password, double? expiresIn = null)
        {
            return GenericData.SetSecureElementData(elementId, key, value, password, expiresIn);
        }

        [HttpGet]
        [Route("/[controller]/GetSecureElementData/{elementId}/{key}/{password}")]
        public string GetSecureElementData(Guid elementId, string key, string password)
        {
            return GenericData.GetSecureElementData(elementId, key, password);
        }

        [HttpGet]
        [Route("/[controller]/SetSecurePlayerData/{deviceId}/{key}/{value}/{password}")]
        [Route("/[controller]/SetSecurePlayerData/{deviceId}/{key}/{value}/{password}/{expiresIn}")]
        [Route("/[controller]/SetPlayer/{deviceId}/{key}/{value}/{password}")]
        [Route("/[controller]/SetPlayer/{deviceId}/{key}/{value}/{password}/{expiresIn}")]
        public bool SetSecurePlayerData(string deviceId, string key, string value, string password, double? expiresIn = null)
        {
            return GenericData.SetSecurePlayerData(deviceId, key, value, password, expiresIn);
        }

        [HttpGet]
        [Route("/[controller]/GetSecurePlayerData/{deviceId}/{key}/{password}")]
        [Route("/[controller]/GetPlayer/{deviceId}/{key}/{password}")]
        public string GetSecurePlayerData(string deviceId, string key, string password)
        {
            return GenericData.GetSecurePlayerData(deviceId, key, password);
        }

        [HttpGet]
        [Route("/[controller]/SetSecurePlusCodeData/{plusCode}/{key}/{password}")] //for when value is part of the body
        [Route("/[controller]/SetSecurePlusCodeData/{plusCode}/{key}/{value}/{password}")]
        [Route("/[controller]/SetSecurePlusCodeData/{plusCode}/{key}/{value}/{password}/{expiresIn}")]
        [Route("/[controller]/SetPlusCode/{plusCode}/{key}/{password}")]
        [Route("/[controller]/SetPlusCode/{plusCode}/{key}/{value}/{password}")]
        [Route("/[controller]/SetPlusCode/{plusCode}/{key}/{value}/{password}/{expiresIn}")]
        public async Task<bool> SetSecurePlusCodeDataAsync(string plusCode, string key, string value, string password, double? expiresIn = null)
        {
            if (!DataCheck.IsInBounds(cache.Get<IPreparedGeometry>("serverBounds"), OpenLocationCode.DecodeValid(plusCode)))
                return false;
            if (value == null)
            {
                //var rr = Request.BodyReader.ReadAsync();
                //var r2 = rr.Result.Buffer; //actual byte data.
                //var r3 = Encoding.ASCII.GetString(r2); //same string as value gets normally from the path below.

                //Request.Body.Seek(0, System.IO.SeekOrigin.Begin);
                var sr = new System.IO.StreamReader(Request.Body);
                value = await sr.ReadToEndAsync(); //974bytes this way.
            }
            return GenericData.SetSecurePlusCodeData(plusCode, key, value, password, expiresIn);
        }

        [HttpGet]
        [Route("/[controller]/SetSecurePlusCodeFile/{plusCode}/{key}/{password}")] //for when value is part of the body
        [Route("/[controller]/SetPlusCodeFile/{plusCode}/{key}/{password}")] //for when value is part of the body
        public async Task<bool> SetSecurePlusCodeDataAsFile(string plusCode, string key, [FromBody] byte[]value, string password, double? expiresIn = null)
        {
            if (!DataCheck.IsInBounds(cache.Get<IPreparedGeometry>("serverBounds"), OpenLocationCode.DecodeValid(plusCode)))
                return false;
            if (value == null)
            {
                //var rr = Request.BodyReader.ReadAsync();
                //var r2 = rr.Result.Buffer; //actual byte data.
                //var r3 = Encoding.ASCII.GetString(r2); //same string as value gets normally from the path below.

                //Request.Body.Seek(0, System.IO.SeekOrigin.Begin);
            }
            return GenericData.SetSecurePlusCodeData(plusCode, key, value, password, expiresIn);
        }

        [HttpGet]
        [Route("/[controller]/GetSecurePlusCodeData/{plusCode}/{key}/{password}")]
        [Route("/[controller]/GetPlusCode/{plusCode}/{key}/{password}")]
        public string GetSecurePlusCodeData(string plusCode, string key, string password)
        {
            if (!DataCheck.IsInBounds(cache.Get<IPreparedGeometry>("serverBounds"), OpenLocationCode.DecodeValid(plusCode)))
                return "";
            return GenericData.GetSecurePlusCodeData(plusCode, key, password);
        }

        [HttpGet]
        [Route("/[controller]/EncryptUserPassword/{devicedId}/{password}")]
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
        public bool CheckPassword(string deviceId, string password)
        {
            BlowfishCrypter crypter = new BlowfishCrypter();
            string existingPassword = GenericData.GetPlayerData(deviceId, "password");
            string checkedPassword = crypter.Crypt(password, existingPassword);
            return existingPassword == checkedPassword;
        }
    }
}
