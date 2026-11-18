namespace Keyframe.Core.Media
{
    using System;
    using Keyframe.Core.Enums;
    using Keyframe.Core.Requests;
    using Keyframe.Core.Settings;

    /// <summary>
    /// Resolves the effective listening rendition: request values over the Media.Listen* defaults.
    /// </summary>
    public static class RenditionResolver
    {
        /// <summary>
        /// Resolve the effective rendition. Every member of the result is filled.
        /// </summary>
        /// <param name="requested">Requested rendition, or null to use every default.</param>
        /// <param name="media">Media settings holding the defaults.</param>
        /// <returns>Effective rendition.</returns>
        /// <exception cref="ArgumentNullException">Thrown when media is null.</exception>
        public static AudioRenditionSpec Resolve(AudioRenditionSpec? requested, MediaSettings media)
        {
            ArgumentNullException.ThrowIfNull(media);
            return new AudioRenditionSpec
            {
                Format = requested?.Format ?? media.ListenFormat,
                SampleRate = requested?.SampleRate ?? media.ListenSampleRate,
                Channels = requested?.Channels ?? media.ListenChannels,
                BitrateKbps = requested?.BitrateKbps ?? media.ListenBitrateKbps
            };
        }

        /// <summary>
        /// MIME type sent with a clip of the given format.
        /// </summary>
        /// <param name="format">Format.</param>
        /// <returns>MIME type.</returns>
        public static string MediaType(ListenFormatEnum format)
        {
            switch (format)
            {
                case ListenFormatEnum.Wav:
                    return "audio/wav";
                case ListenFormatEnum.Flac:
                    return "audio/flac";
                default:
                    return "audio/mpeg";
            }
        }

        /// <summary>
        /// File extension for a clip of the given format, without the dot.
        /// </summary>
        /// <param name="format">Format.</param>
        /// <returns>Extension.</returns>
        public static string Extension(ListenFormatEnum format)
        {
            switch (format)
            {
                case ListenFormatEnum.Wav:
                    return "wav";
                case ListenFormatEnum.Flac:
                    return "flac";
                default:
                    return "mp3";
            }
        }

        /// <summary>
        /// Planning estimate of a clip's encoded size in bytes, before base64. Mp3 uses the bitrate; Wav and Flac use
        /// 16-bit PCM at the sample rate and channel count (Flac is usually smaller, so the estimate is conservative).
        /// </summary>
        /// <param name="rendition">Effective rendition.</param>
        /// <param name="durationMs">Clip length in milliseconds.</param>
        /// <returns>Estimated bytes.</returns>
        /// <exception cref="ArgumentNullException">Thrown when rendition is null.</exception>
        public static long EstimateBytes(AudioRenditionSpec rendition, long durationMs)
        {
            ArgumentNullException.ThrowIfNull(rendition);
            double seconds = Math.Max(0, durationMs) / 1000.0;
            ListenFormatEnum format = rendition.Format ?? ListenFormatEnum.Mp3;
            if (format == ListenFormatEnum.Mp3) return (long)Math.Ceiling((rendition.BitrateKbps ?? 64) * 1000.0 / 8.0 * seconds) + 1024;
            return (long)Math.Ceiling((rendition.SampleRate ?? 16000) * (rendition.Channels ?? 1) * 2.0 * seconds) + 1024;
        }

        /// <summary>
        /// Wire size of inline media as PolyPrompt counts it: base64 of the bytes plus a data URL prefix allowance.
        /// </summary>
        /// <param name="bytes">Raw bytes.</param>
        /// <returns>Wire bytes.</returns>
        public static long WireBytes(long bytes)
        {
            long safe = Math.Max(0, bytes);
            return (safe + 2) / 3 * 4 + 64;
        }
    }
}
