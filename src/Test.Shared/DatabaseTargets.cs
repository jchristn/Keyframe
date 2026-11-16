namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using Keyframe.Core.Database;

    /// <summary>
    /// Database providers to test. SQLite always runs. Server providers run when listed in
    /// KEYFRAME_TEST_DATABASES (comma separated: Mysql, Postgresql, SqlServer), using
    /// KEYFRAME_TEST_DB_HOST (default 127.0.0.1), KEYFRAME_TEST_DB_PASSWORD (default Keyframe_Test_1), and
    /// KEYFRAME_TEST_MYSQL_PORT, KEYFRAME_TEST_POSTGRESQL_PORT, KEYFRAME_TEST_SQLSERVER_PORT to override default ports.
    /// </summary>
    public static class DatabaseTargets
    {
        /// <summary>
        /// Providers enabled for this run.
        /// </summary>
        /// <returns>Provider types.</returns>
        public static List<DatabaseTypeEnum> Enabled()
        {
            List<DatabaseTypeEnum> list = new List<DatabaseTypeEnum> { DatabaseTypeEnum.Sqlite };
            string? env = Environment.GetEnvironmentVariable("KEYFRAME_TEST_DATABASES");
            if (String.IsNullOrWhiteSpace(env)) return list;

            foreach (string part in env.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                DatabaseTypeEnum parsed;
                if (Enum.TryParse<DatabaseTypeEnum>(part, true, out parsed) && !list.Contains(parsed)) list.Add(parsed);
            }

            return list;
        }

        /// <summary>
        /// Settings for a provider. Server providers get a fresh database name per call.
        /// </summary>
        /// <param name="type">Provider.</param>
        /// <returns>Settings.</returns>
        public static DatabaseSettings Settings(DatabaseTypeEnum type)
        {
            string host = Environment.GetEnvironmentVariable("KEYFRAME_TEST_DB_HOST") ?? "127.0.0.1";
            string password = Environment.GetEnvironmentVariable("KEYFRAME_TEST_DB_PASSWORD") ?? "Keyframe_Test_1";
            string dbName = "kftest_" + Guid.NewGuid().ToString("N").Substring(0, 12);

            switch (type)
            {
                case DatabaseTypeEnum.Mysql:
                    return new DatabaseSettings { Type = type, Hostname = host, Port = Port("KEYFRAME_TEST_MYSQL_PORT", 3306), DatabaseName = dbName, Username = "root", Password = password };
                case DatabaseTypeEnum.Postgresql:
                    return new DatabaseSettings { Type = type, Hostname = host, Port = Port("KEYFRAME_TEST_POSTGRESQL_PORT", 5432), DatabaseName = dbName, Username = "postgres", Password = password };
                case DatabaseTypeEnum.SqlServer:
                    return new DatabaseSettings { Type = type, Hostname = host, Port = Port("KEYFRAME_TEST_SQLSERVER_PORT", 1433), DatabaseName = dbName, Username = "sa", Password = password };
                default:
                    return new DatabaseSettings { Type = DatabaseTypeEnum.Sqlite, Filename = Path.Combine(TestEnvironment.Root, "db-" + Guid.NewGuid().ToString("N") + ".db") };
            }
        }

        private static int Port(string variable, int fallback)
        {
            string? value = Environment.GetEnvironmentVariable(variable);
            int parsed;
            if (!String.IsNullOrWhiteSpace(value) && Int32.TryParse(value, out parsed)) return parsed;
            return fallback;
        }
    }
}
