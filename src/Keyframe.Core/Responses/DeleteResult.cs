namespace Keyframe.Core.Responses
{
    /// <summary>
    /// Result of a bulk delete.
    /// </summary>
    public class DeleteResult
    {
        /// <summary>
        /// Rows deleted.
        /// </summary>
        public int DeletedCount { get; set; } = 0;
    }
}
