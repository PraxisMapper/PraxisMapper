using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using PraxisCore;
using System;
using System.Buffers;

namespace PraxisMapper.Controllers {
    /// <summary>
    /// The SecureData endpoints were intended to allow a client-driven app to be able to store data securely on a server.
    /// This is one of the earliest bits of the server developed, and the utility area for these is now rather small. 
    /// It may have some use for early prototyping, allowing a client to save and load data that connects a player to a place or area
    /// to prove a concept out, but a proper server-authoritative game should have a custom plugin that calls the appropriate GenericData
    /// secure data functions instead, using the player's internal password (See PraxisDemoPlugin/UnroutineController for examples).
    /// A second alternative for a client-authoritative game would be to allow clients to communicate directly with each other, rather than
    /// relaying sensitive data through a server. In general, consider this endpoint deprecated for any serious development.
    /// </summary>
    [Route("[controller]")]
    [ApiController]
    public class SecureDataController : Controller {
        private readonly IConfiguration Configuration;
        private static IMemoryCache cache;

        public SecureDataController(IConfiguration config, IMemoryCache memoryCacheSingleton) {
            Configuration = config;
            cache = memoryCacheSingleton;
        }

        public override void OnActionExecuting(ActionExecutingContext context) {
            base.OnActionExecuting(context);
            if (Configuration.GetValue<bool>("enableDataEndpoints") == false)
                HttpContext.Abort();
        }

        /// <summary>
        /// Save data to a Place securely.
        /// </summary>
        /// <param name="elementId">The privacyID(GUID) to save data to</param>
        /// <param name="key">The key to save data to</param>
        /// <param name="value">The value to save </param>
        /// <param name="password">The password to use when saving data. </param>
        /// <param name="expiresIn">If set, this value will expire this many seconds after saving.</param>
        /// <returns>true if the data was saved</returns>
        /// <remarks>These endpoints allow a client game to write data without a game-specific plugin. This is most useful for prototyping and early development.
        /// Most server-side game code should be done in a custom plugin, directly calling the function in GenericData. See PraxisDemos/UnroutineController for an example of how to use these.
        /// This endpoint MUST be used if you want to save data on a player to a place.
        /// This can allow players to save their own specific data to a place by using distinct password values.
        /// The key should NOT be the player's account ID, since that connects the player to the place.
        /// </remarks>
        [HttpPut]
        [Route("/[controller]/SetSecureElementData/{elementId}/{key}/{value}/{password}")]
        [Route("/[controller]/SetSecureElementData/{elementId}/{key}/{value}/{password}/{expiresIn}")]
        [Route("/[controller]/Element/{elementId}/{key}/{value}/{password}")]
        [Route("/[controller]/Element/{elementId}/{key}/{value}/{password}/{expiresIn}")]
        [Route("/[controller]/Place/{elementId}/{key}/{password}")]
        [Route("/[controller]/Place/{elementId}/{key}/{value}/{password}")]
        [Route("/[controller]/Place/{elementId}/{key}/{value}/{password}/{expiresIn}")]
        public bool SetSecureElementData(Guid elementId, string key, string value, string password, double? expiresIn = null) {
            //TODO: make a route that can allow password to be sent via body.
            Response.Headers.Add("X-noPerfTrack", "SecureData/Place/" + elementId.ToString() + "/VALUESREMOVED-PUT");
            SimpleLockable.PerformWithLock(elementId + "-" + key, () =>
            {
                if (value == null)
                {
                    var endData = GenericData.ReadBody(Request.BodyReader, (int)Request.ContentLength);
                    GenericData.SetSecurePlaceData(elementId, key, endData, password, expiresIn);
                }
                GenericData.SetSecurePlaceData(elementId, key, value, password, expiresIn);
            });
            return true;
        }

        /// <summary>
        /// Loads a secure value from a place.
        /// </summary>
        /// <param name="elementId">The privacyID(GUID) of the place to laod</param>
        /// <param name="key">The key to load data from</param>
        /// <param name="password">the password the data was saved with</param>
        /// <remarks>Results are written to the body of the response.
        /// Most server-side game code should be done in a custom plugin, directly calling the function in GenericData. See PraxisDemos/UnroutineController for an example of how to use these.</remarks>
        [HttpGet]
        [Route("/[controller]/Element/{elementId}/{key}/{password}")]
        [Route("/[controller]/Place/{elementId}/{key}/{password}")]
        public void GetSecureElementData(Guid elementId, string key, string password) {
            Response.Headers.Add("X-noPerfTrack", "SecureData/Place/VALUESREMOVED-GET");
            byte[] rawData = GenericData.GetSecurePlaceData(elementId, key, password);
            Response.BodyWriter.Write(rawData);
            return;
        }

        /// <summary>
        /// Set a secure value on a player's account
        /// </summary>
        /// <param name="accountId">the account id to save data to</param>
        /// <param name="key">the key to save data to</param>
        /// <param name="value">the value to save</param>
        /// <param name="password">the password to save data with</param>
        /// <param name="expiresIn">if set, this value expires this many seconds after saving</param>
        /// <returns>true if the data was saved</returns>  
        /// <remarks> Most server-side game code should be done in a custom plugin, directly calling the function in GenericData. See PraxisDemos/UnroutineController for an example of how to use these.
        /// Do not use place IDs or PlusCodes as keys, since that could reveal connections to the player.</remarks>
        [HttpPut]
        [Route("/[controller]/SetSecurePlayerData/{accountId}/{key}/{value}/{password}")]
        [Route("/[controller]/SetSecurePlayerData/{accountId}/{key}/{value}/{password}/{expiresIn}")]
        [Route("/[controller]/Player/{accountId}/{key}/{password}")]
        [Route("/[controller]/Player/{accountId}/{key}/{value}/{password}")]
        [Route("/[controller]/Player/{accountId}/{key}/{value}/{password}/{expiresIn}")]
        public bool SetSecurePlayerData(string accountId, string key, string value, string password, double? expiresIn = null) {
            Response.Headers.Add("X-noPerfTrack", "SecureData/Player/" + accountId.ToString() + "/VALUESREMOVED-PUT");

            SimpleLockable.PerformWithLock(accountId + "-" + key, () =>
            {
                if (value == null)
                {
                    var endData = GenericData.ReadBody(Request.BodyReader, (int)Request.ContentLength);
                    GenericData.SetSecurePlayerData(accountId, key, endData, password, expiresIn);
                    return;
                }
                GenericData.SetSecurePlayerData(accountId, key, value, password, expiresIn);
            });
            return true;
        }

        /// <summary>
        /// Get a secure value on a players account
        /// </summary>
        /// <param name="accountId">the account to read data from</param>
        /// <param name="key">the key to read data from</param>
        /// <param name="password">the password used to save the data</param>
        /// <remarks>Results are written to the body of the response.
        /// Most server-side game code should be done in a custom plugin, directly calling the function in GenericData. See PraxisDemos/UnroutineController for an example of how to use these.</remarks>
        [HttpGet]
        [Route("/[controller]/Player/{accountId}/{key}/{password}")]
        public void GetSecurePlayerData(string accountId, string key, string password) {
            Response.Headers.Add("X-noPerfTrack", "SecureData/Player/VALUESREMOVED-GET");
            byte[] rawData = GenericData.GetSecurePlayerData(accountId, key, password);
            Response.BodyWriter.Write(rawData);
            return;
        }

        /// <summary>
        /// Saves a value securely to a PlusCode.
        /// </summary>
        /// <param name="plusCode">The area to save data to</param>
        /// <param name="key">the key to save data to</param>
        /// <param name="value">the value to save</param>
        /// <param name="password">the password to save data with</param>
        /// <param name="expiresIn">if set, the data expires this many seconds after saving</param>
        /// <returns>true if the data was saved</returns>
        /// <remarks>Most server-side game code should be done in a custom plugin, directly calling the function in GenericData. See PraxisDemos/UnroutineController for an example of how to use these.
        /// This call MUST be used if you are saving data on players to an Area</remarks>
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
        public bool SetSecurePlusCodeData(string plusCode, string key, string value, string password, double? expiresIn = null) {
            Response.Headers.Add("X-noPerfTrack", "SecureData/Area/" + plusCode + "/VALUESREMOVED-PUT");
            if (!DataCheck.IsInBounds(plusCode))
                return false;

            SimpleLockable.PerformWithLock(plusCode + "-" + key, () =>
            {
                if (value == null)
                {
                    var endData = GenericData.ReadBody(Request.BodyReader, (int)Request.ContentLength);
                    GenericData.SetSecureAreaData(plusCode, key, endData, password, expiresIn);
                    return;
                }
                GenericData.SetSecureAreaData(plusCode, key, value, password, expiresIn);
            });
            return true;
        }

        /// <summary>
        /// Gets data from an area
        /// </summary>
        /// <param name="plusCode">the PlusCode to read data from</param>
        /// <param name="key">the key to load data from</param>
        /// <param name="password">the password used to save data</param>
        /// <remarks>Most server-side game code should be done in a custom plugin, directly calling the function in GenericData. See PraxisDemos/UnroutineController for an example of how to use these.</remarks>
        [HttpGet]
        [Route("/[controller]/GetSecurePlusCodeData/{plusCode}/{key}/{password}")]
        [Route("/[controller]/GetPlusCode/{plusCode}/{key}/{password}")]
        [Route("/[controller]/Area/{plusCode}/{key}/{password}")]
        public void GetSecurePlusCodeData(string plusCode, string key, string password) {
            Response.Headers.Add("X-noPerfTrack", "SecureData/Area/" + plusCode + "/VALUESREMOVED-GET");
            if (!DataCheck.IsInBounds(plusCode))
                return;

            byte[] rawData = GenericData.GetSecureAreaData(plusCode, key, password);
            Response.BodyWriter.Write(rawData);
            return;
        }

        /// <summary>
        /// Increments a secure value on a Place
        /// </summary>
        /// <param name="elementId">the privacyID(GUID) for the place</param>
        /// <param name="key">the key to increment</param>
        /// <param name="password">the password used to save data</param>
        /// <param name="changeAmount">How much to adjust the value by.</param>
        /// <param name="expirationTimer">if supplied, how many seconds the data will last for after saving.</param>
        /// <remarks>Most server-side game code should be done in a custom plugin, directly calling the function in GenericData. See PraxisDemos/UnroutineController for an example of how to use these.</remarks>
        [HttpPut]
        [Route("/[controller]/Place/Increment{elementId}/{key}/{password}/{changeAmount}/{expirationTimer}")]
        [Route("/[controller]/Place/Increment{elementId}/{key}/{password}/{changeAmount}")]
        public void IncrementSecureElementData(Guid elementId, string key, string password, double changeAmount, double? expirationTimer = null) {
            Response.Headers.Add("X-noPerfTrack", "SecureData/Place/Increment" + elementId.ToString() + "/VALUESREMOVED");
            GenericData.IncrementSecurePlaceData(elementId, key, password, changeAmount, expirationTimer);
        }

        /// <summary>
        /// Increments a secure value on a Player
        /// </summary>
        /// <param name="playerId">the account id to use</param>
        /// <param name="key">the key to increment</param>
        /// <param name="password">the password used to save data</param>
        /// <param name="changeAmount">how much to change the value by</param>
        /// <param name="expirationTimer">if provided, the data will expire this many seconds after saving</param>
        /// <remarks>
        /// Most server-side game code should be done in a custom plugin, directly calling the function in GenericData. See PraxisDemos/UnroutineController for an example of how to use these.
        /// </remarks>
        [HttpPut]
        [Route("/[controller]/Player/Increment{playerId}/{key}/{password}/{changeAmount}/{expirationTimer}")]
        [Route("/[controller]/Player/Increment{playerId}/{key}/{password}/{changeAmount}")]
        public void IncrementSecurePlayerData(string playerId, string key, string password, double changeAmount, double? expirationTimer = null) {
            Response.Headers.Add("X-noPerfTrack", "SecureData/Player/Increment/VALUESREMOVED");
            GenericData.IncrementSecurePlayerData(playerId, key, password, changeAmount, expirationTimer);
        }

        /// <summary>
        /// Increments a value on an Area
        /// </summary>
        /// <param name="plusCode">the PlusCode to change data on</param>
        /// <param name="key">the key to change</param>
        /// <param name="password">the password used to save the data</param>
        /// <param name="changeAmount">how much to increment the value by</param>
        /// <param name="expirationTimer">If set, the data will expire this many seconds after saving.</param>
        /// <remarks>
        /// Most server-side game code should be done in a custom plugin, directly calling the function in GenericData. See PraxisDemos/UnroutineController for an example of how to use these.
        /// </remarks>
        [HttpPut]
        [Route("/[controller]/Area/Increment{plusCode}/{key}/{password}/{changeAmount}/{expirationTimer}")]
        [Route("/[controller]/Area/Increment{plusCode}/{key}/{password}/{changeAmount}")]
        public void IncrementSecureAreaData(string plusCode, string key, string password, double changeAmount, double? expirationTimer = null) {
            Response.Headers.Add("X-noPerfTrack", "SecureData/Area/Increment" + plusCode + "/VALUESREMOVED");
            if (!DataCheck.IsInBounds(plusCode))
                return;

            GenericData.IncrementSecureAreaData(plusCode, key, password, changeAmount, expirationTimer);
        }
    }
}