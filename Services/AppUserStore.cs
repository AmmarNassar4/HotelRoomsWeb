using HotelRoomsWeb.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace HotelRoomsWeb.Services
{
    public class AppUserStore
    {
        private readonly IConfiguration _configuration;
        private readonly IWebHostEnvironment _environment;

        public AppUserStore(IConfiguration configuration, IWebHostEnvironment environment)
        {
            _configuration = configuration;
            _environment = environment;
        }

        public AppUserViewModel? ValidateUser(string userName, string password)
        {
            EnsureDatabase();

            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT rowid AS Id, UserName, IsActive, IsAdmin, CanChangeRoomStatus, IFNULL(CreatedAt, '') AS CreatedAt
FROM Users
WHERE UserName = $userName
  AND Password = $password
  AND IsActive = 1
LIMIT 1;";
            command.Parameters.AddWithValue("$userName", userName.Trim());
            command.Parameters.AddWithValue("$password", password);

            using var reader = command.ExecuteReader();
            return reader.Read() ? ReadUser(reader) : null;
        }

        public List<AppUserViewModel> GetUsers()
        {
            EnsureDatabase();

            var users = new List<AppUserViewModel>();
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT rowid AS Id, UserName, IsActive, IsAdmin, CanChangeRoomStatus, IFNULL(CreatedAt, '') AS CreatedAt
FROM Users
ORDER BY UserName;";

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                users.Add(ReadUser(reader));
            }

            return users;
        }

        public void CreateUser(CreateUserViewModel model)
        {
            EnsureDatabase();

            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"
INSERT INTO Users (UserName, Password, IsActive, IsAdmin, CanChangeRoomStatus, CreatedAt)
VALUES ($userName, $password, $isActive, $isAdmin, $canChangeRoomStatus, $createdAt);";
            command.Parameters.AddWithValue("$userName", model.UserName.Trim());
            command.Parameters.AddWithValue("$password", model.Password);
            command.Parameters.AddWithValue("$isActive", model.IsActive ? 1 : 0);
            command.Parameters.AddWithValue("$isAdmin", model.IsAdmin ? 1 : 0);
            command.Parameters.AddWithValue("$canChangeRoomStatus", model.CanChangeRoomStatus ? 1 : 0);
            command.Parameters.AddWithValue("$createdAt", KsaDateTime.NowText());
            command.ExecuteNonQuery();

            EnsureAtLeastOneAdmin(connection);
        }

        public void UpdateUserPermissions(long id, bool isActive, bool isAdmin, bool canChangeRoomStatus)
        {
            EnsureDatabase();

            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"
UPDATE Users
SET IsActive = $isActive,
    IsAdmin = $isAdmin,
    CanChangeRoomStatus = $canChangeRoomStatus
WHERE rowid = $id;";
            command.Parameters.AddWithValue("$id", id);
            command.Parameters.AddWithValue("$isActive", isActive ? 1 : 0);
            command.Parameters.AddWithValue("$isAdmin", isAdmin ? 1 : 0);
            command.Parameters.AddWithValue("$canChangeRoomStatus", canChangeRoomStatus ? 1 : 0);
            command.ExecuteNonQuery();

            EnsureAtLeastOneAdmin(connection);
        }

        public bool UpdateUserPassword(long id, string newPassword)
        {
            EnsureDatabase();

            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"
UPDATE Users
SET Password = $password
WHERE rowid = $id;";
            command.Parameters.AddWithValue("$id", id);
            command.Parameters.AddWithValue("$password", newPassword);

            return command.ExecuteNonQuery() > 0;
        }

        public void AddRoomStatusChange(int roomNumber, string oldStatus, string newStatus, string changedBy)
        {
            EnsureDatabase();

            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"
INSERT INTO RoomStatusChangeLog (RoomNumber, OldStatus, NewStatus, ChangedBy, ChangedAt)
VALUES ($roomNumber, $oldStatus, $newStatus, $changedBy, $changedAt);";
            command.Parameters.AddWithValue("$roomNumber", roomNumber);
            command.Parameters.AddWithValue("$oldStatus", oldStatus);
            command.Parameters.AddWithValue("$newStatus", newStatus);
            command.Parameters.AddWithValue("$changedBy", changedBy);
            command.Parameters.AddWithValue("$changedAt", KsaDateTime.NowText());
            command.ExecuteNonQuery();
        }

        public List<RoomStatusHistoryViewModel> GetRoomStatusHistory(int roomNumber, int take = 20)
        {
            EnsureDatabase();

            var history = new List<RoomStatusHistoryViewModel>();
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT RoomNumber, OldStatus, NewStatus, ChangedBy, ChangedAt
FROM RoomStatusChangeLog
WHERE RoomNumber = $roomNumber
ORDER BY Id DESC
LIMIT $take;";
            command.Parameters.AddWithValue("$roomNumber", roomNumber);
            command.Parameters.AddWithValue("$take", take);

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                history.Add(ReadRoomStatusHistory(reader));
            }

            return history;
        }

        public List<RoomStatusHistoryViewModel> GetAllRoomStatusHistory(int take = 500)
        {
            EnsureDatabase();

            var history = new List<RoomStatusHistoryViewModel>();
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT RoomNumber, OldStatus, NewStatus, ChangedBy, ChangedAt
FROM RoomStatusChangeLog
ORDER BY Id DESC
LIMIT $take;";
            command.Parameters.AddWithValue("$take", take);

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                history.Add(ReadRoomStatusHistory(reader));
            }

            return history;
        }

        public void EnsureDatabase()
        {
            Directory.CreateDirectory(Path.Combine(_environment.ContentRootPath, "App_Data"));

            using var connection = OpenConnection();

            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
CREATE TABLE IF NOT EXISTS Users (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    UserName TEXT NOT NULL UNIQUE,
    Password TEXT NOT NULL,
    IsActive INTEGER NOT NULL DEFAULT 1,
    IsAdmin INTEGER NOT NULL DEFAULT 0,
    CanChangeRoomStatus INTEGER NOT NULL DEFAULT 0,
    CreatedAt TEXT NOT NULL DEFAULT (datetime('now', '+3 hours'))
);";
                command.ExecuteNonQuery();
            }

            EnsureColumn(connection, "Users", "IsAdmin", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(connection, "Users", "CanChangeRoomStatus", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(connection, "Users", "CreatedAt", "TEXT NOT NULL DEFAULT ''");

            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
CREATE TABLE IF NOT EXISTS RoomStatusChangeLog (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    RoomNumber INTEGER NOT NULL,
    OldStatus TEXT NOT NULL,
    NewStatus TEXT NOT NULL,
    ChangedBy TEXT NOT NULL,
    ChangedAt TEXT NOT NULL DEFAULT (datetime('now', '+3 hours'))
);
CREATE INDEX IF NOT EXISTS IX_RoomStatusChangeLog_RoomNumber_Id
ON RoomStatusChangeLog (RoomNumber, Id DESC);
CREATE INDEX IF NOT EXISTS IX_RoomStatusChangeLog_Id
ON RoomStatusChangeLog (Id DESC);";
                command.ExecuteNonQuery();
            }

            EnsureAtLeastOneAdmin(connection);
        }

        private SqliteConnection OpenConnection()
        {
            var connection = new SqliteConnection(GetConnectionString());
            connection.Open();
            return connection;
        }

        private string GetConnectionString()
        {
            var configured = _configuration.GetConnectionString("UsersConnection");
            if (!string.IsNullOrWhiteSpace(configured))
            {
                return configured;
            }

            var databasePath = Path.Combine(_environment.ContentRootPath, "App_Data", "users.db");
            return $"Data Source={databasePath}";
        }

        private static AppUserViewModel ReadUser(SqliteDataReader reader)
        {
            return new AppUserViewModel
            {
                Id = Convert.ToInt64(reader["Id"]),
                UserName = reader["UserName"]?.ToString() ?? string.Empty,
                IsActive = Convert.ToInt32(reader["IsActive"]) == 1,
                IsAdmin = Convert.ToInt32(reader["IsAdmin"]) == 1,
                CanChangeRoomStatus = Convert.ToInt32(reader["CanChangeRoomStatus"]) == 1,
                CreatedAt = KsaDateTime.FormatStoredValue(reader["CreatedAt"])
            };
        }

        private static RoomStatusHistoryViewModel ReadRoomStatusHistory(SqliteDataReader reader)
        {
            return new RoomStatusHistoryViewModel
            {
                RoomNumber = Convert.ToInt32(reader["RoomNumber"]),
                OldStatus = reader["OldStatus"]?.ToString() ?? string.Empty,
                NewStatus = reader["NewStatus"]?.ToString() ?? string.Empty,
                ChangedBy = reader["ChangedBy"]?.ToString() ?? string.Empty,
                ChangedAt = KsaDateTime.FormatStoredValue(reader["ChangedAt"])
            };
        }

        private static void EnsureColumn(SqliteConnection connection, string table, string column, string definition)
        {
            if (ColumnExists(connection, table, column))
            {
                return;
            }

            using var command = connection.CreateCommand();
            command.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {definition};";
            command.ExecuteNonQuery();
        }

        private static bool ColumnExists(SqliteConnection connection, string table, string column)
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"PRAGMA table_info({table});";

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                if (string.Equals(reader["name"]?.ToString(), column, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static void EnsureAtLeastOneAdmin(SqliteConnection connection)
        {
            using (var countCommand = connection.CreateCommand())
            {
                countCommand.CommandText = "SELECT COUNT(1) FROM Users WHERE IsAdmin = 1 AND IsActive = 1;";
                if (Convert.ToInt32(countCommand.ExecuteScalar()) > 0)
                {
                    return;
                }
            }

            using (var adminCommand = connection.CreateCommand())
            {
                adminCommand.CommandText = @"
UPDATE Users
SET IsAdmin = 1,
    CanChangeRoomStatus = 1
WHERE rowid = (
    SELECT rowid
    FROM Users
    WHERE IsActive = 1
    ORDER BY CASE WHEN lower(UserName) = 'admin' THEN 0 ELSE 1 END, rowid
    LIMIT 1
);";
                adminCommand.ExecuteNonQuery();
            }
        }
    }
}
