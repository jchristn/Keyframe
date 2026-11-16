namespace Keyframe.Sdk.Responses
{
    using System.Collections.Generic;

    /// <summary>
    /// A page of results.
    /// </summary>
    /// <typeparam name="T">Item type.</typeparam>
    public class EnumerationResult<T>
    {
        /// <summary>
        /// Items on this page. Never null.
        /// </summary>
        public List<T> Items { get; set; } = new List<T>();

        /// <summary>
        /// One-based page number.
        /// </summary>
        public int PageNumber { get; set; } = 1;

        /// <summary>
        /// Page size.
        /// </summary>
        public int PageSize { get; set; } = 25;

        /// <summary>
        /// Total matching records.
        /// </summary>
        public long TotalCount { get; set; } = 0;

        /// <summary>
        /// Total pages.
        /// </summary>
        public int TotalPages { get; set; } = 0;
    }
}
