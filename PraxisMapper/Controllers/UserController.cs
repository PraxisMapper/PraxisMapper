using Microsoft.AspNetCore.Mvc;

namespace PraxisMapper.Controllers
{
    public class UserController : Controller
    {
        //This is the interface a non-admin user should get when hitting the web view.
        //They should be able to login, see and download their info, delete everything.
        
        //This will need to be made an exception to PraxisAuth, since they need this page to get in.
        //PraxisAuth may need expanded to check cookies too, since this page means we now handle browsers.
        public IActionResult Index()
        {
            return View();
        }
    }
}
