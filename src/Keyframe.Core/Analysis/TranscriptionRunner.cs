namespace Keyframe.Core.Analysis
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Keyframe.Core.Enums;
    using Keyframe.Core.Inference;
    using Keyframe.Core.Media;
    using Keyframe.Core.Observability;
    using Keyframe.Core.Requests;
    using Keyframe.Core.Responses;
    using Keyframe.Core.Settings;

    /// <summary>
    /// Extracts the audio track of a media file as fixed-length chunks and transcribes them in parallel, with
    /// retries for transient failures. Shared by video and audio analysis. Records one telemetry entry per chunk.
    /// Thread safety: safe for concurrent use.
    /// </summary>
    public class TranscriptionRunner
    {
        #region Private-Members

        private readonly AudioExtractor _Audio;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="audio">Audio extractor.</param>
        /// <exception cref="ArgumentNullException">Thrown when audio is null.</exception>
        public TranscriptionRunner(AudioExtractor audio)
        {
            ArgumentNullException.ThrowIfNull(audio);
            _Audio = audio;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Extract and transcribe the audio track. Chunk failures are recorded as analysis errors rather than thrown.
        /// </summary>
        /// <param name="client">Transcription client.</param>
        /// <param name="path">Media path. Must contain an audio stream.</param>
        /// <param name="durationMs">Media duration in milliseconds.</param>
        /// <param name="spec">Transcription options; Model and Language are sent to the endpoint.</param>
        /// <param name="workspace">Workspace for chunk files.</param>
        /// <param name="settings">Current settings.</param>
        /// <param name="context">Analysis context.</param>
        /// <param name="progressStart">Overall progress percent when extraction starts.</param>
        /// <param name="progressEnd">Overall progress percent when every chunk is transcribed.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Transcript with segments in time order.</returns>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
        /// <exception cref="Keyframe.Core.Exceptions.MediaProcessingException">Thrown when ffmpeg fails.</exception>
        /// <exception cref="OperationCanceledException">Thrown when canceled.</exception>
        public async Task<TranscriptResult> TranscribeAsync(
            ITranscriptionClient client,
            string path,
            long durationMs,
            TranscribeSpec spec,
            MediaWorkspace workspace,
            KeyframeSettings settings,
            AnalysisContext context,
            double progressStart,
            double progressEnd,
            CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(client);
            ArgumentNullException.ThrowIfNull(spec);
            ArgumentNullException.ThrowIfNull(workspace);
            ArgumentNullException.ThrowIfNull(settings);
            ArgumentNullException.ThrowIfNull(context);
            if (String.IsNullOrEmpty(path)) throw new ArgumentNullException(nameof(path));

            context.ReportProgress("AudioExtraction", 0, 1, progressStart);
            List<AudioChunk> chunks;
            using (StageTimer stage = context.BeginStage(AnalysisStageEnum.AudioExtraction))
            {
                chunks = await _Audio.ExtractAsync(path, workspace.CreateSubdirectory("audio"), settings.Media.TranscriptionChunkSeconds, token).ConfigureAwait(false);
            }

            TranscriptResult transcript = new TranscriptResult { Language = spec.Language };
            using (StageTimer stage = context.BeginStage(AnalysisStageEnum.Transcribe))
            {
                int completed = 0;
                double span = Math.Max(0, progressEnd - progressStart);
                List<Task<List<TranscriptSegmentResult>>> tasks = chunks.Select(chunk => Task.Run(async () =>
                {
                    long chunkDuration = Math.Min(settings.Media.TranscriptionChunkSeconds * 1000L, Math.Max(1, durationMs - chunk.OffsetMs));
                    List<TranscriptSegmentResult> segments = await TranscribeChunkAsync(client, chunk, chunkDuration, spec, transcript, settings, context, token).ConfigureAwait(false);
                    int done = Interlocked.Increment(ref completed);
                    context.ReportProgress("Transcribe", done, chunks.Count, progressStart + span * done / chunks.Count);
                    return segments;
                }, token)).ToList();

                List<TranscriptSegmentResult>[] results = await Task.WhenAll(tasks).ConfigureAwait(false);
                transcript.Segments = results.SelectMany(r => r).OrderBy(s => s.StartMs).ToList();
                transcript.Text = String.Join(" ", transcript.Segments.Select(s => s.Text.Trim()).Where(s => s.Length > 0));
                if (context.HasErrors()) stage.Fail("transcription errors");
            }

            return transcript;
        }

        #endregion

        #region Private-Methods

        private async Task<List<TranscriptSegmentResult>> TranscribeChunkAsync(
            ITranscriptionClient client,
            AudioChunk chunk,
            long chunkDurationMs,
            TranscribeSpec spec,
            TranscriptResult transcript,
            KeyframeSettings settings,
            AnalysisContext context,
            CancellationToken token)
        {
            byte[] audio = await File.ReadAllBytesAsync(chunk.Path, token).ConfigureAwait(false);
            ModelCallTelemetry telemetry = new ModelCallTelemetry
            {
                Purpose = ModelCallPurposeEnum.Transcribe,
                Target = "chunk:" + chunk.Index,
                Endpoint = client.EndpointName,
                Provider = client.Provider,
                Model = spec.Model ?? client.Model,
                StartUtc = DateTime.UtcNow,
                InputAudioBytes = audio.Length,
                Attempts = 0
            };

            Stopwatch sw = Stopwatch.StartNew();
            TranscriptionResponse? response = null;
            using (Activity? activity = KeyframeTelemetry.Source.StartActivity("inference Transcribe", ActivityKind.Client))
            {
                activity?.SetTag("keyframe.endpoint", client.EndpointName);
                activity?.SetTag("keyframe.provider", client.Provider.ToString());

                for (int attempt = 0; attempt <= settings.Inference.MaxRetries; attempt++)
                {
                    telemetry.Attempts++;
                    DateTime attemptStart = DateTime.UtcNow;
                    Stopwatch attemptWatch = Stopwatch.StartNew();
                    response = await client.TranscribeAsync(new TranscriptionRequest
                    {
                        Audio = audio,
                        MediaType = chunk.MediaType,
                        FileName = Path.GetFileName(chunk.Path),
                        DurationMs = chunkDurationMs,
                        Model = spec.Model,
                        Language = spec.Language,
                        Prompt = spec.Prompt
                    }, token).ConfigureAwait(false);
                    attemptWatch.Stop();

                    telemetry.StatusCode = response.StatusCode;
                    telemetry.QueueMs = Math.Round(telemetry.QueueMs + response.QueueMs, 3);
                    if (!String.IsNullOrEmpty(response.Model)) telemetry.Model = response.Model;
                    telemetry.AttemptDetails.Add(new ModelCallAttempt
                    {
                        Attempt = telemetry.Attempts,
                        StartUtc = attemptStart,
                        Success = response.Success,
                        Error = response.Success ? null : response.Error,
                        StatusCode = response.StatusCode,
                        DurationMs = Math.Round(attemptWatch.Elapsed.TotalMilliseconds, 3),
                        QueueMs = response.QueueMs
                    });
                    KeyframeMetrics.RecordModelTiming("transcribe", client.Provider.ToString().ToLowerInvariant(), response.QueueMs, null, null, null);

                    if (response.Success || !response.Retryable) break;

                    int delay = (int)Math.Min(60000, settings.Inference.RetryBackoffMs * Math.Pow(2, attempt));
                    if (attempt < settings.Inference.MaxRetries && delay > 0) await Task.Delay(delay, token).ConfigureAwait(false);
                }

                sw.Stop();
                telemetry.DurationMs = Math.Round(sw.Elapsed.TotalMilliseconds, 2);
                telemetry.Success = response != null && response.Success;
                telemetry.Error = telemetry.Success ? null : response?.Error ?? "Transcription failed.";
                if (!telemetry.Success) activity?.SetStatus(ActivityStatusCode.Error, telemetry.Error);
            }

            KeyframeMetrics.RecordModelCall("transcribe", client.Provider.ToString().ToLowerInvariant(), telemetry.Success ? "success" : "failure", sw.Elapsed.TotalSeconds, null, null);
            context.AddModelCall(telemetry);

            List<TranscriptSegmentResult> segments = new List<TranscriptSegmentResult>();
            if (response == null || !response.Success)
            {
                context.AddError("Transcribe chunk " + chunk.Index + ": " + telemetry.Error);
                return segments;
            }

            if (transcript.Language == null && response.Language != null)
            {
                lock (transcript) { if (transcript.Language == null) transcript.Language = response.Language; }
            }

            if (response.Segments.Count > 0)
            {
                foreach (TranscriptionSegment s in response.Segments)
                    segments.Add(new TranscriptSegmentResult { StartMs = chunk.OffsetMs + s.StartMs, EndMs = chunk.OffsetMs + s.EndMs, Text = s.Text });
            }
            else if (!String.IsNullOrWhiteSpace(response.Text))
            {
                segments.Add(new TranscriptSegmentResult { StartMs = chunk.OffsetMs, EndMs = chunk.OffsetMs + chunkDurationMs, Text = response.Text });
            }

            return segments;
        }

        #endregion
    }
}
