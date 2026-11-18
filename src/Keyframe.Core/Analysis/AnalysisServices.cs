namespace Keyframe.Core.Analysis
{
    using System;
    using Keyframe.Core.Inference;
    using Keyframe.Core.Inference.Providers;
    using Keyframe.Core.Media;
    using Keyframe.Core.Observability;
    using Keyframe.Core.Settings;

    /// <summary>
    /// The shared components an analysis needs: media tools, the inference client factory, and the function runner.
    /// ffmpeg paths are read once at construction; everything else reads current settings on each call.
    /// Thread safety: safe for concurrent use.
    /// </summary>
    public class AnalysisServices : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// Accessor returning current settings.
        /// </summary>
        public Func<KeyframeSettings> Settings { get; }

        /// <summary>
        /// ffmpeg runner.
        /// </summary>
        public FfmpegRunner Ffmpeg { get; }

        /// <summary>
        /// Media prober.
        /// </summary>
        public MediaProber Prober { get; }

        /// <summary>
        /// Image normalizer.
        /// </summary>
        public ImageNormalizer Normalizer { get; }

        /// <summary>
        /// Scene detector.
        /// </summary>
        public SceneDetector Scenes { get; }

        /// <summary>
        /// Frame extractor.
        /// </summary>
        public FrameExtractor Frames { get; }

        /// <summary>
        /// Audio extractor.
        /// </summary>
        public AudioExtractor Audio { get; }

        /// <summary>
        /// Renders the audio clips sent to listening models.
        /// </summary>
        public ListenRenditionExtractor Listen { get; }

        /// <summary>
        /// Audio measurer (loudness, levels, silence, stereo width).
        /// </summary>
        public AudioMeasurer Measurer { get; }

        /// <summary>
        /// Audio section detector (silence-based).
        /// </summary>
        public SectionDetector Sections { get; }

        /// <summary>
        /// Media resolver.
        /// </summary>
        public MediaResolver Resolver { get; }

        /// <summary>
        /// Segment cutter for POST /v1.0/segment.
        /// </summary>
        public SegmentExtractor Segments { get; }

        /// <summary>
        /// Inference client factory.
        /// </summary>
        public InferenceClientFactory Clients { get; }

        /// <summary>
        /// Model invoker.
        /// </summary>
        public ModelInvoker Invoker { get; }

        /// <summary>
        /// Summarize and extract runner.
        /// </summary>
        public MediaFunctions Functions { get; }

        /// <summary>
        /// Audio extraction and chunked transcription runner, shared by video and audio analysis.
        /// </summary>
        public TranscriptionRunner Transcription { get; }

        /// <summary>
        /// Invoked once for every analysis that started, after its response is complete: on success, on failure
        /// (including thrown errors), and on cancellation. The host uses it to persist analytics. Must not block;
        /// exceptions it throws are swallowed. Null disables the notification.
        /// </summary>
        public Action<AnalysisCompletion>? Completed { get; set; } = null;

        /// <summary>
        /// Endpoint health, used to fall back when the listening endpoint is unhealthy. Set by the host; null treats
        /// every endpoint as healthy. Default null.
        /// </summary>
        public IEndpointHealthLookup? Health
        {
            get
            {
                return _Health;
            }
            set
            {
                _Health = value;
                Invoker.Health = value;
            }
        }

        #endregion

        #region Private-Members

        private bool _Disposed = false;
        private IEndpointHealthLookup? _Health = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="settings">Accessor returning current settings.</param>
        /// <param name="clients">Inference client factory. Null creates one that serves the mock provider and every provider the PolyPrompt integration supports.</param>
        /// <exception cref="ArgumentNullException">Thrown when settings is null.</exception>
        public AnalysisServices(Func<KeyframeSettings> settings, InferenceClientFactory? clients = null)
        {
            ArgumentNullException.ThrowIfNull(settings);
            Settings = settings;

            KeyframeSettings current = settings();
            Ffmpeg = new FfmpegRunner(current.Media.FfmpegPath, current.Media.FfprobePath, current.Media.FfmpegTimeoutMs);
            Prober = new MediaProber(Ffmpeg);
            Normalizer = new ImageNormalizer(Ffmpeg);
            Scenes = new SceneDetector(Ffmpeg);
            Frames = new FrameExtractor(Ffmpeg);
            Audio = new AudioExtractor(Ffmpeg);
            Listen = new ListenRenditionExtractor(Ffmpeg);
            Measurer = new AudioMeasurer(Ffmpeg);
            Sections = new SectionDetector(Ffmpeg);
            Resolver = new MediaResolver(current.Media);
            Segments = new SegmentExtractor(Ffmpeg, Resolver, Prober);
            if (clients == null)
            {
                clients = new InferenceClientFactory(() => Settings().Inference);
                PolyPromptClientBuilder.Register(clients);
            }

            Clients = clients;
            Invoker = new ModelInvoker(() => Settings().Inference);
            Functions = new MediaFunctions(Invoker);
            Transcription = new TranscriptionRunner(Audio);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Record realtime-factor metrics for a finished analysis and hand it to <see cref="Completed"/>. Never throws.
        /// </summary>
        /// <param name="completion">Completion.</param>
        public void NotifyCompleted(AnalysisCompletion completion)
        {
            if (completion == null) return;
            try
            {
                if (completion.MediaDurationMs.HasValue && completion.MediaDurationMs.Value > 0 && !completion.Canceled && completion.Exception == null)
                {
                    double factor = completion.Response.Telemetry.TotalMs / completion.MediaDurationMs.Value;
                    KeyframeMetrics.RecordRealtimeFactor(completion.MediaKind.ToString().ToLowerInvariant(), factor);
                }

                Action<AnalysisCompletion>? callback = Completed;
                if (callback != null) callback(completion);
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// Dispose owned resources.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion

        #region Private-Methods

        /// <summary>
        /// Dispose pattern implementation.
        /// </summary>
        /// <param name="disposing">True when called from Dispose.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (_Disposed) return;
            _Disposed = true;
            if (disposing) Resolver.Dispose();
        }

        #endregion
    }
}
