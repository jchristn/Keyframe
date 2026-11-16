namespace Keyframe.Core.Database.Sqlite.Implementations
{
    using Keyframe.Core.Database.Common;

    /// <summary>
    /// SQLite analytics methods.
    /// </summary>
    public class AnalyticsMethods : AnalyticsMethodsBase
    {
        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="driver">Owning driver.</param>
        public AnalyticsMethods(SqliteDatabaseDriver driver) : base(driver)
        {
        }
    }
}
