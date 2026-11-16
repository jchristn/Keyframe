namespace Keyframe.Core.Database.Mysql.Implementations
{
    using Keyframe.Core.Database.Common;

    /// <summary>
    /// MySQL request history methods.
    /// </summary>
    public class RequestHistoryMethods : RequestHistoryMethodsBase
    {
        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="driver">Owning driver.</param>
        public RequestHistoryMethods(MysqlDatabaseDriver driver) : base(driver)
        {
        }
    }
}
