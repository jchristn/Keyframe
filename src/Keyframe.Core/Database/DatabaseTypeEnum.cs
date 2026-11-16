namespace Keyframe.Core.Database
{
    /// <summary>
    /// Supported database providers.
    /// </summary>
    public enum DatabaseTypeEnum
    {
        /// <summary>
        /// SQLite (default).
        /// </summary>
        Sqlite,

        /// <summary>
        /// MySQL.
        /// </summary>
        Mysql,

        /// <summary>
        /// PostgreSQL.
        /// </summary>
        Postgresql,

        /// <summary>
        /// Microsoft SQL Server.
        /// </summary>
        SqlServer
    }
}
