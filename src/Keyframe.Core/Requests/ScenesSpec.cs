namespace Keyframe.Core.Requests
{
    using System;

    /// <summary>
    /// Scene breakdown options for video.
    /// </summary>
    public class ScenesSpec
    {
        #region Public-Members

        /// <summary>
        /// Whether to break the video into scenes. Default false.
        /// </summary>
        public bool Enable { get; set; } = false;

        /// <summary>
        /// Detection options.
        /// </summary>
        public SceneDetectionSpec Detection
        {
            get
            {
                return _Detection;
            }
            set
            {
                _Detection = value ?? new SceneDetectionSpec();
            }
        }

        /// <summary>
        /// Keyframes extracted per scene. Default 3. Minimum 1, maximum 10.
        /// </summary>
        public int FramesPerScene
        {
            get
            {
                return _FramesPerScene;
            }
            set
            {
                _FramesPerScene = Math.Clamp(value, 1, 10);
            }
        }

        /// <summary>
        /// Per-scene summary options.
        /// </summary>
        public SummarizeSpec Summarize
        {
            get
            {
                return _Summarize;
            }
            set
            {
                _Summarize = value ?? new SummarizeSpec();
            }
        }

        /// <summary>
        /// Per-scene extraction options.
        /// </summary>
        public ExtractSpec Extract
        {
            get
            {
                return _Extract;
            }
            set
            {
                _Extract = value ?? new ExtractSpec();
            }
        }

        #endregion

        #region Private-Members

        private SceneDetectionSpec _Detection = new SceneDetectionSpec();
        private int _FramesPerScene = 3;
        private SummarizeSpec _Summarize = new SummarizeSpec();
        private ExtractSpec _Extract = new ExtractSpec();

        #endregion
    }
}
