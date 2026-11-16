namespace Keyframe.Core.Analysis
{
    using System;
    using Keyframe.Core.Inference;
    using Keyframe.Core.Inference.Providers;
    using Keyframe.Core.Media;
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
        /// Media resolver.
        /// </summary>
        public MediaResolver Resolver { get; }

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

        #endregion

        #region Private-Members

        private bool _Disposed = false;

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
            Resolver = new MediaResolver(current.Media);
            if (clients == null)
            {
                clients = new InferenceClientFactory(() => Settings().Inference);
                PolyPromptClientBuilder.Register(clients);
            }

            Clients = clients;
            Invoker = new ModelInvoker(() => Settings().Inference);
            Functions = new MediaFunctions(Invoker);
        }

        #endregion

        #region Public-Methods

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
