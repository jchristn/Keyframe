namespace Keyframe.Core.Media
{
    /// <summary>
    /// Outcome of an ffmpeg or ffprobe invocation.
    /// </summary>
    public class FfmpegResult
    {
        /// <summary>
        /// Process exit code.
        /// </summary>
        public int ExitCode { get; set; } = -1;

        /// <summary>
        /// Captured standard output.
        /// </summary>
        public string StandardOutput { get; set; } = string.Empty;

        /// <summary>
        /// Captured standard error. ffmpeg writes its diagnostics and filter output here.
        /// </summary>
        public string StandardError { get; set; } = string.Empty;

        /// <summary>
        /// Wall-clock duration in milliseconds.
        /// </summary>
        public double DurationMs { get; set; } = 0;
    }
}
