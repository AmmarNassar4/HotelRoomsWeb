using System.Collections.Generic;
using System.Linq;

namespace HotelRoomsWeb.Services
{
    /// <summary>
    /// Central definition of room status codes.
    /// Codes 1-3 live in the PMS (FMROMTBL.ROMSTA).
    /// Code 4 (Under Cleaning) is internal only: it is stored in the local
    /// users database and is never written to the PMS.
    /// </summary>
    public static class RoomStatuses
    {
        public const int UnderCleaningCode = 4;
        public const string UnderCleaningLabel = "Under Cleaning";

        public static readonly IReadOnlyDictionary<int, string> PmsLabels = new Dictionary<int, string>
        {
            [1] = "Clean",
            [2] = "Dirty",
            [3] = "Clean (Inspected)"
        };

        public static readonly IReadOnlyDictionary<int, string> AllLabels = new Dictionary<int, string>
        {
            [1] = "Clean",
            [2] = "Dirty",
            [3] = "Clean (Inspected)",
            [UnderCleaningCode] = UnderCleaningLabel
        };

        public static bool IsInternalOnly(int statusCode)
        {
            return statusCode == UnderCleaningCode;
        }

        public static string GetLabel(int statusCode)
        {
            return AllLabels.TryGetValue(statusCode, out var label)
                ? label
                : $"Unknown ({statusCode})";
        }

        /// <summary>
        /// Parses the stored CSV of allowed status codes ("1,2,4").
        /// Null or empty means the user is allowed to set every status.
        /// </summary>
        public static HashSet<int> ParseAllowedCodes(string? csv)
        {
            if (string.IsNullOrWhiteSpace(csv))
            {
                return new HashSet<int>(AllLabels.Keys);
            }

            var codes = csv
                .Split(',', System.StringSplitOptions.RemoveEmptyEntries | System.StringSplitOptions.TrimEntries)
                .Select(part => int.TryParse(part, out var code) ? code : (int?)null)
                .Where(code => code.HasValue && AllLabels.ContainsKey(code.Value))
                .Select(code => code!.Value)
                .ToHashSet();

            return codes.Count > 0 ? codes : new HashSet<int>(AllLabels.Keys);
        }

        public static string ToCsv(IEnumerable<int>? codes)
        {
            if (codes == null)
            {
                return string.Empty;
            }

            var valid = codes.Where(AllLabels.ContainsKey).Distinct().OrderBy(c => c).ToList();

            // Storing every code and storing none both mean "all allowed";
            // normalise to empty so old rows and new rows behave the same.
            return valid.Count == 0 || valid.Count == AllLabels.Count
                ? string.Empty
                : string.Join(",", valid);
        }
    }
}
