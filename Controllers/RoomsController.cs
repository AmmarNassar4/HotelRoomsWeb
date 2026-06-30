using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using HotelRoomsWeb.Models;
using HotelRoomsWeb.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace HotelRoomsWeb.Controllers
{
    [Authorize]
    public class RoomsController : Controller
    {
        private const string PropertyCode = "RRK";

        private static readonly IReadOnlyDictionary<int, string> RoomStatusLabels = new Dictionary<int, string>
        {
            [1] = "Clean",
            [2] = "Dirty",
            [3] = "Clean (Inspected)"
        };

        private readonly IConfiguration _configuration;
        private readonly AppUserStore _userStore;

        public RoomsController(IConfiguration configuration, IWebHostEnvironment environment)
        {
            _configuration = configuration;
            _userStore = new AppUserStore(configuration, environment);
        }

        // List of rooms with guest names
        public IActionResult Index()
        {
            _userStore.EnsureDatabase();
            var model = GetAllRooms();
            return View(model);
        }

        // Dashboard (charts + filters)
        public IActionResult Dashboard()
        {
            var rooms = GetAllRooms();
            var expectedArrivalRooms = GetExpectedArrivalRooms();
            var cleanInspectedVacantRooms = CountCleanInspectedVacantRooms(rooms);

            var vm = new RoomDashboardViewModel
            {
                Rooms = rooms,
                RoomTypes = rooms
                    .Where(r => !string.IsNullOrWhiteSpace(r.RoomType))
                    .Select(r => r.RoomType)
                    .Distinct()
                    .OrderBy(s => s)
                    .ToList(),
                ExpectedArrivalRooms = expectedArrivalRooms,
                CleanInspectedVacantRooms = cleanInspectedVacantRooms,
                AvailableRoomsAfterExpectedArrivals = cleanInspectedVacantRooms - expectedArrivalRooms
            };

            return View(vm);
        }

        // (API) Current rooms data - used by the Refresh buttons without a full page reload.
        [HttpGet]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public IActionResult Data()
        {
            var rooms = GetAllRooms();
            return Json(rooms);
        }

        // (API) Dashboard summary data - used by the dashboard refresh button.
        [HttpGet]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public IActionResult DashboardStats()
        {
            var rooms = GetAllRooms();
            var expectedArrivalRooms = GetExpectedArrivalRooms();
            var cleanInspectedVacantRooms = CountCleanInspectedVacantRooms(rooms);

            return Json(new
            {
                expectedArrivalRooms,
                cleanInspectedVacantRooms,
                availableRoomsAfterExpectedArrivals = cleanInspectedVacantRooms - expectedArrivalRooms
            });
        }

        // (API) Available room status options for the edit modal.
        [HttpGet]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public IActionResult RoomStatusOptions()
        {
            return Json(RoomStatusLabels.Select(status => new
            {
                code = status.Key,
                label = status.Value
            }));
        }

        // (API) Last 20 status changes for a room.
        [HttpGet]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public IActionResult RoomStatusHistory(int id)
        {
            return Json(_userStore.GetRoomStatusHistory(id, 20));
        }

        // (API) Change vacant room status only for authorized users.
        [HttpPost]
        [ValidateAntiForgeryToken]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public IActionResult ChangeRoomStatus(int roomNumber, int newStatusCode)
        {
            if (!UserCanChangeRoomStatus())
            {
                return StatusCode(StatusCodes.Status403Forbidden, new { message = "You do not have permission to change room status." });
            }

            if (!RoomStatusLabels.ContainsKey(newStatusCode))
            {
                return BadRequest(new { message = "Invalid room status." });
            }

            var changedBy = User.Identity?.Name ?? "Unknown";
            var result = UpdateVacantRoomStatus(roomNumber, newStatusCode, changedBy);
            if (!result.Success)
            {
                return BadRequest(new { message = result.Message });
            }

            return Json(new
            {
                success = true,
                message = result.Message,
                history = _userStore.GetRoomStatusHistory(roomNumber, 20),
                room = GetRoomByNumber(roomNumber)
            });
        }

        // Single room details page
        public IActionResult Details(int id)
        {
            var room = GetRoomByNumber(id);
            if (room == null)
            {
                return NotFound();
            }

            return View(room);
        }

        // (API) Guest details for a room - used by the modal via AJAX
        [HttpGet]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public IActionResult GuestDetails(int id)
        {
            var payload = GetGuestDetailsByRoom(id);
            if (payload == null)
            {
                return NotFound();
            }

            return Json(payload);
        }

        // --------------------------------------------------------------------
        // Data access helpers
        // --------------------------------------------------------------------

        private (bool Success, string Message) UpdateVacantRoomStatus(int roomNumber, int newStatusCode, string changedBy)
        {
            var connectionString = _configuration.GetConnectionString("PmsConnection");
            if (string.IsNullOrEmpty(connectionString))
            {
                throw new Exception("Connection string 'PmsConnection' is missing.");
            }

            using var connection = new SqlConnection(connectionString);
            connection.Open();

            var roomTypeExpr = ResolveExistingColumn(
                connection,
                schema: "PMS",
                table: "FMROMTBL",
                candidates: new[] { "ROMTYP", "ROOMTYP", "ROMCAT", "CLASS", "TYP" },
                fallback: "NULL",
                alias: "R");

            var roomTypeFilter = roomTypeExpr != "NULL" ? $" AND {roomTypeExpr} <> 'zzz'" : string.Empty;

            int occupancyStatus;
            int oldStatusCode;

            using (var selectCommand = new SqlCommand($@"
SELECT R.MANSTA, R.ROMSTA
FROM PMS.FMROMTBL AS R
WHERE R.ROMNUB = @RoomNumber{roomTypeFilter};", connection))
            {
                selectCommand.Parameters.AddWithValue("@RoomNumber", roomNumber);

                using var reader = selectCommand.ExecuteReader();
                if (!reader.Read())
                {
                    return (false, "Room was not found.");
                }

                occupancyStatus = Convert.ToInt32(reader["MANSTA"]);
                oldStatusCode = Convert.ToInt32(reader["ROMSTA"]);
            }

            if (occupancyStatus != 1)
            {
                return (false, "Only vacant rooms can be changed.");
            }

            if (oldStatusCode == newStatusCode)
            {
                return (true, "Room status is already selected.");
            }

            using (var updateCommand = new SqlCommand(@"
UPDATE PMS.FMROMTBL
SET ROMSTA = @NewStatusCode
WHERE ROMNUB = @RoomNumber
  AND MANSTA = 1;", connection))
            {
                updateCommand.Parameters.AddWithValue("@NewStatusCode", newStatusCode);
                updateCommand.Parameters.AddWithValue("@RoomNumber", roomNumber);

                var affectedRows = updateCommand.ExecuteNonQuery();
                if (affectedRows == 0)
                {
                    return (false, "Room status was not changed. The room may no longer be vacant.");
                }
            }

            var oldStatus = GetRoomStatusLabel(oldStatusCode);
            var newStatus = GetRoomStatusLabel(newStatusCode);
            _userStore.AddRoomStatusChange(roomNumber, oldStatus, newStatus, changedBy);

            return (true, "Room status updated successfully.");
        }

        private int GetExpectedArrivalRooms()
        {
            var connectionString = _configuration.GetConnectionString("PmsConnection");
            if (string.IsNullOrEmpty(connectionString))
            {
                throw new Exception("Connection string 'PmsConnection' is missing.");
            }

            var arrivalDate = int.Parse(DateTime.Today.ToString("yyyyMMdd", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

            const string sql = @"
SELECT ISNULL(SUM(
           ISNULL(R1.SGLCNF, 0)
         + ISNULL(R1.DBLCNF, 0)
         + ISNULL(R1.TPLCNF, 0)
         + ISNULL(R1.QUDCNF, 0)
         - ISNULL(R1.CKIROM, 0)
       ), 0) AS ExpectedArrivalRooms
FROM PMS.FMR01TBL R1
WHERE R1.PRPCOD = @PropertyCode
  AND R1.ARRDAT = @ArrivalDate
  AND (ISNULL(R1.SGLCNF, 0) + ISNULL(R1.DBLCNF, 0) + ISNULL(R1.TPLCNF, 0) + ISNULL(R1.QUDCNF, 0) - ISNULL(R1.CKIROM, 0)) <> 0
  AND R1.SUBSRL IN (
      SELECT MAX(A.SUBSRL)
      FROM PMS.FMR01TBL A
      WHERE A.PRPCOD = R1.PRPCOD
        AND A.RESNUB = R1.RESNUB
        AND A.SRLNUB = R1.SRLNUB
  )
  AND R1.ROMTYP <> 'ZZZ';";

            using var connection = new SqlConnection(connectionString);
            connection.Open();

            using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@PropertyCode", PropertyCode);
            command.Parameters.AddWithValue("@ArrivalDate", arrivalDate);

            var value = command.ExecuteScalar();
            return value == null || value == DBNull.Value ? 0 : Convert.ToInt32(value);
        }

        private static int CountCleanInspectedVacantRooms(IEnumerable<RoomGuestViewModel> rooms)
        {
            return rooms.Count(r =>
                string.Equals(r.RoomStatus, "Clean (Inspected)", StringComparison.OrdinalIgnoreCase)
                && string.Equals(r.OccupancyStatus, "Vacant", StringComparison.OrdinalIgnoreCase));
        }

        // Get all rooms (one row per room) with aggregated guest names
        private List<RoomGuestViewModel> GetAllRooms()
        {
            var result = new List<RoomGuestViewModel>();

            var connectionString = _configuration.GetConnectionString("PmsConnection");
            if (string.IsNullOrEmpty(connectionString))
            {
                throw new Exception("Connection string 'PmsConnection' is missing.");
            }

            using var connection = new SqlConnection(connectionString);
            connection.Open();

            // Detect room type column on PMS.FMROMTBL
            var roomTypeExpr = ResolveExistingColumn(
                connection,
                schema: "PMS",
                table: "FMROMTBL",
                candidates: new[] { "ROMTYP", "ROOMTYP", "ROMCAT", "CLASS", "TYP" },
                fallback: "NULL",
                alias: "R");

            // Exclude rooms with type 'zzz'
            var roomTypeFilterAll =
                roomTypeExpr != "NULL"
                    ? $"WHERE {roomTypeExpr} <> 'zzz'"
                    : string.Empty;

            var sql = $@"
SELECT 
    R.ROMNUB AS RoomNumber,
    {roomTypeExpr} AS RoomTypeCode,
    CASE R.MANSTA
        WHEN 1 THEN N'Vacant'
        WHEN 2 THEN N'Occupied'
        ELSE N'Unknown'
    END AS OccupancyStatus,
    CASE R.ROMSTA
        WHEN 1 THEN N'Clean'
        WHEN 2 THEN N'Dirty'
        WHEN 3 THEN N'Clean (Inspected)'
        ELSE N'Unknown'
    END AS RoomStatus,
    ISNULL(
        NULLIF(
            STUFF(
                (
                    SELECT N' | ' + LTRIM(RTRIM(G2.FSTNAM + ' ' + G2.LSTNAM))
                    FROM PMS.FMOCCTBL AS G2
                    WHERE G2.ROMNUB = R.ROMNUB
                          AND R.MANSTA = 2
                    FOR XML PATH(''), TYPE
                ).value('.', 'nvarchar(max)'),
                1, 3, ''   -- remove first ' | '
            ),
            ''
        ),
        N'Empty'
    ) AS GuestName
FROM PMS.FMROMTBL AS R
{roomTypeFilterAll}
ORDER BY R.ROMNUB;";

            using var command = new SqlCommand(sql, connection);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var item = new RoomGuestViewModel
                {
                    RoomNumber = Convert.ToInt32(reader["RoomNumber"]),
                    RoomType = reader["RoomTypeCode"]?.ToString() ?? string.Empty,
                    OccupancyStatus = reader["OccupancyStatus"]?.ToString() ?? string.Empty,
                    RoomStatus = reader["RoomStatus"]?.ToString() ?? string.Empty,
                    GuestName = reader["GuestName"]?.ToString() ?? string.Empty
                };

                result.Add(item);
            }

            return result;
        }

        // Get details for a single room (same logic as GetAllRooms but filtered)
        private RoomGuestViewModel? GetRoomByNumber(int roomNumber)
        {
            var connectionString = _configuration.GetConnectionString("PmsConnection");
            if (string.IsNullOrEmpty(connectionString))
            {
                throw new Exception("Connection string 'PmsConnection' is missing.");
            }

            using var connection = new SqlConnection(connectionString);
            connection.Open();

            var roomTypeExpr = ResolveExistingColumn(
                connection,
                schema: "PMS",
                table: "FMROMTBL",
                candidates: new[] { "ROMTYP", "ROOMTYP", "ROMCAT", "CLASS", "TYP" },
                fallback: "NULL",
                alias: "R");

            var roomTypeFilterSingle =
                roomTypeExpr != "NULL"
                    ? $" AND {roomTypeExpr} <> 'zzz'"
                    : string.Empty;

            var sql = $@"
SELECT 
    R.ROMNUB AS RoomNumber,
    {roomTypeExpr} AS RoomTypeCode,
    CASE R.MANSTA
        WHEN 1 THEN N'Vacant'
        WHEN 2 THEN N'Occupied'
        ELSE N'Unknown'
    END AS OccupancyStatus,
    CASE R.ROMSTA
        WHEN 1 THEN N'Clean'
        WHEN 2 THEN N'Dirty'
        WHEN 3 THEN N'Clean (Inspected)'
        ELSE N'Unknown'
    END AS RoomStatus,
    ISNULL(
        NULLIF(
            STUFF(
                (
                    SELECT N' | ' + LTRIM(RTRIM(G2.FSTNAM + ' ' + G2.LSTNAM))
                    FROM PMS.FMOCCTBL AS G2
                    WHERE G2.ROMNUB = R.ROMNUB
                          AND R.MANSTA = 2
                    FOR XML PATH(''), TYPE
                ).value('.', 'nvarchar(max)'),
                1, 3, ''
            ),
            ''
        ),
        N'Empty'
    ) AS GuestName
FROM PMS.FMROMTBL AS R
WHERE R.ROMNUB = @RoomNumber{roomTypeFilterSingle};";

            using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@RoomNumber", roomNumber);

            using var reader = command.ExecuteReader();
            if (reader.Read())
            {
                return new RoomGuestViewModel
                {
                    RoomNumber = Convert.ToInt32(reader["RoomNumber"]),
                    RoomType = reader["RoomTypeCode"]?.ToString() ?? string.Empty,
                    OccupancyStatus = reader["OccupancyStatus"]?.ToString() ?? string.Empty,
                    RoomStatus = reader["RoomStatus"]?.ToString() ?? string.Empty,
                    GuestName = reader["GuestName"]?.ToString() ?? string.Empty
                };
            }

            return null;
        }

        // Get full guest details list for a room from FMOCCTBL
        private object? GetGuestDetailsByRoom(int roomNumber)
        {
            var connectionString = _configuration.GetConnectionString("PmsConnection");
            if (string.IsNullOrEmpty(connectionString))
            {
                throw new Exception("Connection string 'PmsConnection' is missing.");
            }

            using var connection = new SqlConnection(connectionString);
            connection.Open();

            var roomTypeExpr = ResolveExistingColumn(
                connection,
                schema: "PMS",
                table: "FMROMTBL",
                candidates: new[] { "ROMTYP", "ROOMTYP", "ROMCAT", "CLASS", "TYP" },
                fallback: "NULL",
                alias: "R");

            // Columns on FMOCCTBL
            var arrCol = ResolveExistingColumn(
                connection,
                schema: "PMS",
                table: "FMOCCTBL",
                candidates: new[] { "ARRDAT", "ARRIVAL", "INDDAT" },
                fallback: "NULL",
                alias: "O");

            var depCol = ResolveExistingColumn(
                connection,
                schema: "PMS",
                table: "FMOCCTBL",
                candidates: new[] { "DEPDAT", "DEPARTURE", "OUTDAT" },
                fallback: "NULL",
                alias: "O");

            var natCol = ResolveExistingColumn(
                connection,
                schema: "PMS",
                table: "FMOCCTBL",
                candidates: new[] { "NATION", "NATCOD", "NATIONALITY" },
                fallback: "NULL",
                alias: "O");

            var telCol = ResolveExistingColumn(
                connection,
                schema: "PMS",
                table: "FMOCCTBL",
                candidates: new[] { "TELNUB", "PHONE", "MOBNUM", "MOBIL" },
                fallback: "NULL",
                alias: "O");

            // Exclude rooms with type 'zzz'
            var roomTypeFilterDetails =
                roomTypeExpr != "NULL"
                    ? $" AND {roomTypeExpr} <> 'zzz'"
                    : string.Empty;

            var sql = $@"
SELECT
   R.ROMNUB AS RoomNumber,
   {roomTypeExpr} AS RoomTypeCode,
   CASE R.MANSTA WHEN 1 THEN N'Vacant' WHEN 2 THEN N'Occupied' ELSE N'Unknown' END AS OccupancyStatus,
   CASE R.ROMSTA WHEN 1 THEN N'Clean' WHEN 2 THEN N'Dirty' WHEN 3 THEN N'Clean (Inspected)' ELSE N'Unknown' END AS RoomStatus,
   LTRIM(RTRIM(ISNULL(O.FSTNAM, N''))) +
       CASE WHEN ISNULL(O.LSTNAM, N'') = N'' THEN N'' ELSE N' ' + LTRIM(RTRIM(O.LSTNAM)) END AS GuestName,
   O.RESNUB AS ReservationNo,
   {arrCol} AS ArrivalDate,
   {depCol} AS DepartureDate,
   {natCol} AS NationalityCode,
   {telCol} AS Phone
FROM PMS.FMROMTBL AS R
LEFT JOIN PMS.FMOCCTBL AS O 
   ON O.ROMNUB = R.ROMNUB
   AND R.MANSTA = 2
WHERE R.ROMNUB = @RoomNumber{roomTypeFilterDetails}
ORDER BY O.LSTDAT DESC;";

            using var cmd = new SqlCommand(sql, connection);
            cmd.Parameters.AddWithValue("@RoomNumber", roomNumber);
            using var reader = cmd.ExecuteReader();

            if (!reader.HasRows)
            {
                return null;
            }

            string roomNumberValue = string.Empty;
            string roomTypeValue = string.Empty;
            string occupancyStatusValue = string.Empty;
            string roomStatusValue = string.Empty;

            var guests = new List<object>();

            while (reader.Read())
            {
                // Fill room-level info from the first row
                if (string.IsNullOrEmpty(roomNumberValue))
                {
                    roomNumberValue = reader["RoomNumber"]?.ToString() ?? string.Empty;
                    roomTypeValue = reader["RoomTypeCode"]?.ToString() ?? string.Empty;
                    occupancyStatusValue = reader["OccupancyStatus"]?.ToString() ?? string.Empty;
                    roomStatusValue = reader["RoomStatus"]?.ToString() ?? string.Empty;
                }

                guests.Add(new
                {
                    guestName = reader["GuestName"]?.ToString() ?? string.Empty,
                    reservationNo = reader["ReservationNo"]?.ToString() ?? string.Empty,
                    arrivalDate = reader["ArrivalDate"]?.ToString() ?? string.Empty,
                    departureDate = reader["DepartureDate"]?.ToString() ?? string.Empty,
                    nationalityCode = reader["NationalityCode"]?.ToString() ?? string.Empty,
                    phone = reader["Phone"]?.ToString() ?? string.Empty
                });
            }

            // One object with room info + list of guests
            return new
            {
                roomNumber = roomNumberValue,
                roomType = roomTypeValue,
                occupancyStatus = occupancyStatusValue,
                roomStatus = roomStatusValue,
                guests
            };
        }

        private bool UserCanChangeRoomStatus()
        {
            return User.HasClaim("CanChangeRoomStatus", "true");
        }

        private static string GetRoomStatusLabel(int statusCode)
        {
            return RoomStatusLabels.TryGetValue(statusCode, out var label)
                ? label
                : $"Unknown ({statusCode})";
        }

        /// <summary>
        /// Checks whether a column exists in a table; returns a safe expression
        /// like alias.[Column] for use in SELECT, or a fallback value such as NULL.
        /// </summary>
        private static string ResolveExistingColumn(
            SqlConnection connection,
            string schema,
            string table,
            string[] candidates,
            string fallback = "NULL",
            string alias = "R")
        {
            foreach (var c in candidates)
            {
                using var cmd = new SqlCommand(
                    "SELECT 1 FROM sys.columns WHERE [name]=@name AND [object_id]=OBJECT_ID(@obj)",
                    connection);
                cmd.Parameters.AddWithValue("@name", c);
                cmd.Parameters.AddWithValue("@obj", $"{schema}.{table}");
                var exists = cmd.ExecuteScalar();
                if (exists != null)
                {
                    return $"{alias}.[{c}]";
                }
            }

            return fallback;
        }
    }
}
