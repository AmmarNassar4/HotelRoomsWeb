using System;

namespace HotelRoomsWeb.Models
{
    /// <summary>
    /// نموذج عرض لحالة الغرفة والضيف.
    /// </summary>
    public class RoomGuestViewModel
    {
        public int RoomNumber { get; set; }
        public string OccupancyStatus { get; set; } = string.Empty; // شاغرة/مشغولة
        public string RoomStatus { get; set; } = string.Empty;      // نظيفة/متسخة/نظيفة (مفتشة)
        public string GuestName { get; set; } = string.Empty;       // اسم النزيل
        public string RoomType { get; set; } = string.Empty;        // نوع الغرفة (رمز أو اسم)
    }
}