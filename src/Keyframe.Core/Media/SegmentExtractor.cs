namespace Keyframe.Core.Media
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Keyframe.Core.Enums;
    using Keyframe.Core.Exceptions;
    using Keyframe.Core.Requests;
    using Keyframe.Core.Responses;
    using Keyframe.Core.Settings;

    /// <summary>
    /// Cuts a time range out of a video or audio source with ffmpeg: a re-encoded video clip (accurate cuts), an
    /// audio clip, or one still frame. The source is resolved through <see cref="MediaResolver"/>, so URL downloads
    /// keep the same size limits and private-address guard as analysis.
    /// Thread safety: safe for concurrent use.
    /// </summary>
    public class SegmentExtractor
    {
        #region Private-Members

        private readonly FfmpegRunner _Runner;
        private readonly MediaResolver _Resolver;
        private readonly MediaProber _Prober;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="runner">ffmpeg runner.</param>
        /// <param name="resolver">Media resolver.</param>
        /// <param name="prober">Media prober.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public SegmentExtractor(FfmpegRunner runner, MediaResolver resolver, MediaProber prober)
        {
            _Runner = runner ?? throw new ArgumentNullException(nameof(runner));
            _Resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
            _Prober = prober ?? throw new ArgumentNullException(nameof(prober));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Validate the request, resolve and probe the source, and cut the segment.
        /// </summary>
        /// <param name="request">Request.</param>
        /// <param name="settings">Media settings (limits and temp directory).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Segment with its bytes loaded.</returns>
        /// <exception cref="ArgumentNullException">Thrown when settings is null.</exception>
        /// <exception cref="RequestValidationException">Thrown when the request is invalid.</exception>
        /// <exception cref="MediaResolutionException">Thrown when the source cannot be downloaded or decoded.</exception>
        /// <exception cref="UnsupportedMediaException">Thrown when the source lacks the stream the output needs, or StartMs is past the end.</exception>
        /// <exception cref="MediaProcessingException">Thrown when ffmpeg fails.</exception>
        /// <exception cref="OperationCanceledException">Thrown when canceled.</exception>
        public async Task<ExtractedSegment> CutAsync(SegmentRequest request, MediaSettings settings, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(settings);
            List<string> errors = RequestValidator.Validate(request, settings.MaxSegmentMs);
            if (errors.Count > 0) throw new RequestValidationException(errors);

            using (MediaWorkspace workspace = new MediaWorkspace(settings.TempDirectory))
            {
                ResolvedMedia resolved = await _Resolver.ResolveAsync(request.Source!, MediaKindEnum.Unknown, workspace, token).ConfigureAwait(false);
                if (resolved.MediaKind == MediaKindEnum.Image)
                    throw new UnsupportedMediaException("Segments need a video or audio source; received an image (" + resolved.ContentType + ").");

                MediaProperties properties = await _Prober.ProbeAsync(resolved.Path, token).ConfigureAwait(false);
                return await CutAsync(resolved.Path, properties, request, workspace, token).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Cut a segment from a probed local file.
        /// </summary>
        /// <param name="path">Source path.</param>
        /// <param name="properties">Probed properties of the source.</param>
        /// <param name="request">Validated request.</param>
        /// <param name="workspace">Workspace for the output file.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Segment with its bytes loaded.</returns>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        /// <exception cref="UnsupportedMediaException">Thrown when the source lacks the stream the output needs, or StartMs is past the end.</exception>
        /// <exception cref="MediaProcessingException">Thrown when ffmpeg fails or writes nothing.</exception>
        /// <exception cref="OperationCanceledException">Thrown when canceled.</exception>
        public async Task<ExtractedSegment> CutAsync(string path, MediaProperties properties, SegmentRequest request, MediaWorkspace workspace, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(path)) throw new ArgumentNullException(nameof(path));
            ArgumentNullException.ThrowIfNull(properties);
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(workspace);

            bool hasVideo = properties.MediaKind == MediaKindEnum.Video;
            if (request.Output == SegmentOutputEnum.Audio && !properties.HasAudio)
                throw new UnsupportedMediaException("Audio output needs a source with an audio track; this media has none.");
            if (request.Output != SegmentOutputEnum.Audio && !hasVideo)
                throw new UnsupportedMediaException(request.Output + " output needs a video source; this media is " + properties.MediaKind.ToString().ToLowerInvariant() + ".");

            long duration = properties.DurationMs ?? 0;
            if (duration < 1) throw new UnsupportedMediaException("The media duration could not be determined.");
            if (request.StartMs >= duration)
                throw new UnsupportedMediaException("StartMs " + request.StartMs + " is at or past the end of the media (" + duration + "ms).");

            string format = SegmentFormats.Normalize(request.Output, request.Format) ?? SegmentFormats.For(request.Output)[0];
            ExtractedSegment segment = new ExtractedSegment { MediaType = SegmentFormats.MediaType(format), StartMs = request.StartMs };

            List<string> args;
            if (request.Output == SegmentOutputEnum.Frame)
            {
                long at = Math.Min(request.StartMs, Math.Max(0, duration - 100));
                segment.StartMs = at;
                segment.EndMs = at;
                segment.Filename = "segment-" + at.ToString(CultureInfo.InvariantCulture) + "." + format;
                args = FrameArguments(path, at, format, request.MaxEdge);
            }
            else
            {
                long end = request.EndMs ?? duration;
                if (end > duration)
                {
                    end = duration;
                    segment.Clamped = true;
                }

                segment.EndMs = end;
                segment.DurationMs = end - request.StartMs;
                segment.Filename = "segment-" + request.StartMs.ToString(CultureInfo.InvariantCulture) + "-" + end.ToString(CultureInfo.InvariantCulture) + "." + format;
                args = request.Output == SegmentOutputEnum.Audio
                    ? AudioArguments(path, request.StartMs, segment.DurationMs, format)
                    : VideoArguments(path, request.StartMs, segment.DurationMs, format, request.MaxEdge, properties.HasAudio);
            }

            string output = workspace.PathFor("segment." + format);
            args.Add(output);
            await _Runner.RunFfmpegCheckedAsync("segment_" + request.Output.ToString().ToLowerInvariant(), args, token).ConfigureAwait(false);

            FileInfo info = new FileInfo(output);
            if (!info.Exists || info.Length < 1) throw new MediaProcessingException("ffmpeg produced no output for the segment.");
            segment.Path = output;
            segment.Data = await File.ReadAllBytesAsync(output, token).ConfigureAwait(false);
            segment.SizeBytes = segment.Data.Length;
            return segment;
        }

        #endregion

        #region Private-Methods

        private static List<string> Prefix(string path, long startMs)
        {
            return new List<string>
            {
                "-y", "-hide_banner", "-nostats", "-loglevel", "error",
                "-ss", Seconds(startMs),
                "-i", path
            };
        }

        private static List<string> FrameArguments(string path, long atMs, string format, int? maxEdge)
        {
            List<string> args = Prefix(path, atMs);
            args.AddRange(new[] { "-map", "0:v:0", "-an", "-sn", "-frames:v", "1" });
            if (maxEdge.HasValue) args.AddRange(new[] { "-vf", ScaleFilter(maxEdge.Value, false) });
            if (format == "png") args.AddRange(new[] { "-c:v", "png", "-f", "image2" });
            else args.AddRange(new[] { "-c:v", "mjpeg", "-q:v", "2", "-pix_fmt", "yuvj420p", "-f", "image2" });
            return args;
        }

        private static List<string> AudioArguments(string path, long startMs, long durationMs, string format)
        {
            List<string> args = Prefix(path, startMs);
            args.AddRange(new[] { "-t", Seconds(durationMs), "-map", "0:a:0", "-vn", "-sn", "-dn" });
            switch (format)
            {
                case "wav":
                    args.AddRange(new[] { "-c:a", "pcm_s16le", "-f", "wav" });
                    break;
                case "flac":
                    args.AddRange(new[] { "-c:a", "flac", "-f", "flac" });
                    break;
                case "m4a":
                    args.AddRange(new[] { "-c:a", "aac", "-b:a", "128k", "-movflags", "+faststart", "-f", "ipod" });
                    break;
                case "ogg":
                    args.AddRange(new[] { "-c:a", "libopus", "-b:a", "96k", "-f", "ogg" });
                    break;
                default:
                    args.AddRange(new[] { "-c:a", "libmp3lame", "-q:a", "4", "-f", "mp3" });
                    break;
            }

            return args;
        }

        private static List<string> VideoArguments(string path, long startMs, long durationMs, string format, int? maxEdge, bool hasAudio)
        {
            List<string> args = Prefix(path, startMs);
            args.AddRange(new[] { "-t", Seconds(durationMs), "-map", "0:v:0" });
            if (hasAudio) args.AddRange(new[] { "-map", "0:a:0" });
            else args.Add("-an");
            args.AddRange(new[] { "-sn", "-dn", "-vf", ScaleFilter(maxEdge ?? 16384, true) });

            if (format == "webm")
            {
                args.AddRange(new[] { "-c:v", "libvpx-vp9", "-b:v", "0", "-crf", "33", "-deadline", "realtime", "-cpu-used", "8", "-row-mt", "1" });
                if (hasAudio) args.AddRange(new[] { "-c:a", "libopus", "-b:a", "96k" });
                args.AddRange(new[] { "-f", "webm" });
            }
            else
            {
                args.AddRange(new[] { "-c:v", "libx264", "-preset", "veryfast", "-crf", "23", "-pix_fmt", "yuv420p" });
                if (hasAudio) args.AddRange(new[] { "-c:a", "aac", "-b:a", "128k" });
                args.AddRange(new[] { "-movflags", "+faststart", "-f", "mp4" });
            }

            return args;
        }

        private static string ScaleFilter(int maxEdge, bool even)
        {
            string e = Math.Clamp(maxEdge, 16, 16384).ToString(CultureInfo.InvariantCulture);
            return "scale='min(iw," + e + ")':'min(ih," + e + ")':force_original_aspect_ratio=decrease" + (even ? ":force_divisible_by=2" : string.Empty);
        }

        private static string Seconds(long ms)
        {
            return (ms / 1000.0).ToString("0.000", CultureInfo.InvariantCulture);
        }

        #endregion
    }
}
