//using Microsoft.AspNetCore.Mvc;
//using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Threading.Tasks;

//namespace PraxisMapper.Controllers
//{
//    [Route("[controller]")]
//    [ApiController]
//    public class CreatureCollectorController : Controller
//    {
//        //CreatureCollector will be an example gameplay mode where you can interact with each thing once a day.
//        //You will walk up to an interactible area, do something on the client side, and get some rewards.
//        //Each interactible area will have additional properties that affect the rewards.
//        //Will areas have types pre-assigned, or will they be assigned on demand?
//        //The game itself needs to do the battle/catch/whatever, this just handles the server side of things.

//        //Needs a few tables
//        //one for things to find in gameplay and their baseline definition
//        //a second to define how often those things show up or whatnot.
//        //a third settings table for basic stuff?
//        //Also, rules for the creatures and how often they show up

//        //Research Notes:
//        /* For reference: the tags Pokemon Go appears to be using. I don't need all of these. I have a few it doesn't, as well.
//         * Pokemon Go has 58 types of areas it cares about as far as maptiles go. It may also use these to determine small spawn rate changes
//         * and for reference, the Google Maps Playable Locations valid types (Interaction points, not terrain types?)
//         *  education
//            entertainment
//            finance
//            food_and_drink
//            outdoor_recreation
//            retail
//            tourism
//            transit
//            transportation_infrastructure
//            wellness

//        My defaults here current have 8 categories of IsGameArea
//        (Wetlands, beach, university, nature reserve, cemetery, tourism, historical, trail)

//         */

//        public IActionResult Index()
//        {
//            return View();
//        }
//    }
//}
