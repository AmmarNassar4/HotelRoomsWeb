using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Data.SqlClient;
using HotelRoomsWeb.Models;

namespace HotelRoomsWeb.Controllers
{
    public class RoomsController : Controller
    {
        private readonly IConfiguration _configuration;

        public RoomsController(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        // Rooms status list
        public IActionResult Index()
        {
            var model = GetAllRooms();
            return View(model);
        }

        // Dashboard
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
            if (room == null) return NotFound();
            return View(room);
        }

        // (API) Guest details for a room - used by the modal via AJAX
        [HttpGet]
        public IActionResult GuestDetails(int id)
        {
            var payload = GetGuestDetailsByRoom(id);
            if (payload == null) return NotFound();
            return Json(payload);
        }

        // -------------------- Data access helpers --------------------

        private object? GetGuestDetailsByRoom(int roomNumber)
        {
            string? connectionString = _configuration.GetConnectionString("PmsConnection");
            if (string.IsNullOrEmpty(connectionString))
                throw new Exception("Connection string 'PmsConnection' is missing.");

            using var connection = new SqlConnection(connectionString);
            connection.Open();

            // Try to detect possible column names in PMS tables
            string roomTypeExpr = ResolveExistingColumn(connection, "PMS", "FMROMTBL",
                new[] { "ROMTYP", "ROOMTYP", "ROMCAT", "CLASS", "TYP" }, "NULL", "R");

            string arrCol = ResolveExistingColumn(connection, "PMS", "FMOCCTBL",
                new[] { "ARRDAT", "ARRIVAL", "INDDAT" }, "NULL", "O");

            string depCol = ResolveExistingColumn(connection, "PMS", "FMOCCTBL",
                new[] { "DEPDAT", "DEPARTURE", "OUTDAT" }, "NULL", "O");

            string natCol = ResolveExistingColumn(connection, "PMS", "ARGSTTBL",
                new[] { "NATCOD", "NATION", "NATIONALITY" }, "NULL", "G");

            string telCol = ResolveExistingColumn(connection, "PMS", "ARGSTTBL",
                new[] { "TELNUB", "PHONE", "MOBNUM", "MOBIL" }, "NULL", "G");

            // NEW: exclude rooms with type 'zzz'
            string roomTypeFilterDetails =
                roomTypeExpr != "NULL"
                    ? $" AND {roomTypeExpr} <> 'zzz'"
                    : string.Empty;

            string sql = $@"
SELECT TOP 1
   R.ROMNUB AS RoomNumber,
   {roomTypeExpr} AS RoomTypeCode,
   CASE R.MANSTA WHEN 1 THEN N'Vacant' WHEN 2 THEN N'Occupied' ELSE N'Unknown' END AS OccupancyStatus,
   CASE R.ROMSTA WHEN 1 THEN N'Clean' WHEN 2 THEN N'Dirty' WHEN 3 THEN N'Clean (Inspected)' ELSE N'Unknown' END AS RoomStatus,
   ISNULL(G.FSTNAM + ' ' + G.LSTNAM, N'') AS GuestName,
   O.RESNUB AS ReservationNo,
   {arrCol} AS ArrivalDate,
   {depCol} AS DepartureDate,
   {natCol} AS NationalityCode,
   {telCol} AS Phone
FROM PMS.FMROMTBL AS R
LEFT JOIN PMS.FMOCCTBL AS O ON O.ROMNUB = R.ROMNUB
LEFT JOIN PMS.ARGSTTBL  AS G ON G.ROMNUB = R.ROMNUB
WHERE R.ROMNUB = @RoomNumber AND R.MANSTA = 2{roomTypeFilterDetails}
ORDER BY O.LSTDAT DESC;";

            using var cmd = new SqlCommand(sql, connection);
            cmd.Parameters.AddWithValue("@RoomNumber", roomNumber);
            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                return new
                {
                    RoomNumber = reader["RoomNumber"]?.ToString() ?? "",
                    RoomType = reader["RoomTypeCode"]?.ToString() ?? "",
                    OccupancyStatus = reader["OccupancyStatus"]?.ToString() ?? "",
                    RoomStatus = reader["RoomStatus"]?.ToString() ?? "",
                    GuestName = reader["GuestName"]?.ToString() ?? "",
                    ReservationNo = reader["ReservationNo"]?.ToString() ?? "",
                    ArrivalDate = reader["ArrivalDate"]?.ToString() ?? "",
                    DepartureDate = reader["DepartureDate"]?.ToString() ?? "",
                    NationalityCode = reader["NationalityCode"]?.ToString() ?? "",
                    Phone = reader["Phone"]?.ToString() ?? ""
                };
            }
            return null;
        }


        private List<RoomGuestViewModel> GetAllRooms()
        {
            var result = new List<RoomGuestViewModel>();

            string? connectionString = _configuration.GetConnectionString("PmsConnection");
            if (string.IsNullOrEmpty(connectionString))
                throw new Exception("Connection string 'PmsConnection' is missing.");

            using var connection = new SqlConnection(connectionString);
            connection.Open();

            // Try to detect the room type column
            string roomTypeExpr = ResolveExistingColumn(connection, "PMS", "FMROMTBL",
                new[] { "ROMTYP", "ROOMTYP", "ROMCAT", "CLASS", "TYP" }, "NULL", "R");

            // Exclude rooms with type 'zzz'
            string roomTypeFilterAll =
                roomTypeExpr != "NULL"
                    ? $"WHERE {roomTypeExpr} <> 'zzz'"
                    : string.Empty;

            string sql = $@"
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



        private RoomGuestViewModel? GetRoomByNumber(int roomNumber)
        {
            string? connectionString = _configuration.GetConnectionString("PmsConnection");
            if (string.IsNullOrEmpty(connectionString))
                throw new Exception("Connection string 'PmsConnection' is missing.");

            using var connection = new SqlConnection(connectionString);
            connection.Open();

            string roomTypeExpr = ResolveExistingColumn(connection, "PMS", "FMROMTBL",
                new[] { "ROMTYP", "ROOMTYP", "ROMCAT", "CLASS", "TYP" }, "NULL", "R");

            // NEW: exclude rooms with type 'zzz'
            string roomTypeFilterSingle =
                roomTypeExpr != "NULL"
                    ? $" AND {roomTypeExpr} <> 'zzz'"
                    : string.Empty;

            string sql = $@"
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
    ISNULL(G.FSTNAM + ' ' + G.LSTNAM, N'') AS GuestName
FROM PMS.FMROMTBL AS R
LEFT JOIN PMS.FMOCCTBL AS G
    ON R.ROMNUB = G.ROMNUB
    AND R.MANSTA = 2
WHERE R.ROMNUB = @RoomNumber{roomTypeFilterSingle};";

            using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@RoomNumber", roomNumber);

            using var reader = command.ExecuteReader();
            if (reader.Read())
            {
                var item = new RoomGuestViewModel
                {
                    RoomNumber = Convert.ToInt32(reader["RoomNumber"]),
                    RoomType = reader["RoomTypeCode"]?.ToString() ?? string.Empty,
                    OccupancyStatus = reader["OccupancyStatus"]?.ToString() ?? string.Empty,
                    RoomStatus = reader["RoomStatus"]?.ToString() ?? string.Empty,
                    GuestName = reader["GuestName"]?.ToString() ?? string.Empty
                };

                return item;
            }

            return null;
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
                if (exists != null) return $"{alias}.[{c}]";
            }
            return fallback;
        }
    }
}
