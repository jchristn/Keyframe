namespace Keyframe.Sdk
{
    using System;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Keyframe.Sdk.Requests;

    /// <summary>
    /// Helpers that build MediaSource values from URLs, bytes, and files.
    /// Thread safety: stateless and safe for concurrent use.
    /// </summary>
    public static class MediaSources
    {
        #region Public-Methods

        /// <summary>
        /// Media referenced by URL. The server downloads it.
        /// </summary>
        /// <param name="url">Absolute http or https URL.</param>
        /// <param name="mediaType">Optional MIME type hint.</param>
        /// <returns>Media source.</returns>
        /// <exception cref="ArgumentException">Thrown when url is not an absolute http or https URL.</exception>
        public static MediaSource FromUrl(string url, string? mediaType = null)
        {
            if (String.IsNullOrWhiteSpace(url)
                || !Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                throw new ArgumentException("URL must be an absolute http or https URL.", nameof(url));
            return new MediaSource { Url = url, MediaType = mediaType };
        }

        /// <summary>
        /// Media sent inline as base64.
        /// </summary>
        /// <param name="data">Media bytes.</param>
        /// <param name="mediaType">Optional MIME type hint; detection uses magic bytes.</param>
        /// <returns>Media source.</returns>
        /// <exception cref="ArgumentNullException">Thrown when data is null.</exception>
        /// <exception cref="ArgumentException">Thrown when data is empty.</exception>
        public static MediaSource FromBytes(byte[] data, string? mediaType = null)
        {
            ArgumentNullException.ThrowIfNull(data);
            if (data.Length < 1) throw new ArgumentException("Media data is empty.", nameof(data));
            return new MediaSource { Data = Convert.ToBase64String(data), MediaType = mediaType };
        }

        /// <summary>
        /// Media read from a local file and sent inline as base64.
        /// </summary>
        /// <param name="path">File path.</param>
        /// <param name="mediaType">Optional MIME type hint. Null infers it from common extensions.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Media source.</returns>
        /// <exception cref="ArgumentNullException">Thrown when path is null or empty.</exception>
        /// <exception cref="FileNotFoundException">Thrown when the file does not exist.</exception>
        /// <exception cref="ArgumentException">Thrown when the file is empty.</exception>
        public static async Task<MediaSource> FromFileAsync(string path, string? mediaType = null, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(path)) throw new ArgumentNullException(nameof(path));
            if (!File.Exists(path)) throw new FileNotFoundException("Media file not found.", path);
            byte[] data = await File.ReadAllBytesAsync(path, token).ConfigureAwait(false);
            return FromBytes(data, mediaType ?? GuessMediaType(path));
        }

        /// <summary>
        /// Guess a MIME type from a file extension. Returns null for unknown extensions.
        /// </summary>
        /// <param name="path">File path or name.</param>
        /// <returns>MIME type or null.</returns>
        public static string? GuessMediaType(string? path)
        {
            if (String.IsNullOrWhiteSpace(path)) return null;
            string ext = Path.GetExtension(path).ToLowerInvariant();
            switch (ext)
            {
                case ".jpg":
                case ".jpeg":
                    return "image/jpeg";
                case ".png":
                    return "image/png";
                case ".gif":
                    return "image/gif";
                case ".webp":
                    return "image/webp";
                case ".bmp":
                    return "image/bmp";
                case ".tif":
                case ".tiff":
                    return "image/tiff";
                case ".mp4":
                case ".m4v":
                    return "video/mp4";
                case ".mov":
                    return "video/quicktime";
                case ".webm":
                    return "video/webm";
                case ".mkv":
                    return "video/x-matroska";
                case ".avi":
                    return "video/x-msvideo";
                default:
                    return null;
            }
        }

        #endregion
    }
}
