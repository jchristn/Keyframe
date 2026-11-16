namespace Keyframe.Sdk.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Captured HTTP request and response.
    /// </summary>
    public class RequestHistoryEntry
    {
        #region Public-Members

        /// <summary>
        /// Entry identifier (prefix "req_").
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Request identifier returned to the caller in the x-request-id header.
        /// </summary>
        public string? RequestId { get; set; } = null;

        /// <summary>
        /// Access key name that authenticated the request, "admin" for the admin key, or null when anonymous.
        /// </summary>
        public string? PrincipalName { get; set; } = null;

        /// <summary>
        /// HTTP method (GET, POST, and so on).
        /// </summary>
        public string Method { get; set; } = String.Empty;

        /// <summary>
        /// Route template that matched, or the raw path when no template matched.
        /// </summary>
        public string Path { get; set; } = String.Empty;

        /// <summary>
        /// Full request URL including the query string.
        /// </summary>
        public string Url { get; set; } = String.Empty;

        /// <summary>
        /// Response HTTP status code.
        /// </summary>
        public int StatusCode { get; set; } = 0;

        /// <summary>
        /// Whether the status code was below 400.
        /// </summary>
        public bool Success
        {
            get
            {
                return StatusCode > 0 && StatusCode < 400;
            }
        }

        /// <summary>
        /// Request duration in milliseconds.
        /// </summary>
        public double DurationMs { get; set; } = 0;

        /// <summary>
        /// Client source IP as observed by the server.
        /// </summary>
        public string? SourceIp { get; set; } = null;

        /// <summary>
        /// Request content type.
        /// </summary>
        public string? RequestContentType { get; set; } = null;

        /// <summary>
        /// Response content type.
        /// </summary>
        public string? ResponseContentType { get; set; } = null;

        /// <summary>
        /// Request headers with secrets redacted. Omitted from list results.
        /// </summary>
        public Dictionary<string, string>? RequestHeaders { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Request body, possibly truncated. Omitted from list results.
        /// </summary>
        public string? RequestBody { get; set; } = null;

        /// <summary>
        /// Original request body length in bytes, before truncation.
        /// </summary>
        public long RequestBodyBytes { get; set; } = 0;

        /// <summary>
        /// Whether the request body was truncated.
        /// </summary>
        public bool RequestBodyTruncated { get; set; } = false;

        /// <summary>
        /// Response headers with secrets redacted. Omitted from list results.
        /// </summary>
        public Dictionary<string, string>? ResponseHeaders { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Response body, possibly truncated. Omitted from list results.
        /// </summary>
        public string? ResponseBody { get; set; } = null;

        /// <summary>
        /// Original response body length in bytes, before truncation.
        /// </summary>
        public long ResponseBodyBytes { get; set; } = 0;

        /// <summary>
        /// Whether the response body was truncated.
        /// </summary>
        public bool ResponseBodyTruncated { get; set; } = false;

        /// <summary>
        /// UTC time the request began.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// UTC time the response was sent.
        /// </summary>
        public DateTime? CompletedUtc { get; set; } = null;

        #endregion
    }
}
