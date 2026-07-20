using HotelRoomsWeb.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using System.Data;
using static System.Formats.Asn1.AsnWriter;

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
SELECT TOP 1 Id, UserName, IsActive, IsAdmin, CanChangeRoomStatus, ISNULL(AllowedRoomStatuses, '') AS AllowedRoomStatuses, ISNULL(CreatedAt, '') AS CreatedAt
FROM Users
WHERE UserName = @userName
  AND Password = @password
  AND IsActive = 1;";
            command.Parameters.AddWithValue("@userName", userName.Trim());
            command.Parameters.AddWithValue("@password", password);

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
SELECT Id, UserName, IsActive, IsAdmin, CanChangeRoomStatus, ISNULL(AllowedRoomStatuses, '') AS AllowedRoomStatuses, ISNULL(CreatedAt, '') AS CreatedAt
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
INSERT INTO Users (UserName, Password, IsActive, IsAdmin, CanChangeRoomStatus, AllowedRoomStatuses, CreatedAt)
VALUES (@userName, @password, @isActive, @isAdmin, @canChangeRoomStatus, @allowedRoomStatuses, @createdAt);";
            command.Parameters.AddWithValue("@userName", model.UserName.Trim());
            command.Parameters.AddWithValue("@password", model.Password);
            command.Parameters.AddWithValue("@isActive", model.IsActive);
            command.Parameters.AddWithValue("@isAdmin", model.IsAdmin);
            command.Parameters.AddWithValue("@canChangeRoomStatus", model.CanChangeRoomStatus);
            command.Parameters.AddWithValue("@allowedRoomStatuses", RoomStatuses.ToCsv(model.AllowedStatusCodes));
            command.Parameters.AddWithValue("@createdAt", KsaDateTime.NowText());
            command.ExecuteNonQuery();

            EnsureAtLeastOneAdmin(connection);
        }

        public void UpdateUserPermissions(long id, bool isActive, bool isAdmin, bool canChangeRoomStatus, IEnumerable<int>? allowedStatusCodes = null)
        {
            EnsureDatabase();

            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"
UPDATE Users
SET IsActive = @isActive,
    IsAdmin = @isAdmin,
    CanChangeRoomStatus = @canChangeRoomStatus,
    AllowedRoomStatuses = @allowedRoomStatuses
WHERE Id = @id;";
            command.Parameters.AddWithValue("@id", id);
            command.Parameters.AddWithValue("@isActive", isActive);
            command.Parameters.AddWithValue("@isAdmin", isAdmin);
            command.Parameters.AddWithValue("@canChangeRoomStatus", canChangeRoomStatus);
            command.Parameters.AddWithValue("@allowedRoomStatuses", RoomStatuses.ToCsv(allowedStatusCodes));
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
SET Password = @password
WHERE Id = @id;";
            command.Parameters.AddWithValue("@id", id);
            command.Parameters.AddWithValue("@password", newPassword);

            return command.ExecuteNonQuery() > 0;
        }

        public void AddRoomStatusChange(int roomNumber, string oldStatus, string newStatus, string changedBy)
        {
            EnsureDatabase();

            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"
INSERT INTO RoomStatusChangeLog (RoomNumber, OldStatus, NewStatus, ChangedBy, ChangedAt)
VALUES (@roomNumber, @oldStatus, @newStatus, @changedBy, @changedAt);";
            command.Parameters.AddWithValue("@roomNumber", roomNumber);
            command.Parameters.AddWithValue("@oldStatus", oldStatus);
            command.Parameters.AddWithValue("@newStatus", newStatus);
            command.Parameters.AddWithValue("@changedBy", changedBy);
            command.Parameters.AddWithValue("@changedAt", KsaDateTime.NowText());
            command.ExecuteNonQuery();
        }

        public List<RoomStatusHistoryViewModel> GetRoomStatusHistory(int roomNumber, int take = 20)
        {
            EnsureDatabase();

            var history = new List<RoomStatusHistoryViewModel>();
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT TOP (@take) RoomNumber, OldStatus, NewStatus, ChangedBy, ChangedAt
FROM RoomStatusChangeLog
WHERE RoomNumber = @roomNumber
ORDER BY Id DESC;";
            command.Parameters.AddWithValue("@roomNumber", roomNumber);
            command.Parameters.AddWithValue("@take", take);

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                history.Add(ReadRoomStatusHistory(reader));
            }

            return history;
        }

        /// <summary>
        /// Stores the internal-only room status (e.g. Under Cleaning).
        /// This never touches the PMS database.
        /// </summary>
        public void SetInternalRoomStatus(int roomNumber, string status, string setBy)
        {
            EnsureDatabase();

            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"
MERGE RoomInternalStatus AS target
USING (SELECT @roomNumber AS RoomNumber) AS source
ON target.RoomNumber = source.RoomNumber
WHEN MATCHED THEN
    UPDATE SET Status = @status, SetBy = @setBy, SetAt = @setAt
WHEN NOT MATCHED THEN
    INSERT (RoomNumber, Status, SetBy, SetAt)
    VALUES (@roomNumber, @status, @setBy, @setAt);";
            command.Parameters.AddWithValue("@roomNumber", roomNumber);
            command.Parameters.AddWithValue("@status", status);
            command.Parameters.AddWithValue("@setBy", setBy);
            command.Parameters.AddWithValue("@setAt", KsaDateTime.NowText());
            command.ExecuteNonQuery();
        }

        public void ClearInternalRoomStatus(int roomNumber)
        {
            EnsureDatabase();

            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM RoomInternalStatus WHERE RoomNumber = @roomNumber;";
            command.Parameters.AddWithValue("@roomNumber", roomNumber);
            command.ExecuteNonQuery();
        }

        public string? GetInternalRoomStatus(int roomNumber)
        {
            EnsureDatabase();

            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT Status FROM RoomInternalStatus WHERE RoomNumber = @roomNumber;";
            command.Parameters.AddWithValue("@roomNumber", roomNumber);

            var value = command.ExecuteScalar();
            return value == null || value == DBNull.Value ? null : value.ToString();
        }

        public Dictionary<int, string> GetInternalRoomStatuses()
        {
            EnsureDatabase();

            var statuses = new Dictionary<int, string>();
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT RoomNumber, Status FROM RoomInternalStatus;";

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                statuses[Convert.ToInt32(reader["RoomNumber"])] = reader["Status"]?.ToString() ?? string.Empty;
            }

            return statuses;
        }

        /// <summary>
        /// Room status codes this user is allowed to set.
        /// An empty stored value means every status is allowed.
        /// </summary>
        public HashSet<int> GetAllowedRoomStatusCodes(string userName)
        {
            EnsureDatabase();

            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT TOP 1 ISNULL(AllowedRoomStatuses, '') AS AllowedRoomStatuses
FROM Users
WHERE UserName = @userName;";
            command.Parameters.AddWithValue("@userName", userName);

            var value = command.ExecuteScalar();
            return RoomStatuses.ParseAllowedCodes(value?.ToString());
        }

        public List<RoomStatusHistoryViewModel> GetAllRoomStatusHistory(int take = 500)
        {
            EnsureDatabase();

            var history = new List<RoomStatusHistoryViewModel>();
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT TOP (@take) RoomNumber, OldStatus, NewStatus, ChangedBy, ChangedAt
FROM RoomStatusChangeLog
ORDER BY Id DESC;";
            command.Parameters.AddWithValue("@take", take);

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                history.Add(ReadRoomStatusHistory(reader));
            }

            return history;
        }

        private const string DatabaseName = "HotelRoomsUsers";

        public void EnsureDatabase()
        {
            EnsureDatabaseExists();

            using var connection = OpenConnection();

            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"

    IF OBJECT_ID('dbo.Users', 'U') IS NULL
BEGIN
    CREATE TABLE Users (
        Id BIGINT IDENTITY(1,1) PRIMARY KEY,
        UserName NVARCHAR(100) NOT NULL UNIQUE,
        Password NVARCHAR(255) NOT NULL,
        IsActive BIT NOT NULL DEFAULT 1,
        IsAdmin BIT NOT NULL DEFAULT 0,
        CanChangeRoomStatus BIT NOT NULL DEFAULT 0,
        CreatedAt NVARCHAR(50) NOT NULL
    );
END

IF OBJECT_ID('dbo.RoomStatusChangeLog', 'U') IS NULL
BEGIN
    CREATE TABLE RoomStatusChangeLog (
        Id BIGINT IDENTITY(1,1) PRIMARY KEY,
        RoomNumber INT NOT NULL,
        OldStatus NVARCHAR(100) NOT NULL,
        NewStatus NVARCHAR(100) NOT NULL,
        ChangedBy NVARCHAR(100) NOT NULL,
        ChangedAt NVARCHAR(50) NOT NULL
    );
END

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = 'IX_RoomStatusChangeLog_RoomNumber_Id'
      AND object_id = OBJECT_ID('dbo.RoomStatusChangeLog')
)
BEGIN
    CREATE INDEX IX_RoomStatusChangeLog_RoomNumber_Id
    ON RoomStatusChangeLog (RoomNumber, Id DESC);
END

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = 'IX_RoomStatusChangeLog_Id'
      AND object_id = OBJECT_ID('dbo.RoomStatusChangeLog')
)
BEGIN
    CREATE INDEX IX_RoomStatusChangeLog_Id
    ON RoomStatusChangeLog (Id DESC);
END

IF OBJECT_ID('dbo.RoomInternalStatus', 'U') IS NULL
BEGIN
    CREATE TABLE RoomInternalStatus (
        RoomNumber INT NOT NULL PRIMARY KEY,
        Status NVARCHAR(100) NOT NULL,
        SetBy NVARCHAR(100) NOT NULL,
        SetAt NVARCHAR(50) NOT NULL
    );
END";
                command.ExecuteNonQuery();
            }

            EnsureColumn(connection, "Users", "IsAdmin", "BIT NOT NULL DEFAULT 0");
            EnsureColumn(connection, "Users", "CanChangeRoomStatus", "BIT NOT NULL DEFAULT 0");
            EnsureColumn(connection, "Users", "AllowedRoomStatuses", "NVARCHAR(200) NOT NULL DEFAULT ''");
            EnsureColumn(connection, "Users", "CreatedAt", "NVARCHAR(50) NOT NULL DEFAULT ''");

            EnsureAtLeastOneAdmin(connection);
        }
        private string GetMasterConnectionString()
        {
            var builder = new SqlConnectionStringBuilder(GetConnectionString())
            {
                InitialCatalog = "master"
            };

            return builder.ConnectionString;
        }
        private void EnsureDatabaseExists()
        {
            var masterConnectionString =
                GetMasterConnectionString();

            using var connection = new SqlConnection(masterConnectionString);
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = $@"
IF DB_ID(N'{DatabaseName}') IS NULL
BEGIN
    CREATE DATABASE [{DatabaseName}];
END";
            command.ExecuteNonQuery();
        }

        private SqlConnection OpenConnection()
        {
            var connection = new SqlConnection(GetConnectionString());
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

            return "Server=localhost;Database=HotelRoomsUsers;Trusted_Connection=True;TrustServerCertificate=True;";
        }

        private static AppUserViewModel ReadUser(SqlDataReader reader)
        {
            return new AppUserViewModel
            {
                Id = Convert.ToInt64(reader["Id"]),
                UserName = reader["UserName"]?.ToString() ?? string.Empty,
                IsActive = Convert.ToBoolean(reader["IsActive"]),
                IsAdmin = Convert.ToBoolean(reader["IsAdmin"]),
                CanChangeRoomStatus = Convert.ToBoolean(reader["CanChangeRoomStatus"]),
                AllowedRoomStatuses = reader["AllowedRoomStatuses"]?.ToString() ?? string.Empty,
                CreatedAt = KsaDateTime.FormatStoredValue(reader["CreatedAt"])
            };
        }

        private static RoomStatusHistoryViewModel ReadRoomStatusHistory(SqlDataReader reader)
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

        private static void EnsureColumn(SqlConnection connection, string table, string column, string definition)
        {
            if (ColumnExists(connection, table, column))
            {
                return;
            }

            using var command = connection.CreateCommand();
            command.CommandText = $"ALTER TABLE {table} ADD {column} {definition};";
            command.ExecuteNonQuery();
        }

        private static bool ColumnExists(SqlConnection connection, string table, string column)
        {
            using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT COUNT(1)
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_NAME = @table
  AND COLUMN_NAME = @column;";
            command.Parameters.AddWithValue("@table", table);
            command.Parameters.AddWithValue("@column", column);

            return Convert.ToInt32(command.ExecuteScalar()) > 0;
        }

        private static void EnsureAtLeastOneAdmin(SqlConnection connection)
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
;WITH FirstActiveUser AS (
    SELECT TOP 1 Id
    FROM Users
    WHERE IsActive = 1
    ORDER BY CASE WHEN LOWER(UserName) = 'admin' THEN 0 ELSE 1 END, Id
)
UPDATE Users
SET IsAdmin = 1,
    CanChangeRoomStatus = 1
WHERE Id IN (SELECT Id FROM FirstActiveUser);";
                adminCommand.ExecuteNonQuery();
            }
        }
    }
}