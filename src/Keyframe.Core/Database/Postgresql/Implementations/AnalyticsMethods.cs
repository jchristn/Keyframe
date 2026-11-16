namespace Keyframe.Core.Database.Postgresql.Implementations
{
    using Keyframe.Core.Database.Common;

    /// <summary>
    /// PostgreSQL analytics methods.
    /// </summary>
    public class AnalyticsMethods : AnalyticsMethodsBase
    {
        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="driver">Owning driver.</param>
        public AnalyticsMethods(PostgresqlDatabaseDriver driver) : base(driver)
        {
        }
    }
}
