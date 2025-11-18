using System.Collections.Generic;

namespace HotelRoomsWeb.Models
{
    /// <summary>
    /// نموذج لوحة المؤشرات.
    /// </summary>
    public class RoomDashboardViewModel
    {
        public List<RoomGuestViewModel> Rooms { get; set; } = new();
        public List<string> RoomTypes { get; set; } = new();
    }
}