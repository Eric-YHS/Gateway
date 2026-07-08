using System.Data.SQLite;

namespace GatewayDemo.Legacy.Web.Infrastructure
{
    internal static class SQLiteConnectionConfigurator
    {
        public const int BusyTimeoutMilliseconds = 15000;
        public const int CommandTimeoutSeconds = 30;

        public static void ConfigureOpenConnection(SQLiteConnection connection)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandTimeout = CommandTimeoutSeconds;
                command.CommandText = @"
PRAGMA foreign_keys = ON;
PRAGMA busy_timeout = 15000;
PRAGMA synchronous = NORMAL;";
                command.ExecuteNonQuery();
            }
        }

        public static void ConfigureDatabase(SQLiteConnection connection)
        {
            ConfigureOpenConnection(connection);

            using (var command = connection.CreateCommand())
            {
                command.CommandTimeout = CommandTimeoutSeconds;
                command.CommandText = @"
PRAGMA journal_mode = WAL;
PRAGMA wal_autocheckpoint = 1000;
PRAGMA synchronous = NORMAL;";
                command.ExecuteNonQuery();
            }
        }
    }
}
