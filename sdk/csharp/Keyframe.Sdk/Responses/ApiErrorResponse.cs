namespace Keyframe.Sdk.Responses
{
    using System.Collections.Generic;
    using Keyframe.Sdk.Enums;

    /// <summary>
    /// Error body returned by the API.
    /// </summary>
    public class ApiErrorResponse
    {
        #region Public-Members

        /// <summary>
        /// Error code.
        /// </summary>
        public ApiErrorEnum Error { get; set; } = ApiErrorEnum.InternalError;

        /// <summary>
        /// HTTP status code.
        /// </summary>
        public int StatusCode { get; set; } = 500;

        /// <summary>
        /// Human-readable message.
        /// </summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// Additional details, such as validation errors. Never null.
        /// </summary>
        public List<string> Details { get; set; } = new List<string>();

        /// <summary>
        /// Request identifier for correlating logs and traces.
        /// </summary>
        public string? RequestId { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate an empty error response.
        /// </summary>
        public ApiErrorResponse()
        {
        }

        /// <summary>
        /// Instantiate an error response.
        /// </summary>
        /// <param name="error">Error code.</param>
        /// <param name="statusCode">HTTP status code.</param>
        /// <param name="message">Message.</param>
        /// <param name="details">Optional details.</param>
        public ApiErrorResponse(ApiErrorEnum error, int statusCode, string message, List<string>? details = null)
        {
            Error = error;
            StatusCode = statusCode;
            Message = message ?? string.Empty;
            if (details != null) Details = details;
        }

        #endregion
    }
}
