namespace Keyframe.Core.Media
{
    using System;
    using System.Collections.Generic;
    using Keyframe.Core.Enums;

    /// <summary>
    /// Formats accepted for each segment output, with their media types.
    /// Thread safety: stateless and safe for concurrent use.
    /// </summary>
    public static class SegmentFormats
    {
        #region Public-Methods

        /// <summary>
        /// Formats accepted for an output, default first.
        /// </summary>
        /// <param name="output">Output.</param>
        /// <returns>Format names.</returns>
        public static IReadOnlyList<string> For(SegmentOutputEnum output)
        {
            switch (output)
            {
                case SegmentOutputEnum.Audio:
                    return new[] { "mp3", "wav", "flac", "m4a", "ogg" };
                case SegmentOutputEnum.Frame:
                    return new[] { "jpg", "png" };
                default:
                    return new[] { "mp4", "webm" };
            }
        }

        /// <summary>
        /// Normalize a requested format: null picks the default, matching is case-insensitive, and "jpeg" means "jpg".
        /// </summary>
        /// <param name="output">Output.</param>
        /// <param name="format">Requested format, or null.</param>
        /// <returns>Normalized format, or null when the format is not valid for the output.</returns>
        public static string? Normalize(SegmentOutputEnum output, string? format)
        {
            IReadOnlyList<string> allowed = For(output);
            if (String.IsNullOrWhiteSpace(format)) return allowed[0];
            string f = format.Trim().TrimStart('.').ToLowerInvariant();
            if (f == "jpeg") f = "jpg";
            foreach (string a in allowed)
            {
                if (a == f) return a;
            }

            return null;
        }

        /// <summary>
        /// Media type for a normalized format.
        /// </summary>
        /// <param name="format">Normalized format.</param>
        /// <returns>Media type.</returns>
        public static string MediaType(string format)
        {
            switch (format)
            {
                case "mp4": return "video/mp4";
                case "webm": return "video/webm";
                case "mp3": return "audio/mpeg";
                case "wav": return "audio/wav";
                case "flac": return "audio/flac";
                case "m4a": return "audio/mp4";
                case "ogg": return "audio/ogg";
                case "png": return "image/png";
                case "jpg": return "image/jpeg";
                default: return "application/octet-stream";
            }
        }

        #endregion
    }
}
