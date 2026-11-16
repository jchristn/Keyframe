namespace Keyframe.Core.Enums
{
    /// <summary>
    /// API error codes.
    /// </summary>
    public enum ApiErrorEnum
    {
        /// <summary>
        /// The request was malformed or failed validation.
        /// </summary>
        BadRequest,

        /// <summary>
        /// The request body was empty.
        /// </summary>
        RequestBodyMissing,

        /// <summary>
        /// The request body could not be parsed.
        /// </summary>
        DeserializationError,

        /// <summary>
        /// No valid credential was supplied.
        /// </summary>
        AuthenticationFailed,

        /// <summary>
        /// The credential is not permitted to perform the operation.
        /// </summary>
        AuthorizationFailed,

        /// <summary>
        /// The resource does not exist.
        /// </summary>
        NotFound,

        /// <summary>
        /// The resource is in a state that does not allow the operation.
        /// </summary>
        Conflict,

        /// <summary>
        /// The media could not be decoded or is not supported.
        /// </summary>
        UnprocessableMedia,

        /// <summary>
        /// Rate limit or queue capacity exceeded.
        /// </summary>
        TooManyRequests,

        /// <summary>
        /// An upstream model endpoint failed or is unhealthy.
        /// </summary>
        BadGateway,

        /// <summary>
        /// The operation timed out.
        /// </summary>
        Timeout,

        /// <summary>
        /// An unexpected server error occurred.
        /// </summary>
        InternalError
    }
}
