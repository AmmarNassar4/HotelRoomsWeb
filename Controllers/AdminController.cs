using HotelRoomsWeb.Models;
using HotelRoomsWeb.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace HotelRoomsWeb.Controllers
{
    [Authorize]
    public class AdminController : Controller
    {
        private readonly AppUserStore _userStore;

        public AdminController(IConfiguration configuration, IWebHostEnvironment environment)
        {
            _userStore = new AppUserStore(configuration, environment);
        }

        [HttpGet]
        public IActionResult Users()
        {
            if (!CurrentUserIsAdmin())
            {
                return Forbid();
            }

            var vm = new AdminUsersViewModel
            {
                Users = _userStore.GetUsers()
            };

            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult CreateUser(CreateUserViewModel newUser)
        {
            if (!CurrentUserIsAdmin())
            {
                return Forbid();
            }

            if (!ModelState.IsValid)
            {
                var vm = new AdminUsersViewModel
                {
                    Users = _userStore.GetUsers(),
                    NewUser = newUser
                };
                return View("Users", vm);
            }

            try
            {
                _userStore.CreateUser(newUser);
                TempData["SuccessMessage"] = "User added successfully.";
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
            {
                TempData["ErrorMessage"] = "Username already exists.";
            }

            return RedirectToAction(nameof(Users));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult UpdateUserPermissions(long id, bool isActive, bool isAdmin, bool canChangeRoomStatus)
        {
            if (!CurrentUserIsAdmin())
            {
                return Forbid();
            }

            _userStore.UpdateUserPermissions(id, isActive, isAdmin, canChangeRoomStatus);
            TempData["SuccessMessage"] = "User permissions updated.";
            return RedirectToAction(nameof(Users));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult ChangeUserPassword(ChangeUserPasswordViewModel model)
        {
            if (!CurrentUserIsAdmin())
            {
                return Forbid();
            }

            if (!ModelState.IsValid || string.IsNullOrWhiteSpace(model.NewPassword))
            {
                TempData["ErrorMessage"] = "Please enter a valid new password. Minimum length is 4 characters.";
                return RedirectToAction(nameof(Users));
            }

            var updated = _userStore.UpdateUserPassword(model.Id, model.NewPassword.Trim());
            if (!updated)
            {
                TempData["ErrorMessage"] = "User was not found. Password was not changed.";
                return RedirectToAction(nameof(Users));
            }

            TempData["SuccessMessage"] = $"Password changed successfully for user {model.UserName}.";
            return RedirectToAction(nameof(Users));
        }

        private bool CurrentUserIsAdmin()
        {
            return User.HasClaim("IsAdmin", "true");
        }
    }
}
