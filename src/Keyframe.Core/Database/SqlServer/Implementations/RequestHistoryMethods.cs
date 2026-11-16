namespace Keyframe.Core.Database.SqlServer.Implementations
{
    using Keyframe.Core.Database.Common;

    /// <summary>
    /// SQL Server request history methods.
    /// </summary>
    public class RequestHistoryMethods : RequestHistoryMethodsBase
    {
        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="driver">Owning driver.</param>
        public RequestHistoryMethods(SqlServerDatabaseDriver driver) : base(driver)
        {
        }
    }
}
