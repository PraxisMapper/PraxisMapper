using Google.OpenLocationCode;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PraxisCore;
using PraxisCore.Support;

namespace PraxisScavengerHuntPlugin.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class ScavengerHuntController : Controller, IPraxisPlugin, IPraxisStartup
    {
        public static void Startup()
        {
            //Check and make new DB tables.
            var db = new ScavengerHuntContext();
            //Doing this manually since the EF's automatic stuff does not cover inheriting an existing context to add stuff on automatically.
            db.CheckAndCreateTables();
        }

        public void GenerateAllEntries()
        {
            //This is run by an admin, not automatically.

            //Scan placeTags for entries that are a gameplay area.
            //Additionally scan for a wikipedia entry.
            var db = new ScavengerHuntContext();
            Dictionary<string, ScavengerHunt> hunts = new Dictionary<string, ScavengerHunt>();

            foreach (var gameElementTags in TagParser.allStyleGroups.First().Value.Where(s => s.Value.IsGameElement))
            {
                hunts.Add(gameElementTags.Key, new ScavengerHunt() { name = gameElementTags.Key });
            }

            //Places will be done in blocks. Can skip geometry data.
            var skip = 0;
            var take = 10000;
            var keepProcessing = true;
            while (keepProcessing)
            {
                var places = db.Places.Include(a => a.Tags).Skip(skip).Take(take).Select(a => new { a.PrivacyId, a.Tags }).ToList();
                if (places.Count < take)
                    keepProcessing = false;
                skip += take;

                foreach (var gameElementTags in TagParser.allStyleGroups.First().Value.Where(s => s.Value.IsGameElement))
                {
                    var foundElements = places.Where(a => TagParser.MatchOnTags(gameElementTags.Value, a.Tags) && !string.IsNullOrWhiteSpace(TagParser.GetPlaceName(a.Tags))).Select(a => TagParser.GetPlaceName(a.Tags)).Distinct().ToList();
                    db.ScavengerHuntEntries.Add(new ScavengerHuntEntry() { description = gameElementTags.Key, ScavengerHunt = hunts[gameElementTags.Key], StoredOsmElementId = foundElements.FirstOrDefault() }); //TODO: this is name, not privacyid.
                    Log.WriteLog(foundElements.Count + " " + gameElementTags.Value.Name + " items found for scavenger hunt.");
                }
            }


            var wikiPlaces = db.PlaceTags.Include(a => a.Place).Where(a => a.Key == "wikipedia").Select(a => a.Place).Distinct().ToList();
        }

        public void Generate(string plusCode) //plusCode is a region to focus on.
        {
            var db = new ScavengerHuntContext();
            var results = new List<ScavengerHunt>();

            var places = Place.GetPlaces(plusCode.ToGeoArea());

            var wikiList = places.Where(a => a.Tags.Any(t => t.Key == "wikipedia") && TagParser.GetPlaceName(a.Tags) != "").Select(a => TagParser.GetPlaceName(a.Tags)).Distinct().ToList();
            //Create automatic scavenger hunt entries.
            Dictionary<string, List<string>> scavengerHunts = new Dictionary<string, List<string>>();

            //NOTE:
            //If i run this by elementID, i get everything unique but several entries get duplicated becaues they're in multiple pieces.
            //If I run this by name, the lists are much shorter but visiting one distinct location might count for all of them (This is a bigger concern with very large areas or retail establishment)
            //So I'm going to run this by name for the player's sake. 
            scavengerHunts.Add("Wikipedia Places", wikiList);
            Log.WriteLog(wikiList.Count + " Wikipedia-linked items found for scavenger hunt.");

            //fill in gameElement lists.
            foreach (var gameElementTags in TagParser.allStyleGroups.First().Value.Where(s => s.Value.IsGameElement))
            {
                var foundElements = places.Where(a => TagParser.MatchOnTags(gameElementTags.Value, a.Tags) && !string.IsNullOrWhiteSpace(TagParser.GetPlaceName(a.Tags))).Select(a => TagParser.GetPlaceName(a.Tags)).Distinct().ToList();
                scavengerHunts.Add(gameElementTags.Value.Name, foundElements);
                Log.WriteLog(foundElements.Count + " " + gameElementTags.Value.Name + " items found for scavenger hunt.");
            }

            //foreach (var hunt in scavengerHunts)
            //{
            //    foreach (var item in hunt.Value)
            //        results.Add(new ScavengerHuntEntry() { ScavengerHunt = hunt, description = item });
            //}
            
            //return results;
        }

        public List<ScavengerHunt> GetLists()
        {
            var db = new ScavengerHuntContext();
            return db.ScavengerHunts.Include(s => s.entries).ToList();
        }

        public string Enter(string plusCode)
        {
            Response.Headers.Add("X-noPerfTrack", "ScavengerHunt/Enter/VARSREMOVED");
            if (!DataCheck.IsInBounds(plusCode))
                return null;

            GeoArea playerCell = OpenLocationCode.DecodeValid(plusCode);
            GeoArea radius = new GeoArea(playerCell.SouthLatitude - (ConstantValues.resolutionCell10 * 2)- .000001, playerCell.WestLongitude - (ConstantValues.resolutionCell10 * 2) - .000001, playerCell.NorthLatitude + (ConstantValues.resolutionCell10 * 2) + .000001, playerCell.EastLongitude + (ConstantValues.resolutionCell10 * 2) + .000001);

            //TODO: set up foreign key connection to Place table.
            //var db = new ScavengerHuntContext();
            //var entries = db.ScavengerHuntEntries.Where(s => s.)

            return "";
        }
    }
}
