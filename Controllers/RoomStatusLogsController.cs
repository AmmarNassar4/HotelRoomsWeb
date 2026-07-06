using HotelRoomsWeb.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

namespace HotelRoomsWeb.Controllers
{
    [Authorize]
    public class RoomStatusLogsController : Controller
    {
        private readonly AppUserStore _userStore;

        public RoomStatusLogsController(IConfiguration configuration, IWebHostEnvironment environment)
        {
            _userStore = new AppUserStore(configuration, environment);
        }

        [HttpGet]
        public IActionResult Index()
        {
            var model = _userStore.GetAllRoomStatusHistory(1000);
            return View(model);
        }
    }
}
