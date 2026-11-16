namespace Keyframe.Sdk
{
    using System;
    using System.Linq;
    using Keyframe.Sdk.Enums;
    using Keyframe.Sdk.Responses;

    /// <summary>
    /// Thrown when the Keyframe server answers with a non-success HTTP status code.
    /// Carries the status code and, when the body could be parsed, the server's ApiErrorResponse.
    /// </summary>
    public class KeyframeApiException : Exception
    {
        #region Public-Members

        /// <summary>
        /// HTTP status code returned by the server, for example 400, 401, 403, 404, 409, 422, 429, 502, or 504.
        /// </summary>
        public int StatusCode { get; }

        /// <summary>
        /// Parsed error body. Null when the server returned an empty or non-JSON body.
        /// </summary>
        public ApiErrorResponse? ApiError { get; }

        /// <summary>
        /// Error category from the parsed body, or null when no body was parsed.
        /// </summary>
        public ApiErrorEnum? Error
        {
            get
            {
                return ApiError?.Error;
            }
        }

        /// <summary>
        /// Server request identifier (x-request-id), from the error body or response header. May be null.
        /// </summary>
        public string? RequestId { get; }

        /// <summary>
        /// Raw response body text. May be null or empty.
        /// </summary>
        public string? ResponseBody { get; }

        /// <summary>
        /// HTTP method of the failed request.
        /// </summary>
        public string Method { get; }

        /// <summary>
        /// Request path of the failed request, relative to the base URL.
        /// </summary>
        public string Path { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Create an exception for a failed request.
        /// </summary>
        /// <param name="method">HTTP method.</param>
        /// <param name="path">Request path.</param>
        /// <param name="statusCode">HTTP status code.</param>
        /// <param name="apiError">Parsed error body, or null.</param>
        /// <param name="responseBody">Raw response body, or null.</param>
        /// <param name="requestId">Request identifier, or null.</param>
        public KeyframeApiException(string method, string path, int statusCode, ApiErrorResponse? apiError, string? responseBody, string? requestId)
            : base(BuildMessage(method, path, statusCode, apiError))
        {
            Method = method ?? string.Empty;
            Path = path ?? string.Empty;
            StatusCode = statusCode;
            ApiError = apiError;
            ResponseBody = responseBody;
            RequestId = apiError?.RequestId ?? requestId;
        }

        #endregion

        #region Private-Methods

        private static string BuildMessage(string method, string path, int statusCode, ApiErrorResponse? apiError)
        {
            string prefix = (method ?? string.Empty) + " " + (path ?? string.Empty) + " returned " + statusCode;
            if (apiError == null) return prefix + ".";
            string message = prefix + " " + apiError.Error + ": " + apiError.Message;
            if (apiError.Details != null && apiError.Details.Any()) message += " (" + String.Join("; ", apiError.Details) + ")";
            return message;
        }

        #endregion
    }
}
