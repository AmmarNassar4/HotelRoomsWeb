using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace HotelRoomsWeb.Models
{
    public class AppUserViewModel
    {
        public long Id { get; set; }
        public string UserName { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public bool IsAdmin { get; set; }
        public bool CanChangeRoomStatus { get; set; }
        public string CreatedAt { get; set; } = string.Empty;
    }

    public class AdminUsersViewModel
    {
        public List<AppUserViewModel> Users { get; set; } = new();
        public CreateUserViewModel NewUser { get; set; } = new();
    }

    public class CreateUserViewModel
    {
        [Required]
        [Display(Name = "Username")]
        public string UserName { get; set; } = string.Empty;

        [Required]
        [DataType(DataType.Password)]
        [Display(Name = "Password")]
        public string Password { get; set; } = string.Empty;

        public bool IsActive { get; set; } = true;
        public bool IsAdmin { get; set; }
        public bool CanChangeRoomStatus { get; set; }
    }

    public class ChangeUserPasswordViewModel
    {
        [Required]
        public long Id { get; set; }

        [Required]
        public string UserName { get; set; } = string.Empty;

        [Required]
        [MinLength(4)]
        [DataType(DataType.Password)]
        [Display(Name = "New password")]
        public string NewPassword { get; set; } = string.Empty;
    }

    public class RoomStatusHistoryViewModel
    {
        public int RoomNumber { get; set; }
        public string OldStatus { get; set; } = string.Empty;
        public string NewStatus { get; set; } = string.Empty;
        public string ChangedBy { get; set; } = string.Empty;
        public string ChangedAt { get; set; } = string.Empty;
    }
}
