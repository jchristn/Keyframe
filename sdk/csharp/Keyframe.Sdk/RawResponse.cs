namespace Keyframe.Sdk
{
    using System;

    /// <summary>
    /// Status code, body, and selected headers of one HTTP exchange.
    /// </summary>
    public class RawResponse
    {
        /// <summary>
        /// HTTP status code.
        /// </summary>
        public int StatusCode { get; set; } = 0;

        /// <summary>
        /// Response body as text. Empty when the server sent no body.
        /// </summary>
        public string Body { get; set; } = String.Empty;

        /// <summary>
        /// Value of the x-request-id response header, or null.
        /// </summary>
        public string? RequestId { get; set; } = null;

        /// <summary>
        /// Value of the x-keyframe-job-id response header (set on a synchronous video result), or null.
        /// </summary>
        public string? JobId { get; set; } = null;

        /// <summary>
        /// Value of the Location response header, or null.
        /// </summary>
        public string? Location { get; set; } = null;
    }
}
