namespace Keyframe.Core.Database.Mysql.Implementations
{
    using Keyframe.Core.Database.Common;

    /// <summary>
    /// MySQL analytics methods.
    /// </summary>
    public class AnalyticsMethods : AnalyticsMethodsBase
    {
        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="driver">Owning driver.</param>
        public AnalyticsMethods(MysqlDatabaseDriver driver) : base(driver)
        {
        }
    }
}
