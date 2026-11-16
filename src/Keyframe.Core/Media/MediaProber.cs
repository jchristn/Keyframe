namespace Keyframe.Core.Media
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Keyframe.Core.Enums;
    using Keyframe.Core.Exceptions;
    using Keyframe.Core.Responses;

    /// <summary>
    /// Reads media properties with ffprobe.
    /// Thread safety: safe for concurrent use.
    /// </summary>
    public class MediaProber
    {
        #region Public-Members

        /// <summary>
        /// Maximum tags copied into media properties. Default 64. Minimum 0, maximum 1024.
        /// </summary>
        public int MaxTags
        {
            get
            {
                return _MaxTags;
            }
            set
            {
                _MaxTags = Math.Clamp(value, 0, 1024);
            }
        }

        /// <summary>
        /// Maximum characters per tag value. Default 512. Minimum 16, maximum 65536.
        /// </summary>
        public int MaxTagValueLength
        {
            get
            {
                return _MaxTagValueLength;
            }
            set
            {
                _MaxTagValueLength = Math.Clamp(value, 16, 65536);
            }
        }

        #endregion

        #region Private-Members

        private readonly FfmpegRunner _Runner;
        private int _MaxTags = 64;
        private int _MaxTagValueLength = 512;

        private static readonly string[] _ImageFormats = new[]
        {
            "image2", "png_pipe", "jpeg_pipe", "webp_pipe", "gif", "bmp_pipe", "tiff_pipe", "heif", "avif", "jpegxl_pipe", "image2pipe"
        };

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the prober.
        /// </summary>
        /// <param name="runner">ffmpeg runner.</param>
        /// <exception cref="ArgumentNullException">Thrown when runner is null.</exception>
        public MediaProber(FfmpegRunner runner)
        {
            ArgumentNullException.ThrowIfNull(runner);
            _Runner = runner;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Probe a media file.
        /// </summary>
        /// <param name="path">File path.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Media properties. SizeBytes, Sha256, and ContentType are left for the caller to fill.</returns>
        /// <exception cref="ArgumentNullException">Thrown when path is null or empty.</exception>
        /// <exception cref="MediaProcessingException">Thrown when ffprobe fails or its output cannot be parsed.</exception>
        public async Task<MediaProperties> ProbeAsync(string path, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(path)) throw new ArgumentNullException(nameof(path));

            FfmpegResult result = await _Runner.RunFfprobeCheckedAsync(
                "probe",
                new[] { "-v", "error", "-print_format", "json", "-show_format", "-show_streams", path },
                token).ConfigureAwait(false);

            FfprobeOutput? output;
            try
            {
                output = JsonSerializer.Deserialize<FfprobeOutput>(result.StandardOutput);
            }
            catch (JsonException ex)
            {
                throw new MediaProcessingException("Unable to parse ffprobe output: " + ex.Message, ex);
            }

            if (output == null) throw new MediaProcessingException("ffprobe returned no output.");
            return ToProperties(output);
        }

        /// <summary>
        /// Convert ffprobe output into media properties.
        /// </summary>
        /// <param name="output">ffprobe output.</param>
        /// <returns>Media properties.</returns>
        /// <exception cref="ArgumentNullException">Thrown when output is null.</exception>
        public MediaProperties ToProperties(FfprobeOutput output)
        {
            ArgumentNullException.ThrowIfNull(output);

            List<FfprobeStream> streams = output.Streams ?? new List<FfprobeStream>();
            FfprobeStream? video = streams.FirstOrDefault(s => s.CodecType == "video" && !s.IsAttachedPicture);
            bool coverArtOnly = video == null && streams.Any(s => s.CodecType == "video" && s.IsAttachedPicture);
            FfprobeStream? audio = streams.FirstOrDefault(s => s.CodecType == "audio");

            MediaProperties props = new MediaProperties
            {
                Format = output.Format?.FormatName,
                BitRate = ParseLong(output.Format?.BitRate),
                Width = video?.Width,
                Height = video?.Height,
                VideoCodec = video?.CodecName,
                HasAudio = audio != null,
                AudioCodec = audio?.CodecName,
                AudioChannels = audio?.Channels,
                AudioSampleRate = ParseInt(audio?.SampleRate)
            };

            double? seconds = ParseDouble(output.Format?.Duration) ?? ParseDouble(video?.Duration) ?? ParseDouble(audio?.Duration);
            props.MediaKind = InferKind(output.Format?.FormatName, video, audio);
            if (coverArtOnly && audio == null) props.MediaKind = MediaKindEnum.Image;

            if (props.MediaKind != MediaKindEnum.Image)
            {
                if (seconds.HasValue) props.DurationMs = (long)Math.Round(seconds.Value * 1000.0);
                if (video != null) props.FrameRate = ParseRate(video.AverageFrameRate) ?? ParseRate(video.RealFrameRate);
            }

            AddTags(props.Tags, output.Format?.Tags);
            if (video != null) AddTags(props.Tags, video.Tags);
            if (audio != null) AddTags(props.Tags, audio.Tags);
            return props;
        }

        #endregion

        #region Private-Methods

        private static MediaKindEnum InferKind(string? formatName, FfprobeStream? video, FfprobeStream? audio)
        {
            if (video != null)
            {
                string fmt = formatName ?? string.Empty;
                bool imageFormat = fmt.Split(',').Any(f => _ImageFormats.Contains(f.Trim()));
                if (imageFormat) return MediaKindEnum.Image;
                return MediaKindEnum.Video;
            }

            if (audio != null) return MediaKindEnum.Audio;
            return MediaKindEnum.Unknown;
        }

        private void AddTags(Dictionary<string, string> target, Dictionary<string, string>? source)
        {
            if (source == null) return;
            foreach (KeyValuePair<string, string> kvp in source)
            {
                if (target.Count >= _MaxTags) return;
                if (String.IsNullOrWhiteSpace(kvp.Key) || target.ContainsKey(kvp.Key)) continue;
                string value = kvp.Value ?? string.Empty;
                if (value.Length > _MaxTagValueLength) value = value.Substring(0, _MaxTagValueLength);
                target[kvp.Key] = value;
            }
        }

        private static double? ParseDouble(string? value)
        {
            if (String.IsNullOrWhiteSpace(value) || value == "N/A") return null;
            if (Double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) && !Double.IsNaN(d)) return d;
            return null;
        }

        private static long? ParseLong(string? value)
        {
            if (String.IsNullOrWhiteSpace(value) || value == "N/A") return null;
            if (Int64.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long l)) return l;
            return null;
        }

        private static int? ParseInt(string? value)
        {
            if (String.IsNullOrWhiteSpace(value) || value == "N/A") return null;
            if (Int32.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i)) return i;
            return null;
        }

        private static double? ParseRate(string? fraction)
        {
            if (String.IsNullOrWhiteSpace(fraction)) return null;
            string[] parts = fraction.Split('/');
            if (parts.Length == 2
                && Double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double n)
                && Double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double d)
                && d > 0 && n > 0)
            {
                return Math.Round(n / d, 3);
            }

            return ParseDouble(fraction);
        }

        #endregion
    }
}
