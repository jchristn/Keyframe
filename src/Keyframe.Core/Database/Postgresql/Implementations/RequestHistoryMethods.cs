namespace Keyframe.Core.Database.Postgresql.Implementations
{
    using Keyframe.Core.Database.Common;

    /// <summary>
    /// PostgreSQL request history methods.
    /// </summary>
    public class RequestHistoryMethods : RequestHistoryMethodsBase
    {
        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="driver">Owning driver.</param>
        public RequestHistoryMethods(PostgresqlDatabaseDriver driver) : base(driver)
        {
        }
    }
}
