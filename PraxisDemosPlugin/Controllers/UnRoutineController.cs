using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Identity.Client;
using NetTopologySuite.Operation.Overlay;
using NetTopologySuite.Triangulate;
using PraxisCore;
using PraxisCore.Support;
using PraxisMapper.Classes;

namespace PraxisDemosPlugin.Controllers
{
    [ApiController]
    [Route("/[controller]")]
    public class UnRoutineController : Controller, IPraxisPlugin
    {
        //RoutineBreaker demo. Controller named UnRoutine to shorten it up a little.
        //Saves location history for a player to find places where they haven't been before.

        //Requires a current target. Save that in player data too.

        string accountId, password;
        IConfiguration Configuration;

        public UnRoutineController(IConfiguration config)
        {
            Configuration = config;
        }

        public override void OnActionExecuting(ActionExecutingContext context)
        {
            base.OnActionExecuting(context);
            PraxisAuthentication.GetAuthInfo(Response, out accountId, out password);
        }

        [HttpPut]
        [Route("/[controller]/Enter/{plusCode10}")]
        public void Enter(string plusCode10)
        {
            var places = Place.GetPlaces(plusCode10.ToGeoArea());
            var placeTracker = GenericData.GetSecurePlayerData<HashSet<Guid>>(accountId, "placesVisitedRB", password);
            if (placeTracker == null)
                placeTracker = new HashSet<Guid>();

            foreach(var p in places)
                if (!placeTracker.Contains(p.PrivacyId))
                    placeTracker.Add(p.PrivacyId);

            GenericData.SetSecurePlayerDataJson(accountId, "placesVisitedRB", placeTracker, password);
        }

        public void FindValidAreas(string plusCode10, double minDistanceMeters, double maxDistanceMeters)
        {
            var places = Place.FindGoodTargetPlaces(plusCode10, minDistanceMeters, maxDistanceMeters, "suggestedGameplay");

            var placeTracker = GenericData.GetSecurePlayerData<HashSet<Guid>>(accountId, "placesVisitedRB", password);
            var ignoreList = GenericData.GetSecurePlayerData<HashSet<Guid>>(accountId, "placesIgnoredRB", password);
            places = places.Where(p => !placeTracker.Contains(p.PrivacyId) && !ignoreList.Contains(p.PrivacyId)).ToList();
        }

        [HttpPut]
        [Route("/[controller]/Ignore/{privacyId}")]
        public void IgnorePlace(string privacyId)
        {
            Guid placeId = new Guid(privacyId);
            var ignoreList = GenericData.GetSecurePlayerData<HashSet<Guid>>(accountId, "placesIgnoredRB", password);
            if (ignoreList.Contains(placeId))
                ignoreList.Add(placeId);

            GenericData.SetSecurePlayerDataJson(accountId, "placesIgnoredRB", ignoreList, password);
        }

        [HttpPut]
        [Route("/[controller]/Target")]
        public void GetTargetPlace()
        {
            var target = GenericData.GetSecurePlayerData<Guid>(accountId, "targetRB", password);
            if (target == null)
                return;

            var place = Place.GetPlace(target);
        }

        [HttpPut]
        [Route("/[controller]/Image/{placeId}")]
        public FileResult GetTargetImage(string placeId)
        {
            var place = Place.GetPlace(placeId);

            //We will lock this preview image to a 900x900px box.
            var imageArea = place.ElementGeometry.Envelope.ToGeoArea();
            ImageStats istats = new ImageStats(imageArea, (int)(imageArea.LongitudeWidth / ConstantValues.resolutionCell11Lon) * (int)MapTileSupport.GameTileScale, (int)(imageArea.LatitudeHeight / ConstantValues.resolutionCell11Lat) * (int)MapTileSupport.GameTileScale);
            istats = MapTileSupport.ScaleBoundsCheck(istats, 900, 810000);

            var tile = MapTileSupport.MapTiles.DrawAreaAtSize(istats);
            return File(tile, "image/png");
        }
    }
}
