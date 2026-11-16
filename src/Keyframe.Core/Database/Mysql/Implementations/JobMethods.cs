namespace Keyframe.Core.Database.Mysql.Implementations
{
    using Keyframe.Core.Database.Common;

    /// <summary>
    /// MySQL job methods.
    /// </summary>
    public class JobMethods : JobMethodsBase
    {
        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="driver">Owning driver.</param>
        public JobMethods(MysqlDatabaseDriver driver) : base(driver)
        {
        }
    }
}
