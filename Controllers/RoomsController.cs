using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using HotelRoomsWeb.Models;

namespace HotelRoomsWeb.Controllers
{
    [Authorize]
    public class RoomsController : Controller
    {
        private readonly IConfiguration _configuration;

        public RoomsController(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        // List of rooms with guest names
        public IActionResult Index()
        {
            var model = GetAllRooms();
            return View(model);
        }

        // Dashboard (charts + filters)
        public IActionResult Dashboard()
        {
            var rooms = GetAllRooms();

            var vm = new RoomDashboardViewModel
            {
                Rooms = rooms,
                RoomTypes = rooms
                    .Where(r => !string.IsNullOrWhiteSpace(r.RoomType))
                    .Select(r => r.RoomType)
                    .Distinct()
                    .OrderBy(s => s)
                    .ToList()
            };

            return View(vm);
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
