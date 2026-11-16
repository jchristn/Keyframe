namespace Keyframe.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Net;
    using System.Text.Json;
    using System.Threading.Tasks;
    using Keyframe.Core;
    using Keyframe.Core.Enums;
    using Keyframe.Core.Exceptions;
    using Keyframe.Core.Responses;
    using Keyframe.Core.Serialization;
    using Keyframe.Server.Classes;
    using WatsonWebserver.Core;
    using ApiErrorResponse = Keyframe.Core.Responses.ApiErrorResponse;

    /// <summary>
    /// Shared helpers for route handlers: JSON responses, errors, query and path parameters, and exception mapping.
    /// </summary>
    public static class RouteHelpers
    {
        #region Public-Methods

        /// <summary>
        /// The request context attached by the server, or a fresh unauthenticated one.
        /// </summary>
        /// <param name="context">HTTP context.</param>
        /// <returns>Request context.</returns>
        public static RequestContext Context(HttpContextBase context)
        {
            if (context.Metadata is RequestContext ctx) return ctx;
            RequestContext created = new RequestContext { RequestId = context.Request.Guid.ToString("N") };
            context.Metadata = created;
            return created;
        }

        /// <summary>
        /// Send a JSON body.
        /// </summary>
        /// <param name="context">HTTP context.</param>
        /// <param name="statusCode">Status code.</param>
        /// <param name="body">Body; null sends "null".</param>
        /// <returns>Task.</returns>
        public static async Task JsonAsync(HttpContextBase context, int statusCode, object? body)
        {
            context.Response.StatusCode = statusCode;
            context.Response.ContentType = Constants.JsonContentType;
            await context.Response.Send(KeyframeSerializer.Serialize(body, true), context.Token).ConfigureAwait(false);
        }

        /// <summary>
        /// Send 204 with no body.
        /// </summary>
        /// <param name="context">HTTP context.</param>
        /// <returns>Task.</returns>
        public static async Task NoContentAsync(HttpContextBase context)
        {
            context.Response.StatusCode = 204;
            await context.Response.Send(context.Token).ConfigureAwait(false);
        }

        /// <summary>
        /// Send an error body.
        /// </summary>
        /// <param name="context">HTTP context.</param>
        /// <param name="error">Error code.</param>
        /// <param name="statusCode">Status code.</param>
        /// <param name="message">Message.</param>
        /// <param name="details">Optional details.</param>
        /// <returns>Task.</returns>
        public static async Task ErrorAsync(HttpContextBase context, ApiErrorEnum error, int statusCode, string message, List<string>? details = null)
        {
            if (context.Response.ResponseSent) return;
            ApiErrorResponse body = new ApiErrorResponse(error, statusCode, message, details);
            body.RequestId = Context(context).RequestId;
            await JsonAsync(context, statusCode, body).ConfigureAwait(false);
        }

        /// <summary>
        /// Map an exception to an error response.
        /// </summary>
        /// <param name="context">HTTP context.</param>
        /// <param name="ex">Exception.</param>
        /// <returns>The status code sent.</returns>
        public static async Task<int> ExceptionAsync(HttpContextBase context, Exception ex)
        {
            int status;
            ApiErrorEnum error;
            List<string>? details = null;
            string message = ex.Message;

            switch (ex)
            {
                case RequestValidationException validation:
                    status = 400;
                    error = ApiErrorEnum.BadRequest;
                    details = validation.Errors;
                    message = "The request is invalid.";
                    break;
                case JsonException:
                    status = 400;
                    error = ApiErrorEnum.DeserializationError;
                    message = "The request body is not valid JSON for this route: " + ex.Message;
                    break;
                case EndpointNotFoundException:
                    status = 400;
                    error = ApiErrorEnum.BadRequest;
                    break;
                case QueueFullException:
                    status = 429;
                    error = ApiErrorEnum.TooManyRequests;
                    break;
                case UnsupportedMediaException:
                case MediaResolutionException:
                case MediaProcessingException:
                    status = 422;
                    error = ApiErrorEnum.UnprocessableMedia;
                    break;
                case InferenceException:
                    status = 502;
                    error = ApiErrorEnum.BadGateway;
                    break;
                case TimeoutException:
                    status = 504;
                    error = ApiErrorEnum.Timeout;
                    break;
                case ArgumentException:
                    status = 400;
                    error = ApiErrorEnum.BadRequest;
                    break;
                case InvalidOperationException:
                    status = 409;
                    error = ApiErrorEnum.Conflict;
                    break;
                default:
                    status = 500;
                    error = ApiErrorEnum.InternalError;
                    message = "An internal error occurred. Check the server log for request " + Context(context).RequestId + ".";
                    break;
            }

            await ErrorAsync(context, error, status, message, details).ConfigureAwait(false);
            return status;
        }

        /// <summary>
        /// Deserialize the request body into a named type.
        /// </summary>
        /// <typeparam name="T">Type.</typeparam>
        /// <param name="context">HTTP context.</param>
        /// <returns>Value.</returns>
        /// <exception cref="RequestValidationException">Thrown when the body is missing.</exception>
        /// <exception cref="JsonException">Thrown when the body is not valid JSON for the type.</exception>
        public static T Body<T>(HttpContextBase context) where T : class
        {
            string body = context.Request.DataAsString;
            if (String.IsNullOrWhiteSpace(body)) throw new RequestValidationException(new List<string> { "A JSON request body is required." });
            T? value = KeyframeSerializer.Deserialize<T>(body);
            if (value == null) throw new RequestValidationException(new List<string> { "A JSON request body is required." });
            return value;
        }

        /// <summary>
        /// Path parameter.
        /// </summary>
        /// <param name="context">HTTP context.</param>
        /// <param name="key">Name.</param>
        /// <returns>Value, or null.</returns>
        public static string? Param(HttpContextBase context, string key)
        {
            if (context.Request.Url.Parameters == null) return null;
            string? value = context.Request.Url.Parameters[key];
            return value == null ? null : WebUtility.UrlDecode(value);
        }

        /// <summary>
        /// Decoded query parameter.
        /// </summary>
        /// <param name="context">HTTP context.</param>
        /// <param name="key">Name.</param>
        /// <returns>Value, or null.</returns>
        public static string? Query(HttpContextBase context, string key)
        {
            if (context.Request.Query == null || context.Request.Query.Elements == null) return null;
            string? raw = context.Request.Query.Elements[key];
            if (raw == null) return null;
            return WebUtility.UrlDecode(raw);
        }

        /// <summary>
        /// Integer query parameter.
        /// </summary>
        /// <param name="context">HTTP context.</param>
        /// <param name="key">Name.</param>
        /// <returns>Value, or null when absent.</returns>
        /// <exception cref="ArgumentException">Thrown when present but not an integer.</exception>
        public static int? QueryInt(HttpContextBase context, string key)
        {
            string? value = Query(context, key);
            if (String.IsNullOrWhiteSpace(value)) return null;
            int parsed;
            if (Int32.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed)) return parsed;
            throw new ArgumentException("Query parameter '" + key + "' must be an integer.");
        }

        /// <summary>
        /// UTC timestamp query parameter (ISO 8601).
        /// </summary>
        /// <param name="context">HTTP context.</param>
        /// <param name="key">Name.</param>
        /// <returns>Value, or null when absent.</returns>
        /// <exception cref="ArgumentException">Thrown when present but not a timestamp.</exception>
        public static DateTime? QueryDate(HttpContextBase context, string key)
        {
            if (context.Request.Query == null || context.Request.Query.Elements == null) return null;
            string? raw = context.Request.Query.Elements[key];
            if (String.IsNullOrWhiteSpace(raw)) return null;
            // A plus sign stays a plus sign so offsets such as +02:00 parse.
            string value = raw.Contains('%') ? Uri.UnescapeDataString(raw) : raw;
            DateTime parsed;
            if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out parsed))
                return DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
            throw new ArgumentException("Query parameter '" + key + "' must be an ISO 8601 timestamp.");
        }

        /// <summary>
        /// Enum query parameter.
        /// </summary>
        /// <typeparam name="T">Enum type.</typeparam>
        /// <param name="context">HTTP context.</param>
        /// <param name="key">Name.</param>
        /// <returns>Value, or null when absent.</returns>
        /// <exception cref="ArgumentException">Thrown when present but not a member name.</exception>
        public static T? QueryEnum<T>(HttpContextBase context, string key) where T : struct, Enum
        {
            string? value = Query(context, key);
            if (String.IsNullOrWhiteSpace(value)) return null;
            T parsed;
            if (Enum.TryParse<T>(value, true, out parsed) && Enum.IsDefined(typeof(T), parsed)) return parsed;
            throw new ArgumentException("Query parameter '" + key + "' must be one of: " + String.Join(", ", Enum.GetNames(typeof(T))) + ".");
        }

        #endregion
    }
}
