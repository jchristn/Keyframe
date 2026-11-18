namespace Keyframe.Core.Media
{
    using System;

    /// <summary>
    /// A time window in the source media, in milliseconds.
    /// </summary>
    public class ClipWindow
    {
        #region Public-Members

        /// <summary>
        /// Start in milliseconds. Minimum 0.
        /// </summary>
        public long StartMs
        {
            get
            {
                return _StartMs;
            }
            set
            {
                _StartMs = Math.Max(0, value);
            }
        }

        /// <summary>
        /// End in milliseconds. Never less than <see cref="StartMs"/> when read.
        /// </summary>
        public long EndMs
        {
            get
            {
                return Math.Max(_EndMs, _StartMs);
            }
            set
            {
                _EndMs = Math.Max(0, value);
            }
        }

        /// <summary>
        /// Length in milliseconds.
        /// </summary>
        public long DurationMs
        {
            get
            {
                return EndMs - StartMs;
            }
        }

        #endregion

        #region Private-Members

        private long _StartMs = 0;
        private long _EndMs = 0;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate an empty window.
        /// </summary>
        public ClipWindow()
        {
        }

        /// <summary>
        /// Instantiate a window.
        /// </summary>
        /// <param name="startMs">Start in milliseconds.</param>
        /// <param name="endMs">End in milliseconds.</param>
        public ClipWindow(long startMs, long endMs)
        {
            StartMs = startMs;
            EndMs = endMs;
        }

        #endregion
    }
}
