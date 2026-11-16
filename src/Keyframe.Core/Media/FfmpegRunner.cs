namespace Keyframe.Core.Media
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Diagnostics;
    using System.Text;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using Keyframe.Core.Exceptions;
    using Keyframe.Core.Observability;

    /// <summary>
    /// Runs ffmpeg and ffprobe as child processes with timeouts and cancellation.
    /// Thread safety: safe for concurrent use; each call starts its own process.
    /// </summary>
    public class FfmpegRunner
    {
        #region Public-Members

        /// <summary>
        /// Path or command name for ffmpeg.
        /// </summary>
        public string FfmpegPath
        {
            get
            {
                return _FfmpegPath;
            }
            set
            {
                if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(FfmpegPath));
                _FfmpegPath = value;
            }
        }

        /// <summary>
        /// Path or command name for ffprobe.
        /// </summary>
        public string FfprobePath
        {
            get
            {
                return _FfprobePath;
            }
            set
            {
                if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(FfprobePath));
                _FfprobePath = value;
            }
        }

        /// <summary>
        /// Timeout per invocation in milliseconds. Default 600000. Minimum 1000, maximum 14400000.
        /// </summary>
        public int TimeoutMs
        {
            get
            {
                return _TimeoutMs;
            }
            set
            {
                _TimeoutMs = Math.Clamp(value, 1000, 14400000);
            }
        }

        /// <summary>
        /// Maximum characters of standard error retained per invocation. Default 4194304. Minimum 4096, maximum 67108864.
        /// Scene detection writes one line per detected cut, so long videos need a generous limit.
        /// </summary>
        public int MaxStandardErrorCharacters
        {
            get
            {
                return _MaxStandardErrorCharacters;
            }
            set
            {
                _MaxStandardErrorCharacters = Math.Clamp(value, 4096, 67108864);
            }
        }

        #endregion

        #region Private-Members

        private string _FfmpegPath = "ffmpeg";
        private string _FfprobePath = "ffprobe";
        private int _TimeoutMs = 600000;
        private int _MaxStandardErrorCharacters = 4194304;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the runner with default paths.
        /// </summary>
        public FfmpegRunner()
        {
        }

        /// <summary>
        /// Instantiate the runner.
        /// </summary>
        /// <param name="ffmpegPath">ffmpeg path or command name.</param>
        /// <param name="ffprobePath">ffprobe path or command name.</param>
        /// <param name="timeoutMs">Timeout per invocation in milliseconds.</param>
        public FfmpegRunner(string ffmpegPath, string ffprobePath, int timeoutMs)
        {
            FfmpegPath = ffmpegPath;
            FfprobePath = ffprobePath;
            TimeoutMs = timeoutMs;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Run ffmpeg.
        /// </summary>
        /// <param name="operation">Low-cardinality operation name for telemetry, for example "normalize".</param>
        /// <param name="arguments">Arguments, one per element. Do not quote.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result. The exit code is not checked; use <see cref="RunFfmpegCheckedAsync"/> to require success.</returns>
        /// <exception cref="MediaProcessingException">Thrown when ffmpeg cannot be started or times out.</exception>
        /// <exception cref="OperationCanceledException">Thrown when canceled.</exception>
        public Task<FfmpegResult> RunFfmpegAsync(string operation, IEnumerable<string> arguments, CancellationToken token = default)
        {
            return RunAsync(_FfmpegPath, operation, arguments, token);
        }

        /// <summary>
        /// Run ffmpeg and require a zero exit code.
        /// </summary>
        /// <param name="operation">Low-cardinality operation name for telemetry.</param>
        /// <param name="arguments">Arguments, one per element.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        /// <exception cref="MediaProcessingException">Thrown when ffmpeg fails, cannot be started, or times out.</exception>
        /// <exception cref="OperationCanceledException">Thrown when canceled.</exception>
        public async Task<FfmpegResult> RunFfmpegCheckedAsync(string operation, IEnumerable<string> arguments, CancellationToken token = default)
        {
            FfmpegResult result = await RunAsync(_FfmpegPath, operation, arguments, token).ConfigureAwait(false);
            if (result.ExitCode != 0)
                throw new MediaProcessingException("ffmpeg " + operation + " failed with exit code " + result.ExitCode + ": " + LastLines(result.StandardError, 5));
            return result;
        }

        /// <summary>
        /// Run ffprobe and require a zero exit code.
        /// </summary>
        /// <param name="operation">Low-cardinality operation name for telemetry.</param>
        /// <param name="arguments">Arguments, one per element.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        /// <exception cref="MediaProcessingException">Thrown when ffprobe fails, cannot be started, or times out.</exception>
        /// <exception cref="OperationCanceledException">Thrown when canceled.</exception>
        public async Task<FfmpegResult> RunFfprobeCheckedAsync(string operation, IEnumerable<string> arguments, CancellationToken token = default)
        {
            FfmpegResult result = await RunAsync(_FfprobePath, operation, arguments, token).ConfigureAwait(false);
            if (result.ExitCode != 0)
                throw new MediaProcessingException("ffprobe " + operation + " failed with exit code " + result.ExitCode + ": " + LastLines(result.StandardError, 5));
            return result;
        }

        /// <summary>
        /// Return the ffmpeg version line, or null when ffmpeg is not available.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Version line or null.</returns>
        public async Task<string?> GetFfmpegVersionAsync(CancellationToken token = default)
        {
            try
            {
                FfmpegResult result = await RunAsync(_FfmpegPath, "version", new[] { "-hide_banner", "-version" }, token).ConfigureAwait(false);
                if (result.ExitCode != 0) return null;
                string[] lines = result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                return lines.Length > 0 ? lines[0].Trim() : null;
            }
            catch (MediaProcessingException)
            {
                return null;
            }
        }

        #endregion

        #region Private-Methods

        private async Task<FfmpegResult> RunAsync(string executable, string operation, IEnumerable<string> arguments, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(arguments);
            token.ThrowIfCancellationRequested();

            ProcessStartInfo psi = new ProcessStartInfo(executable)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = false,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            foreach (string arg in arguments) psi.ArgumentList.Add(arg);

            Stopwatch sw = Stopwatch.StartNew();
            StringBuilder stdout = new StringBuilder();
            StringBuilder stderr = new StringBuilder();
            int maxStderr = _MaxStandardErrorCharacters;
            string outcome = "success";

            using (Activity? activity = KeyframeTelemetry.Source.StartActivity("ffmpeg " + operation, ActivityKind.Internal))
            using (Process process = new Process { StartInfo = psi })
            {
                process.OutputDataReceived += (s, e) =>
                {
                    if (e.Data == null) return;
                    lock (stdout) { stdout.Append(e.Data).Append('\n'); }
                };
                process.ErrorDataReceived += (s, e) =>
                {
                    if (e.Data == null) return;
                    lock (stderr)
                    {
                        if (stderr.Length < maxStderr) stderr.Append(e.Data).Append('\n');
                    }
                };

                try
                {
                    if (!process.Start())
                        throw new MediaProcessingException("Unable to start '" + executable + "'.");
                }
                catch (Win32Exception ex)
                {
                    KeyframeMetrics.RecordFfmpeg(operation, "unavailable", sw.Elapsed.TotalSeconds);
                    activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                    throw new MediaProcessingException("Unable to start '" + executable + "'. Is it installed and on the PATH? " + ex.Message, ex);
                }

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                using (CancellationTokenSource timeout = new CancellationTokenSource(_TimeoutMs))
                using (CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(token, timeout.Token))
                {
                    try
                    {
                        await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        TryKill(process);
                        outcome = token.IsCancellationRequested ? "canceled" : "timeout";
                        KeyframeMetrics.RecordFfmpeg(operation, outcome, sw.Elapsed.TotalSeconds);
                        activity?.SetStatus(ActivityStatusCode.Error, outcome);
                        if (token.IsCancellationRequested) throw;
                        throw new MediaProcessingException("'" + executable + "' " + operation + " timed out after " + _TimeoutMs + "ms.");
                    }
                }

                // Ensure asynchronous output handlers have drained.
                process.WaitForExit();
                sw.Stop();

                FfmpegResult result = new FfmpegResult
                {
                    ExitCode = process.ExitCode,
                    DurationMs = sw.Elapsed.TotalMilliseconds
                };

                lock (stdout) { result.StandardOutput = stdout.ToString(); }
                lock (stderr) { result.StandardError = stderr.ToString(); }

                if (result.ExitCode != 0)
                {
                    outcome = "failure";
                    activity?.SetStatus(ActivityStatusCode.Error, "exit code " + result.ExitCode);
                }

                activity?.SetTag("keyframe.ffmpeg.operation", operation);
                activity?.SetTag("keyframe.ffmpeg.exit_code", result.ExitCode);
                KeyframeMetrics.RecordFfmpeg(operation, outcome, sw.Elapsed.TotalSeconds);
                return result;
            }
        }

        private static void TryKill(Process process)
        {
            try
            {
                if (!process.HasExited) process.Kill(true);
            }
            catch (InvalidOperationException)
            {
            }
            catch (Win32Exception)
            {
            }
        }

        private static readonly Regex _UnixPath = new Regex(@"(/[^\s/:'""|]+)+/([^\s/:'""|]+)", RegexOptions.Compiled);
        private static readonly Regex _WindowsPath = new Regex(@"([A-Za-z]:\\(?:[^\s\\:'""|]+\\)*)([^\s\\:'""|]+)", RegexOptions.Compiled);

        private static string LastLines(string text, int count)
        {
            if (String.IsNullOrEmpty(text)) return "(no output)";
            string[] lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            int start = Math.Max(0, lines.Length - count);
            string joined = String.Join(" | ", lines, start, lines.Length - start).Trim();

            // Error text reaches API callers, so keep file names but drop server directories (temp paths).
            joined = _UnixPath.Replace(joined, "$2");
            return _WindowsPath.Replace(joined, "$2");
        }

        #endregion
    }
}
