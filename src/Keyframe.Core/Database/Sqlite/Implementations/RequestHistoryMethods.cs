namespace Keyframe.Core.Database.Sqlite.Implementations
{
    using Keyframe.Core.Database.Common;

    /// <summary>
    /// SQLite request history methods.
    /// </summary>
    public class RequestHistoryMethods : RequestHistoryMethodsBase
    {
        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="driver">Owning driver.</param>
        public RequestHistoryMethods(SqliteDatabaseDriver driver) : base(driver)
        {
        }
    }
}
