using Google.OpenLocationCode;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using PraxisCore;
using PraxisCore.Support;
using PraxisMapper.Classes;
using System.Dynamic;
using System.Text;
using System.Text.Json;

namespace PraxisDemosPlugin.Controllers
{
    public class VisitedPlace
    {
        public Guid placeId;
        public string Name;
        public string Category;
    }

    public class PlayerSettings
    {
        public int distancePref { get; set; } = 3;  //1 is short, 2 is medium, 3 is long. THey're group, not discrete values.
        public string categories { get; set; } = ""; //names of styles the player wants to go to.
    }

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
            var placeTracker = GenericData.GetSecurePlayerData<List<VisitedPlace>>(accountId, "placesVisitedRB", password);
            if (placeTracker == null)
                placeTracker = new List<VisitedPlace>();

            foreach(var p in places)
                if (!placeTracker.Any(pt => pt.placeId == p.PrivacyId))
                    placeTracker.Add(new VisitedPlace() { placeId = p.PrivacyId, Name = TagParser.GetName(p), Category = p.StyleName });

            GenericData.SetSecurePlayerDataJson(accountId, "placesVisitedRB", placeTracker, password);
        }

        public List<DbTables.Place> FindValidPlaces(string plusCode10, double minDistanceMeters, double maxDistanceMeters)
        {
            var places = Place.FindGoodTargetPlaces(plusCode10, minDistanceMeters, maxDistanceMeters, "suggestedGameplay");

            var placeTracker = GenericData.GetSecurePlayerData<List<VisitedPlace>>(accountId, "placesVisitedRB", password);
            if (placeTracker == null)
                placeTracker = new List<VisitedPlace>();
            var ignoreList = GenericData.GetSecurePlayerData<HashSet<Guid>>(accountId, "placesIgnoredRB", password);
            if (ignoreList == null)
                ignoreList = new HashSet<Guid>();

            //NOTE: some trails are unnamed in OSM, which could remove otherwise good choices. On the other hand, those unnamed
            //trails could also just be sidewalks more often than not.
            places = places.Where(p => !placeTracker.Any(pt => pt.placeId == p.PrivacyId) && !ignoreList.Contains(p.PrivacyId) && TagParser.GetName(p) != "").ToList();

            return places;
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

        //GET /Target gives you the data on your target location
        //PUT /Target/plusCode10 asks the server to set one for you.
        [HttpGet]
        [Route("/[controller]/Target")]
        public string GetTargetPlace()
        {
            var target = GenericData.GetSecurePlayerData<Guid>(accountId, "targetRB", password);
            if (target == Guid.Empty)
                return "";

            var place = Place.GetPlace(target);
            var placeData = new { Name = TagParser.GetName(place), Category = place.StyleName, Location = new OpenLocationCode(place.ElementGeometry.Centroid.Centroid.Y, place.ElementGeometry.Centroid.X).Code };
            return JsonSerializer.Serialize(placeData);
        }

        [HttpPut]
        [Route("/[controller]/Target/{plusCode10}")]
        public string PickTargetPlace(string plusCode10)
        {
            var settings = GenericData.GetPlayerData<PlayerSettings>(accountId, "rbSettings");
            if (settings == null)
                settings = new PlayerSettings() { distancePref = 3, categories = "all" };
            
            int minDistance = 1;
            int maxDistance = 2;
            switch (settings.distancePref)
            {
                case 1: //1-2km. People walk at about 1.4 m/s. Set this to be a 15-30 minute walk away. So 1-2 kilometers.
                    minDistance = 1260;
                    maxDistance = 2520;
                    break;
                case 2: //14-28km. For shorter car trips, 35 MPH is 15.6 m/s this is a 15-30 minute car drive on local roads.
                    minDistance = 14040;
                    maxDistance = 28080;
                    break;
                case 3: //50-200Km away. Up for big adventures. 65 MPH is 29 m/s. This is a 30-120 minute car drive on highways.
                    minDistance = 52250;
                    maxDistance = 208800;
                    break;
            }

            var places = FindValidPlaces(plusCode10, minDistance, maxDistance);
            places = places.Where(p => settings.categories == "all" || settings.categories.Contains(p.StyleName)).ToList();
            var place = places.PickOneRandom();

            if (place == null)
                return JsonSerializer.Serialize(new { Name = "No Place Found" });


            return JsonSerializer.Serialize(new { Name = TagParser.GetName(place), Category = place.StyleName, PlaceId = place.PrivacyId, Location = new OpenLocationCode(place.ElementGeometry.Centroid.Centroid.Y, place.ElementGeometry.Centroid.X).Code });

        }

        [HttpGet]
        [Route("/[controller]/Image/{placeId}")]
        public FileResult GetTargetImage(string placeId)
        {
            var place = Place.GetPlace(placeId);

            //We will lock this preview image to a 900x900px box.
            
            var imageArea = place.ElementGeometry.Envelope.ToGeoArea();
            if (place.ElementGeometry.GeometryType == "Point")
                imageArea = place.ElementGeometry.Buffer(.00125).Envelope.ToGeoArea();

            var longerSide = imageArea.LatitudeHeight >= imageArea.LongitudeWidth ? imageArea.LatitudeHeight : imageArea.LongitudeWidth;
            ImageStats istats = new ImageStats(imageArea, (int)(longerSide / ConstantValues.resolutionCell11Lon) * (int)MapTileSupport.GameTileScale, (int)(longerSide / ConstantValues.resolutionCell11Lat) * (int)MapTileSupport.GameTileScale);
            istats = MapTileSupport.ScaleBoundsCheck(istats, 900, 810000);

            var tile = MapTileSupport.MapTiles.DrawAreaAtSize(istats);
            return File(tile, "image/png");
        }

        [HttpGet]
        [Route("/[controller]/Visited")]
        public string GetVisitedPlaces()
        {
            //The player will probably want to see a list of places they've been, possibly organized by category.
            var placeTracker = GenericData.GetSecurePlayerData<List<VisitedPlace>>(accountId, "placesVisitedRB", password);
            return JsonSerializer.Serialize(placeTracker);
        }

        [HttpGet]
        [Route("/[controller]/Options")]
        public string GetOptions()
        {
            var settings = GenericData.GetPlayerData<PlayerSettings>(accountId, "rbSettings");
            return JsonSerializer.Serialize(settings);
        }

        [HttpPut]
        [Route("/[controller]/Options/{options}")]
        public void SetOptions(string options)
        {
            var settings = GenericData.GetPlayerData<PlayerSettings>(accountId, "rbSettings");
            if (settings == null)
                settings = new PlayerSettings() { distancePref = 3, categories = "-park-university-natureReserve-cemetery-artsCulture-tourism-historical-trail" };

            string[] newSettings = options.Split('~'); //sections are split by ~. 
            settings.categories = newSettings[0]; //categories are split by | but the individual pieces aren't checked.

            if (newSettings.Length > 1)
                settings.distancePref = newSettings[1].ToInt();

            GenericData.SetPlayerDataJson(accountId, "rbSettings", settings);
        }
    }
}
