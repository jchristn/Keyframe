namespace Keyframe.Core.Database.SqlServer.Implementations
{
    using Keyframe.Core.Database.Common;

    /// <summary>
    /// SQL Server job methods.
    /// </summary>
    public class JobMethods : JobMethodsBase
    {
        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="driver">Owning driver.</param>
        public JobMethods(SqlServerDatabaseDriver driver) : base(driver)
        {
        }
    }
}
