namespace Keyframe.Sdk.Clients
{
    using System;
    using System.Globalization;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using Keyframe.Sdk.Enums;
    using Keyframe.Sdk.Requests;
    using Keyframe.Sdk.Responses;

    /// <summary>
    /// Segment retrieval (POST /v1.0/segment): a video clip, an audio clip, or one still frame cut from a source.
    /// The source is resolved again from Url or Data on every call; Keyframe does not retain uploaded media.
    /// Thread safety: safe for concurrent use.
    /// </summary>
    public class SegmentsClient
    {
        #region Private-Members

        private const string Path = "/v1.0/segment";
        private readonly KeyframeClient _Client;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Create the sub-client. Use the instance exposed on KeyframeClient.
        /// </summary>
        /// <param name="client">Parent client.</param>
        /// <exception cref="ArgumentNullException">Thrown when client is null.</exception>
        public SegmentsClient(KeyframeClient client)
        {
            _Client = client ?? throw new ArgumentNullException(nameof(client));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Cut a segment and return the media bytes with the metadata from the response headers.
        /// When the Content-Disposition header is not visible to the HTTP stack, Filename follows the server's naming
        /// (segment-{StartMs}-{EndMs}.{ext}, or segment-{StartMs}.{ext} for frames) using the extension of the returned media type.
        /// request.ResponseFormat is sent as Binary regardless of its value; the request object is not modified.
        /// </summary>
        /// <param name="request">Request.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Segment media.</returns>
        /// <exception cref="ArgumentNullException">Thrown when request is null.</exception>
        /// <exception cref="KeyframeApiException">Thrown on a non-success status code (400 invalid range or format, 422 missing stream or unresolvable source).</exception>
        /// <exception cref="HttpRequestException">Thrown when the server cannot be reached.</exception>
        /// <exception cref="TimeoutException">Thrown when the request exceeds the client timeout.</exception>
        public async Task<SegmentMedia> GetSegmentAsync(SegmentRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            SegmentRequest copy = WithFormat(request, SegmentResponseFormatEnum.Binary);

            RawResponse raw = await _Client.SendBinaryCheckedAsync(HttpMethod.Post, Path, copy, token).ConfigureAwait(false);
            SegmentMedia media = new SegmentMedia
            {
                MediaType = raw.ContentType ?? "application/octet-stream",
                StartMs = ParseLong(HeaderValue(raw, "x-keyframe-segment-start-ms")) ?? copy.StartMs,
                EndMs = ParseLong(HeaderValue(raw, "x-keyframe-segment-end-ms")) ?? copy.EndMs ?? copy.StartMs,
                DurationMs = ParseLong(HeaderValue(raw, "x-keyframe-segment-duration-ms")) ?? 0,
                RequestId = raw.RequestId,
                Data = raw.Data ?? Array.Empty<byte>()
            };

            media.Filename = ParseFilename(HeaderValue(raw, "Content-Disposition")) ?? DefaultFilename(copy.Output, media);
            return media;
        }

        /// <summary>
        /// Cut a segment and return it as JSON with the media as base64 (SegmentResponse.GetBytes decodes it).
        /// request.ResponseFormat is sent as Json regardless of its value; the request object is not modified.
        /// </summary>
        /// <param name="request">Request.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Segment response.</returns>
        /// <exception cref="ArgumentNullException">Thrown when request is null.</exception>
        /// <exception cref="KeyframeApiException">Thrown on a non-success status code (400 invalid range or format, 422 missing stream or unresolvable source).</exception>
        /// <exception cref="HttpRequestException">Thrown when the server cannot be reached.</exception>
        /// <exception cref="TimeoutException">Thrown when the request exceeds the client timeout.</exception>
        public Task<SegmentResponse> GetSegmentJsonAsync(SegmentRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            SegmentRequest copy = WithFormat(request, SegmentResponseFormatEnum.Json);
            return _Client.SendAsync<SegmentResponse>(HttpMethod.Post, Path, copy, token);
        }

        #endregion

        #region Private-Methods

        private static SegmentRequest WithFormat(SegmentRequest request, SegmentResponseFormatEnum format)
        {
            return new SegmentRequest
            {
                Source = request.Source,
                StartMs = request.StartMs,
                EndMs = request.EndMs,
                Output = request.Output,
                Format = request.Format,
                MaxEdge = request.MaxEdge,
                ResponseFormat = format
            };
        }

        private static string? HeaderValue(RawResponse raw, string name)
        {
            string? value;
            return raw.Headers.TryGetValue(name, out value) ? value : null;
        }

        private static string DefaultFilename(SegmentOutputEnum output, SegmentMedia media)
        {
            string ext = Extension(media.MediaType);
            if (output == SegmentOutputEnum.Frame) return "segment-" + media.StartMs.ToString(CultureInfo.InvariantCulture) + "." + ext;
            return "segment-" + media.StartMs.ToString(CultureInfo.InvariantCulture) + "-" + media.EndMs.ToString(CultureInfo.InvariantCulture) + "." + ext;
        }

        private static string Extension(string mediaType)
        {
            string type = mediaType.Split(';')[0].Trim().ToLowerInvariant();
            switch (type)
            {
                case "video/mp4":
                    return "mp4";
                case "video/webm":
                    return "webm";
                case "audio/mpeg":
                    return "mp3";
                case "audio/wav":
                case "audio/x-wav":
                case "audio/wave":
                    return "wav";
                case "audio/flac":
                case "audio/x-flac":
                    return "flac";
                case "audio/mp4":
                case "audio/x-m4a":
                    return "m4a";
                case "audio/ogg":
                    return "ogg";
                case "image/jpeg":
                    return "jpg";
                case "image/png":
                    return "png";
                default:
                    return "bin";
            }
        }

        private static long? ParseLong(string? value)
        {
            if (String.IsNullOrWhiteSpace(value)) return null;
            long parsed;
            return Int64.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed) ? parsed : null;
        }

        private static string? ParseFilename(string? disposition)
        {
            if (String.IsNullOrWhiteSpace(disposition)) return null;
            foreach (string part in disposition.Split(';'))
            {
                string trimmed = part.Trim();
                if (!trimmed.StartsWith("filename=", StringComparison.OrdinalIgnoreCase)) continue;
                string value = trimmed.Substring("filename=".Length).Trim();
                if (value.Length >= 2 && value.StartsWith("\"", StringComparison.Ordinal) && value.EndsWith("\"", StringComparison.Ordinal))
                    value = value.Substring(1, value.Length - 2);
                return value;
            }

            return null;
        }

        #endregion
    }
}
