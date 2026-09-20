using System.IO;
using System.Data.SQLite;

namespace GatewayDemo.Legacy.Web.Infrastructure
{
    public static class GatewayDatabaseInitializer
    {
        private const string Schema = @"
PRAGMA foreign_keys = ON;
CREATE TABLE IF NOT EXISTS GatewayManagedDevices (
    Id TEXT NOT NULL PRIMARY KEY,
    DeviceId TEXT NOT NULL UNIQUE,
    DeviceCode TEXT NOT NULL,
    TrustState INTEGER NOT NULL,
    ChallengeReason TEXT NULL,
    SignalHash TEXT NOT NULL,
    UserAgent TEXT NOT NULL,
    RegisteredIp TEXT NOT NULL,
    LastSeenIp TEXT NULL,
    HasAppCredential INTEGER NOT NULL DEFAULT 0,
    CreatedAtUtc TEXT NOT NULL,
    LastSeenAtUtc TEXT NULL,
    ChallengedAtUtc TEXT NULL
);
CREATE TABLE IF NOT EXISTS GatewayBrowserCredentials (
    Id TEXT NOT NULL PRIMARY KEY,
    DeviceId TEXT NOT NULL UNIQUE,
    TokenHash TEXT NOT NULL UNIQUE,
    CreatedAtUtc TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS GatewayAppCredentials (
    Id TEXT NOT NULL PRIMARY KEY,
    DeviceId TEXT NOT NULL UNIQUE,
    CredentialKey TEXT NOT NULL UNIQUE,
    ProtectedSecret TEXT NOT NULL,
    CreatedAtUtc TEXT NOT NULL,
    LastRotatedAtUtc TEXT NULL,
    RevokedAtUtc TEXT NULL,
    RevokedReason TEXT NULL
);
CREATE TABLE IF NOT EXISTS GatewayLegacyAppIdentities (
    Id TEXT NOT NULL PRIMARY KEY,
    DeviceId TEXT NOT NULL,
    IdentityHash TEXT NOT NULL UNIQUE,
    IdentitySummary TEXT NOT NULL,
    CreatedAtUtc TEXT NOT NULL,
    LastSeenAtUtc TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS IX_GatewayLegacyAppIdentities_DeviceId
    ON GatewayLegacyAppIdentities (DeviceId);
CREATE TABLE IF NOT EXISTS GatewayLegacyAppSessions (
    Id TEXT NOT NULL PRIMARY KEY,
    DeviceId TEXT NOT NULL,
    SessionKeyHash TEXT NOT NULL UNIQUE,
    SessionPreview TEXT NOT NULL,
    CreatedAtUtc TEXT NOT NULL,
    LastSeenAtUtc TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS IX_GatewayLegacyAppSessions_DeviceId
    ON GatewayLegacyAppSessions (DeviceId);
CREATE TABLE IF NOT EXISTS GatewayAccessRequests (
    Id TEXT NOT NULL PRIMARY KEY,
    DeviceId TEXT NOT NULL,
    DeviceCode TEXT NOT NULL,
    SignalHash TEXT NOT NULL,
    CompanyName TEXT NOT NULL,
    ApplicantName TEXT NOT NULL,
    Phone TEXT NOT NULL,
    Reason TEXT NULL,
    TargetPath TEXT NOT NULL,
    ClientIp TEXT NOT NULL,
    LegacyUrlBefore TEXT NULL,
    LegacyCompanyId TEXT NULL,
    LegacyUserId TEXT NULL,
    LegacyDataCenterId TEXT NULL,
    LegacyDeviceImei TEXT NULL,
    LegacyMobileUserNum TEXT NULL,
    AllowAllSites INTEGER NOT NULL DEFAULT 0,
    Status INTEGER NOT NULL,
    ReviewNote TEXT NULL,
    ReviewedBy TEXT NULL,
    ExternalApprovalId TEXT NULL,
    CreatedAtUtc TEXT NOT NULL,
    UpdatedAtUtc TEXT NOT NULL,
    ReviewedAtUtc TEXT NULL,
    ProcessedAtUtc TEXT NULL
);
CREATE INDEX IF NOT EXISTS IX_GatewayAccessRequests_DeviceId_Status
    ON GatewayAccessRequests (DeviceId, Status);
CREATE TABLE IF NOT EXISTS GatewayAccessRequestSites (
    Id TEXT NOT NULL PRIMARY KEY,
    RequestId TEXT NOT NULL,
    SiteKey TEXT NOT NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS IX_GatewayAccessRequestSites_RequestId_SiteKey
    ON GatewayAccessRequestSites (RequestId, SiteKey);
CREATE TABLE IF NOT EXISTS GatewayDeviceAuthorizations (
    Id TEXT NOT NULL PRIMARY KEY,
    DeviceId TEXT NOT NULL,
    DeviceCode TEXT NOT NULL,
    CompanyName TEXT NOT NULL,
    ApplicantName TEXT NOT NULL,
    Phone TEXT NOT NULL,
    LegacyCompanyId TEXT NULL,
    LegacyUrlBefore TEXT NULL,
    LegacyUserId TEXT NULL,
    LegacyDataCenterId TEXT NULL,
    LegacyDeviceImei TEXT NULL,
    LegacyMobileUserNum TEXT NULL,
    Status INTEGER NOT NULL,
    PolicyMode TEXT NOT NULL,
    ReviewNote TEXT NULL,
    LastSeenIp TEXT NULL,
    LastSeenSiteKey TEXT NULL,
    LastSeenPath TEXT NULL,
    CreatedAtUtc TEXT NOT NULL,
    ApprovedAtUtc TEXT NOT NULL,
    LastSeenAtUtc TEXT NULL,
    RevokedAtUtc TEXT NULL
);
CREATE INDEX IF NOT EXISTS IX_GatewayDeviceAuthorizations_DeviceId_Status
    ON GatewayDeviceAuthorizations (DeviceId, Status);
CREATE TABLE IF NOT EXISTS GatewayDeviceAuthorizationSites (
    Id TEXT NOT NULL PRIMARY KEY,
    AuthorizationId TEXT NOT NULL,
    SiteKey TEXT NOT NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS IX_GatewayDeviceAuthorizationSites_AuthorizationId_SiteKey
    ON GatewayDeviceAuthorizationSites (AuthorizationId, SiteKey);
CREATE TABLE IF NOT EXISTS GatewayAppReplayNonces (
    Id TEXT NOT NULL PRIMARY KEY,
    CredentialKey TEXT NOT NULL,
    NonceHash TEXT NOT NULL,
    CreatedAtUtc TEXT NOT NULL,
    ExpiresAtUtc TEXT NOT NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS IX_GatewayAppReplayNonces_CredentialKey_NonceHash
    ON GatewayAppReplayNonces (CredentialKey, NonceHash);
CREATE INDEX IF NOT EXISTS IX_GatewayAppReplayNonces_ExpiresAtUtc
    ON GatewayAppReplayNonces (ExpiresAtUtc);
CREATE TABLE IF NOT EXISTS GatewayAuditLogs (
    Id TEXT NOT NULL PRIMARY KEY,
    Kind TEXT NOT NULL,
    Message TEXT NOT NULL,
    DeviceId TEXT NOT NULL,
    DeviceCode TEXT NOT NULL,
    SiteKey TEXT NULL,
    ClientIp TEXT NULL,
    CreatedAtUtc TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS IX_GatewayAuditLogs_CreatedAtUtc
    ON GatewayAuditLogs (CreatedAtUtc);
CREATE TABLE IF NOT EXISTS GatewayWebViewHandoffTickets (
    Id TEXT NOT NULL PRIMARY KEY,
    TicketHash TEXT NOT NULL UNIQUE,
    DeviceId TEXT NOT NULL,
    SiteKey TEXT NOT NULL,
    HostBinding TEXT NOT NULL,
    TargetPath TEXT NOT NULL,
    ExpiresAtUtc TEXT NOT NULL,
    ConsumedAtUtc TEXT NULL,
    CreatedAtUtc TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS IX_GatewayWebViewHandoffTickets_DeviceId
    ON GatewayWebViewHandoffTickets (DeviceId);
CREATE TABLE IF NOT EXISTS GatewayWebViewCredentials (
    Id TEXT NOT NULL PRIMARY KEY,
    TokenHash TEXT NOT NULL UNIQUE,
    DeviceId TEXT NOT NULL,
    SiteKey TEXT NOT NULL,
    HostBinding TEXT NOT NULL,
    TargetPath TEXT NOT NULL,
    ExpiresAtUtc TEXT NOT NULL,
    RevokedAtUtc TEXT NULL,
    CreatedAtUtc TEXT NOT NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS IX_GatewayWebViewCredentials_DeviceId_SiteKey
    ON GatewayWebViewCredentials (DeviceId, SiteKey);
CREATE INDEX IF NOT EXISTS IX_GatewayWebViewCredentials_DeviceId
    ON GatewayWebViewCredentials (DeviceId);";

        public static void EnsureCreated(LegacyGatewayConfiguration configuration)
        {
            var directory = Path.GetDirectoryName(configuration.DatabaseFilePath);
            if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            if (!File.Exists(configuration.DatabaseFilePath))
            {
                SQLiteConnection.CreateFile(configuration.DatabaseFilePath);
            }

            using (var connection = new SQLiteConnection(configuration.Storage.SqliteConnectionString))
            {
                connection.Open();
                SQLiteConnectionConfigurator.ConfigureDatabase(connection);
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = Schema;
                    command.ExecuteNonQuery();
                }

                EnsureColumn(connection, "GatewayAccessRequests", "LegacyUrlBefore", "TEXT NULL");
                EnsureColumn(connection, "GatewayAccessRequests", "LegacyCompanyId", "TEXT NULL");
                EnsureColumn(connection, "GatewayAccessRequests", "LegacyUserId", "TEXT NULL");
                EnsureColumn(connection, "GatewayAccessRequests", "LegacyDataCenterId", "TEXT NULL");
                EnsureColumn(connection, "GatewayAccessRequests", "LegacyDeviceImei", "TEXT NULL");
                EnsureColumn(connection, "GatewayAccessRequests", "LegacyMobileUserNum", "TEXT NULL");
                EnsureColumn(connection, "GatewayAccessRequests", "AllowAllSites", "INTEGER NOT NULL DEFAULT 0");
                EnsureColumn(connection, "GatewayAccessRequests", "ReviewNote", "TEXT NULL");
                EnsureColumn(connection, "GatewayAccessRequests", "ReviewedBy", "TEXT NULL");
                EnsureColumn(connection, "GatewayAccessRequests", "ExternalApprovalId", "TEXT NULL");
                EnsureColumn(connection, "GatewayAccessRequests", "ReviewedAtUtc", "TEXT NULL");
                EnsureColumn(connection, "GatewayAccessRequests", "ProcessedAtUtc", "TEXT NULL");
                EnsureColumn(connection, "GatewayDeviceAuthorizations", "LegacyCompanyId", "TEXT NULL");
                EnsureColumn(connection, "GatewayDeviceAuthorizations", "LegacyUrlBefore", "TEXT NULL");
                EnsureColumn(connection, "GatewayDeviceAuthorizations", "LegacyUserId", "TEXT NULL");
                EnsureColumn(connection, "GatewayDeviceAuthorizations", "LegacyDataCenterId", "TEXT NULL");
                EnsureColumn(connection, "GatewayDeviceAuthorizations", "LegacyDeviceImei", "TEXT NULL");
                EnsureColumn(connection, "GatewayDeviceAuthorizations", "LegacyMobileUserNum", "TEXT NULL");
                EnsureColumn(connection, "GatewayDeviceAuthorizations", "PolicyMode", "TEXT NOT NULL DEFAULT 'device'");
                EnsureColumn(connection, "GatewayDeviceAuthorizations", "ReviewNote", "TEXT NULL");
                EnsureColumn(connection, "GatewayDeviceAuthorizations", "LastSeenSiteKey", "TEXT NULL");
                EnsureColumn(connection, "GatewayDeviceAuthorizations", "LastSeenPath", "TEXT NULL");
                EnsureColumn(connection, "GatewayAppCredentials", "RevokedAtUtc", "TEXT NULL");
                EnsureColumn(connection, "GatewayAppCredentials", "RevokedReason", "TEXT NULL");
                EnsureColumn(connection, "GatewayWebViewCredentials", "TargetPath", "TEXT NOT NULL DEFAULT '/'");
                EnsureColumn(connection, "GatewayManagedDevices", "ChallengeReason", "TEXT NULL");
                EnsureColumn(connection, "GatewayManagedDevices", "ChallengedAtUtc", "TEXT NULL");
                EnsureColumn(connection, "GatewayManagedDevices", "HasAppCredential", "INTEGER NOT NULL DEFAULT 0");
                EnsureColumn(connection, "GatewayAuditLogs", "SiteKey", "TEXT NULL");
                EnsureColumn(connection, "GatewayAuditLogs", "ClientIp", "TEXT NULL");
                EnsureLegacyAppIdentitiesAllowMultiplePerDevice(connection);
            }
        }

        private static void EnsureColumn(SQLiteConnection connection, string tableName, string columnName, string definition)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "PRAGMA table_info(" + tableName + ");";
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        if (string.Equals(System.Convert.ToString(reader["name"]), columnName, System.StringComparison.OrdinalIgnoreCase))
                        {
                            return;
                        }
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "ALTER TABLE " + tableName + " ADD COLUMN " + columnName + " " + definition + ";";
                command.ExecuteNonQuery();
            }
        }

        private static void EnsureLegacyAppIdentitiesAllowMultiplePerDevice(SQLiteConnection connection)
        {
            if (!HasUniqueIndexOnSingleColumn(connection, "GatewayLegacyAppIdentities", "DeviceId"))
            {
                return;
            }

            using (var transaction = connection.BeginTransaction())
            {
                using (var command = connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = @"
CREATE TABLE IF NOT EXISTS GatewayLegacyAppIdentities_New (
    Id TEXT NOT NULL PRIMARY KEY,
    DeviceId TEXT NOT NULL,
    IdentityHash TEXT NOT NULL UNIQUE,
    IdentitySummary TEXT NOT NULL,
    CreatedAtUtc TEXT NOT NULL,
    LastSeenAtUtc TEXT NOT NULL
);
INSERT OR IGNORE INTO GatewayLegacyAppIdentities_New (
    Id, DeviceId, IdentityHash, IdentitySummary, CreatedAtUtc, LastSeenAtUtc
)
SELECT Id, DeviceId, IdentityHash, IdentitySummary, CreatedAtUtc, LastSeenAtUtc
FROM GatewayLegacyAppIdentities;
DROP TABLE GatewayLegacyAppIdentities;
ALTER TABLE GatewayLegacyAppIdentities_New RENAME TO GatewayLegacyAppIdentities;
CREATE INDEX IF NOT EXISTS IX_GatewayLegacyAppIdentities_DeviceId
    ON GatewayLegacyAppIdentities (DeviceId);";
                    command.ExecuteNonQuery();
                }

                transaction.Commit();
            }
        }

        private static bool HasUniqueIndexOnSingleColumn(SQLiteConnection connection, string tableName, string columnName)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "PRAGMA index_list(" + tableName + ");";
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        if (System.Convert.ToInt32(reader["unique"]) != 1)
                        {
                            continue;
                        }

                        var indexName = System.Convert.ToString(reader["name"]);
                        if (IndexContainsOnlyColumn(connection, indexName, columnName))
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        private static bool IndexContainsOnlyColumn(SQLiteConnection connection, string indexName, string columnName)
        {
            if (string.IsNullOrWhiteSpace(indexName))
            {
                return false;
            }

            var columnCount = 0;
            var matchesColumn = false;
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "PRAGMA index_info(" + QuoteSqlIdentifier(indexName) + ");";
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        columnCount++;
                        if (string.Equals(System.Convert.ToString(reader["name"]), columnName, System.StringComparison.OrdinalIgnoreCase))
                        {
                            matchesColumn = true;
                        }
                    }
                }
            }

            return columnCount == 1 && matchesColumn;
        }

        private static string QuoteSqlIdentifier(string identifier)
        {
            return "'" + (identifier ?? string.Empty).Replace("'", "''") + "'";
        }
    }
}
