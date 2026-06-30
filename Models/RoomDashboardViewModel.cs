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
        public int ExpectedArrivalRooms { get; set; }
        public int CleanInspectedVacantRooms { get; set; }
        public int AvailableRoomsAfterExpectedArrivals { get; set; }
    }
}
