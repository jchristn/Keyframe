namespace Keyframe.Core.Responses
{
    /// <summary>
    /// Deterministic measurements of an audio recording. Levels are in dB relative to full scale; a member is null when
    /// ffmpeg could not measure it (for example a level of digital silence).
    /// </summary>
    public class AudioMeasurements
    {
        /// <summary>
        /// Integrated loudness (EBU R128) in LUFS.
        /// </summary>
        public double? IntegratedLufs { get; set; } = null;

        /// <summary>
        /// Loudness range (EBU R128) in LU.
        /// </summary>
        public double? LoudnessRangeLu { get; set; } = null;

        /// <summary>
        /// True peak in dBTP.
        /// </summary>
        public double? TruePeakDbtp { get; set; } = null;

        /// <summary>
        /// Sample peak level in dBFS.
        /// </summary>
        public double? PeakDbfs { get; set; } = null;

        /// <summary>
        /// RMS level in dBFS.
        /// </summary>
        public double? RmsDbfs { get; set; } = null;

        /// <summary>
        /// Crest factor in dB (peak level minus RMS level).
        /// </summary>
        public double? CrestFactorDb { get; set; } = null;

        /// <summary>
        /// Number of samples at the peak level, across channels.
        /// </summary>
        public long? PeakCount { get; set; } = null;

        /// <summary>
        /// True when TruePeakDbtp is at or above Media.ClippingTruePeakDbtp and PeakCount per minute is at or above
        /// Media.ClippingMinPeaksPerMinute. The raw inputs are reported so callers can apply their own threshold.
        /// </summary>
        public bool ClippingLikely { get; set; } = false;

        /// <summary>
        /// Fraction of the recording that is silence (0 to 1), using Media.MeasureSilenceDb and Media.MeasureMinSilenceMs.
        /// </summary>
        public double SilenceRatio { get; set; } = 0;

        /// <summary>
        /// Number of silent stretches.
        /// </summary>
        public int SilenceCount { get; set; } = 0;

        /// <summary>
        /// Length of the silence at the start, in milliseconds.
        /// </summary>
        public long LeadingSilenceMs { get; set; } = 0;

        /// <summary>
        /// Length of the silence at the end, in milliseconds.
        /// </summary>
        public long TrailingSilenceMs { get; set; } = 0;

        /// <summary>
        /// Channel count of the source.
        /// </summary>
        public int Channels { get; set; } = 0;

        /// <summary>
        /// Side-to-mid RMS level difference in dB for stereo sources; -120 means the channels are identical. Null for
        /// sources that are not stereo.
        /// </summary>
        public double? StereoWidthDb { get; set; } = null;

        /// <summary>
        /// True for a mono source, or a stereo source whose StereoWidthDb is at or below Media.MonoMaxSideDb.
        /// </summary>
        public bool EffectivelyMono { get; set; } = false;
    }
}
