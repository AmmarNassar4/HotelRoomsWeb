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
    [Route("RoomsStatus")]
    public class RoomsStatusController : Controller
    {
        private static readonly IReadOnlyDictionary<int, string> RoomStatusLabels = RoomStatuses.AllLabels;

        private readonly IConfiguration _configuration;
        private readonly AppUserStore _userStore;

        public RoomsStatusController(IConfiguration configuration, IWebHostEnvironment environment)
        {
            _configuration = configuration;
            _userStore = new AppUserStore(configuration, environment);
        }

        [HttpPost("ChangeRoomStatus")]
        [ValidateAntiForgeryToken]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public IActionResult ChangeRoomStatus(int roomNumber, int newStatusCode)
        {
            if (!User.HasClaim("CanChangeRoomStatus", "true"))
            {
                return StatusCode(StatusCodes.Status403Forbidden, new { message = "You do not have permission to change room status." });
            }

            if (!RoomStatusLabels.ContainsKey(newStatusCode))
            {
                return BadRequest(new { message = "Invalid room status." });
            }

            var changedBy = User.Identity?.Name ?? "Unknown";

            if (!_userStore.GetAllowedRoomStatusCodes(changedBy).Contains(newStatusCode))
            {
                return StatusCode(StatusCodes.Status403Forbidden, new { message = "You do not have permission to set this room status." });
            }

            var result = UpdateRoomStatus(roomNumber, newStatusCode, changedBy);
            if (!result.Success)
            {
                return BadRequest(new { message = result.Message });
            }

            return Json(new
            {
                success = true,
                message = result.Message,
                history = _userStore.GetRoomStatusHistory(roomNumber, 20)
            });
        }

        private (bool Success, string Message) UpdateRoomStatus(int roomNumber, int newStatusCode, string changedBy)
        {
            var connectionString = _configuration.GetConnectionString("PmsConnection");
            if (string.IsNullOrWhiteSpace(connectionString))
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

            if (occupancyStatus != 1 && occupancyStatus != 2)
            {
                return (false, "Only vacant or occupied rooms can be changed.");
            }

            // Effective current status: the internal status (if any) wins over the PMS one.
            var internalStatus = _userStore.GetInternalRoomStatus(roomNumber);
            var oldStatus = internalStatus ?? GetRoomStatusLabel(oldStatusCode);

            if (RoomStatuses.IsInternalOnly(newStatusCode))
            {
                // Internal-only status: stored locally, never written to the PMS.
                var internalLabel = GetRoomStatusLabel(newStatusCode);
                if (oldStatus == internalLabel)
                {
                    return (true, "Room status is already selected.");
                }

                _userStore.SetInternalRoomStatus(roomNumber, internalLabel, changedBy);
                _userStore.AddRoomStatusChange(roomNumber, oldStatus, internalLabel, changedBy);
                return (true, "Room status updated (internal only, not sent to PMS).");
            }

            if (internalStatus == null && oldStatusCode == newStatusCode)
            {
                return (true, "Room status is already selected.");
            }

            using (var updateCommand = new SqlCommand(@"
UPDATE PMS.FMROMTBL
SET ROMSTA = @NewStatusCode
WHERE ROMNUB = @RoomNumber
  AND MANSTA IN (1, 2);", connection))
            {
                updateCommand.Parameters.AddWithValue("@NewStatusCode", newStatusCode);
                updateCommand.Parameters.AddWithValue("@RoomNumber", roomNumber);

                var affectedRows = updateCommand.ExecuteNonQuery();
                if (affectedRows == 0)
                {
                    return (false, "Room status was not changed. The room may no longer be vacant or occupied.");
                }
            }

            // Leaving an internal status back to a real PMS status clears the local override.
            if (internalStatus != null)
            {
                _userStore.ClearInternalRoomStatus(roomNumber);
            }

            var newStatus = GetRoomStatusLabel(newStatusCode);
            _userStore.AddRoomStatusChange(roomNumber, oldStatus, newStatus, changedBy);

            return (true, "Room status updated successfully.");
        }

        private static string GetRoomStatusLabel(int statusCode)
        {
            return RoomStatusLabels.TryGetValue(statusCode, out var label)
                ? label
                : $"Unknown ({statusCode})";
        }

        private static string ResolveExistingColumn(
            SqlConnection connection,
            string schema,
            string table,
            string[] candidates,
            string fallback = "NULL",
            string alias = "R")
        {
            foreach (var column in candidates)
            {
                using var command = new SqlCommand(
                    "SELECT 1 FROM sys.columns WHERE [name]=@name AND [object_id]=OBJECT_ID(@obj)",
                    connection);
                command.Parameters.AddWithValue("@name", column);
                command.Parameters.AddWithValue("@obj", $"{schema}.{table}");
                var exists = command.ExecuteScalar();
                if (exists != null)
                {
                    return $"{alias}.[{column}]";
                }
            }

            return fallback;
        }
    }
}
