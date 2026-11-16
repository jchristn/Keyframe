namespace Keyframe.Core.Requests
{
    using System;

    /// <summary>
    /// Scene boundary detection options.
    /// </summary>
    public class SceneDetectionSpec
    {
        #region Public-Members

        /// <summary>
        /// ffmpeg scene-change score threshold. Lower values find more cuts. Default 0.30. Minimum 0.01, maximum 1.0.
        /// </summary>
        public double Threshold
        {
            get
            {
                return _Threshold;
            }
            set
            {
                _Threshold = Math.Clamp(value, 0.01, 1.0);
            }
        }

        /// <summary>
        /// Minimum scene length in milliseconds. Shorter scenes merge into a neighbor. Default 2000. Minimum 0, maximum 600000.
        /// </summary>
        public long MinSceneMs
        {
            get
            {
                return _MinSceneMs;
            }
            set
            {
                _MinSceneMs = Math.Clamp(value, 0L, 600000L);
            }
        }

        /// <summary>
        /// Maximum scene length in milliseconds. Longer scenes split evenly. Default 60000. Minimum 1000, maximum 3600000.
        /// </summary>
        public long MaxSceneMs
        {
            get
            {
                return _MaxSceneMs;
            }
            set
            {
                _MaxSceneMs = Math.Clamp(value, 1000L, 3600000L);
            }
        }

        /// <summary>
        /// Maximum scenes analyzed. Excess scenes are merged. Default 200. Minimum 1, maximum 2000.
        /// </summary>
        public int MaxScenes
        {
            get
            {
                return _MaxScenes;
            }
            set
            {
                _MaxScenes = Math.Clamp(value, 1, 2000);
            }
        }

        #endregion

        #region Private-Members

        private double _Threshold = 0.30;
        private long _MinSceneMs = 2000;
        private long _MaxSceneMs = 60000;
        private int _MaxScenes = 200;

        #endregion
    }
}
