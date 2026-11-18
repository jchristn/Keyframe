namespace Keyframe.Sdk.Requests
{
    using System;

    /// <summary>
    /// Section detection options: where silences split an audio recording, and how sections are merged and split.
    /// </summary>
    public class SectionDetectionSpec
    {
        #region Public-Members

        /// <summary>
        /// Level in dBFS below which audio counts as silence. Default -40. Minimum -90, maximum -10.
        /// </summary>
        public double SilenceDb
        {
            get
            {
                return _SilenceDb;
            }
            set
            {
                _SilenceDb = Math.Clamp(value, -90.0, -10.0);
            }
        }

        /// <summary>
        /// Shortest silence in milliseconds that can separate sections. Default 1500. Minimum 100, maximum 30000.
        /// </summary>
        public long MinSilenceMs
        {
            get
            {
                return _MinSilenceMs;
            }
            set
            {
                _MinSilenceMs = Math.Clamp(value, 100L, 30000L);
            }
        }

        /// <summary>
        /// Sections shorter than this merge into a neighbor, in milliseconds. Default 20000. Minimum 0, maximum 600000.
        /// </summary>
        public long MinSectionMs
        {
            get
            {
                return _MinSectionMs;
            }
            set
            {
                _MinSectionMs = Math.Clamp(value, 0L, 600000L);
            }
        }

        /// <summary>
        /// Sections longer than this split evenly, in milliseconds. Default 600000. Minimum 10000, maximum 3600000.
        /// </summary>
        public long MaxSectionMs
        {
            get
            {
                return _MaxSectionMs;
            }
            set
            {
                _MaxSectionMs = Math.Clamp(value, 10000L, 3600000L);
            }
        }

        /// <summary>
        /// Most sections; the shortest adjacent pairs merge beyond this. Default 100. Minimum 1, maximum 1000.
        /// </summary>
        public int MaxSections
        {
            get
            {
                return _MaxSections;
            }
            set
            {
                _MaxSections = Math.Clamp(value, 1, 1000);
            }
        }

        #endregion

        #region Private-Members

        private double _SilenceDb = -40.0;
        private long _MinSilenceMs = 1500;
        private long _MinSectionMs = 20000;
        private long _MaxSectionMs = 600000;
        private int _MaxSections = 100;

        #endregion
    }
}
