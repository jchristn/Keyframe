namespace Keyframe.Core.Database.Sqlite.Implementations
{
    using Keyframe.Core.Database.Common;

    /// <summary>
    /// SQLite job methods.
    /// </summary>
    public class JobMethods : JobMethodsBase
    {
        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="driver">Owning driver.</param>
        public JobMethods(SqliteDatabaseDriver driver) : base(driver)
        {
        }
    }
}
