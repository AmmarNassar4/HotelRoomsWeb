using System.Globalization;

namespace HotelRoomsWeb.Services
{
    public static class KsaDateTime
    {
        private const string DateTimeFormat = "yyyy-MM-dd HH:mm:ss";
        private static readonly Lazy<TimeZoneInfo> SaudiTimeZone = new(ResolveSaudiTimeZone);

        public static DateTime Now => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, SaudiTimeZone.Value);

        public static DateTime Today => Now.Date;

        public static string NowText()
        {
            return Now.ToString(DateTimeFormat, CultureInfo.InvariantCulture);
        }

        public static string FormatStoredValue(object? value)
        {
            var raw = value?.ToString()?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(raw))
            {
                return string.Empty;
            }

            if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            {
                return parsed.ToString(DateTimeFormat, CultureInfo.InvariantCulture);
            }

            return raw;
        }

        private static TimeZoneInfo ResolveSaudiTimeZone()
        {
            foreach (var id in new[] { "Asia/Riyadh", "Arab Standard Time" })
            {
                try
                {
                    return TimeZoneInfo.FindSystemTimeZoneById(id);
                }
                catch (TimeZoneNotFoundException)
                {
                }
                catch (InvalidTimeZoneException)
                {
                }
            }

            return TimeZoneInfo.CreateCustomTimeZone(
                id: "Asia/Riyadh",
                baseUtcOffset: TimeSpan.FromHours(3),
                displayName: "Saudi Arabia Standard Time",
                standardDisplayName: "Saudi Arabia Standard Time");
        }
    }
}
