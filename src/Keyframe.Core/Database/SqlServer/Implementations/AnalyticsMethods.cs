namespace Keyframe.Core.Database.SqlServer.Implementations
{
    using Keyframe.Core.Database.Common;

    /// <summary>
    /// SQL Server analytics methods.
    /// </summary>
    public class AnalyticsMethods : AnalyticsMethodsBase
    {
        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="driver">Owning driver.</param>
        public AnalyticsMethods(SqlServerDatabaseDriver driver) : base(driver)
        {
        }
    }
}
