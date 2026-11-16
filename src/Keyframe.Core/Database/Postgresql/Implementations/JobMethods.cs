namespace Keyframe.Core.Database.Postgresql.Implementations
{
    using Keyframe.Core.Database.Common;

    /// <summary>
    /// PostgreSQL job methods.
    /// </summary>
    public class JobMethods : JobMethodsBase
    {
        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="driver">Owning driver.</param>
        public JobMethods(PostgresqlDatabaseDriver driver) : base(driver)
        {
        }
    }
}
