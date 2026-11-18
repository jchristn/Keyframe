namespace Keyframe.Sdk.Settings
{
    using System;
    using Keyframe.Sdk.Enums;

    /// <summary>
    /// Media handling settings. Paths require a restart; limits apply live.
    /// </summary>
    public class MediaSettings
    {
        #region Public-Members

        /// <summary>
        /// Root directory for per-request working directories. Default "temp".
        /// </summary>
        public string TempDirectory
        {
            get
            {
                return _TempDirectory;
            }
            set
            {
                if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(TempDirectory));
                _TempDirectory = value;
            }
        }

        /// <summary>
        /// Path or command name for ffmpeg. Default "ffmpeg".
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
        /// Path or command name for ffprobe. Default "ffprobe".
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
        /// Maximum bytes downloaded from a media URL. Default 2147483648 (2 GB). Minimum 1024, maximum 107374182400 (100 GB).
        /// </summary>
        public long MaxDownloadBytes
        {
            get
            {
                return _MaxDownloadBytes;
            }
            set
            {
                _MaxDownloadBytes = Math.Clamp(value, 1024L, 107374182400L);
            }
        }

        /// <summary>
        /// Download timeout in milliseconds. Default 300000. Minimum 1000, maximum 7200000.
        /// </summary>
        public int DownloadTimeoutMs
        {
            get
            {
                return _DownloadTimeoutMs;
            }
            set
            {
                _DownloadTimeoutMs = Math.Clamp(value, 1000, 7200000);
            }
        }

        /// <summary>
        /// Whether media URLs may point at loopback, link-local, or private network addresses. Default false (SSRF protection).
        /// </summary>
        public bool AllowPrivateNetworkUrls { get; set; } = false;

        /// <summary>
        /// Longest edge in pixels of images sent to a model. Default 1568. Minimum 64, maximum 8192.
        /// </summary>
        public int ImageMaxEdge
        {
            get
            {
                return _ImageMaxEdge;
            }
            set
            {
                _ImageMaxEdge = Math.Clamp(value, 64, 8192);
            }
        }

        /// <summary>
        /// Longest edge in pixels of keyframe thumbnails returned inline. Default 320. Minimum 32, maximum 2048.
        /// </summary>
        public int ThumbnailMaxEdge
        {
            get
            {
                return _ThumbnailMaxEdge;
            }
            set
            {
                _ThumbnailMaxEdge = Math.Clamp(value, 32, 2048);
            }
        }

        /// <summary>
        /// JPEG quality scale for ffmpeg (-q:v). Lower is better quality. Default 3. Minimum 1, maximum 31.
        /// </summary>
        public int JpegQuality
        {
            get
            {
                return _JpegQuality;
            }
            set
            {
                _JpegQuality = Math.Clamp(value, 1, 31);
            }
        }

        /// <summary>
        /// Maximum ffmpeg processes run in parallel for one request or job. Default 4. Minimum 1, maximum 64.
        /// </summary>
        public int MaxParallelFfmpeg
        {
            get
            {
                return _MaxParallelFfmpeg;
            }
            set
            {
                _MaxParallelFfmpeg = Math.Clamp(value, 1, 64);
            }
        }

        /// <summary>
        /// Timeout in milliseconds for a single ffmpeg or ffprobe invocation. Default 600000. Minimum 1000, maximum 14400000.
        /// </summary>
        public int FfmpegTimeoutMs
        {
            get
            {
                return _FfmpegTimeoutMs;
            }
            set
            {
                _FfmpegTimeoutMs = Math.Clamp(value, 1000, 14400000);
            }
        }

        /// <summary>
        /// Maximum accepted video duration in milliseconds. Default 14400000 (4 hours). Minimum 1000, maximum 86400000.
        /// </summary>
        public long MaxVideoDurationMs
        {
            get
            {
                return _MaxVideoDurationMs;
            }
            set
            {
                _MaxVideoDurationMs = Math.Clamp(value, 1000L, 86400000L);
            }
        }

        /// <summary>
        /// Maximum length of a segment cut by POST /v1.0/segment in milliseconds. Default 600000 (10 minutes).
        /// Minimum 1000, maximum 14400000.
        /// </summary>
        public long MaxSegmentMs
        {
            get
            {
                return _MaxSegmentMs;
            }
            set
            {
                _MaxSegmentMs = Math.Clamp(value, 1000L, 14400000L);
            }
        }

        /// <summary>
        /// Maximum accepted audio duration in milliseconds (audio analysis, including the audio track of a video file).
        /// Default 14400000 (4 hours). Minimum 1000, maximum 86400000.
        /// </summary>
        public long MaxAudioDurationMs
        {
            get
            {
                return _MaxAudioDurationMs;
            }
            set
            {
                _MaxAudioDurationMs = Math.Clamp(value, 1000L, 86400000L);
            }
        }

        /// <summary>
        /// Maximum audio chunk length in seconds sent to a transcription endpoint. Default 600. Minimum 10, maximum 7200.
        /// </summary>
        public int TranscriptionChunkSeconds
        {
            get
            {
                return _TranscriptionChunkSeconds;
            }
            set
            {
                _TranscriptionChunkSeconds = Math.Clamp(value, 10, 7200);
            }
        }

        /// <summary>
        /// Default format of the audio sent to listening models, used when a request leaves Listen.Rendition.Format null.
        /// Default Mp3. OpenAI-compatible endpoints accept only Mp3 and Wav.
        /// </summary>
        public ListenFormatEnum ListenFormat { get; set; } = ListenFormatEnum.Mp3;

        /// <summary>
        /// Default sample rate in Hz of listening audio. Default 16000. Minimum 8000, maximum 48000.
        /// </summary>
        public int ListenSampleRate
        {
            get
            {
                return _ListenSampleRate;
            }
            set
            {
                _ListenSampleRate = Math.Clamp(value, 8000, 48000);
            }
        }

        /// <summary>
        /// Default channel count of listening audio. Default 1 (mono). Minimum 1, maximum 2.
        /// </summary>
        public int ListenChannels
        {
            get
            {
                return _ListenChannels;
            }
            set
            {
                _ListenChannels = Math.Clamp(value, 1, 2);
            }
        }

        /// <summary>
        /// Default bitrate in kbps of Mp3 listening audio. Ignored for Wav and Flac. Default 64. Minimum 16, maximum 320.
        /// </summary>
        public int ListenBitrateKbps
        {
            get
            {
                return _ListenBitrateKbps;
            }
            set
            {
                _ListenBitrateKbps = Math.Clamp(value, 16, 320);
            }
        }

        /// <summary>
        /// Default listen budget in seconds: the longest audio sent as one clip and the most audio sent in one call, unless the endpoint sets its own MaxListenSeconds. Default 1200. Minimum 10, maximum 7200.
        /// </summary>
        public int MaxListenSeconds
        {
            get
            {
                return _MaxListenSeconds;
            }
            set
            {
                _MaxListenSeconds = Math.Clamp(value, 10, 7200);
            }
        }

        /// <summary>
        /// Level in dBFS below which audio counts as silence in Measurements. Default -50. Minimum -90, maximum -10.
        /// </summary>
        public double MeasureSilenceDb
        {
            get
            {
                return _MeasureSilenceDb;
            }
            set
            {
                _MeasureSilenceDb = Math.Clamp(value, -90.0, -10.0);
            }
        }

        /// <summary>
        /// Shortest silence in milliseconds counted in Measurements. Default 500. Minimum 100, maximum 30000.
        /// </summary>
        public int MeasureMinSilenceMs
        {
            get
            {
                return _MeasureMinSilenceMs;
            }
            set
            {
                _MeasureMinSilenceMs = Math.Clamp(value, 100, 30000);
            }
        }

        /// <summary>
        /// True peak in dBTP at or above which ClippingLikely can be true. Default -0.1. Minimum -6, maximum 3.
        /// </summary>
        public double ClippingTruePeakDbtp
        {
            get
            {
                return _ClippingTruePeakDbtp;
            }
            set
            {
                _ClippingTruePeakDbtp = Math.Clamp(value, -6.0, 3.0);
            }
        }

        /// <summary>
        /// Peak-level sample count per minute of audio at or above which ClippingLikely can be true. Default 10. Minimum 0, maximum 100000.
        /// </summary>
        public double ClippingMinPeaksPerMinute
        {
            get
            {
                return _ClippingMinPeaksPerMinute;
            }
            set
            {
                _ClippingMinPeaksPerMinute = Math.Clamp(value, 0.0, 100000.0);
            }
        }

        /// <summary>
        /// StereoWidthDb at or below which a stereo source is reported as EffectivelyMono. Default -40. Minimum -120, maximum 0.
        /// </summary>
        public double MonoMaxSideDb
        {
            get
            {
                return _MonoMaxSideDb;
            }
            set
            {
                _MonoMaxSideDb = Math.Clamp(value, -120.0, 0.0);
            }
        }

        #endregion

        #region Private-Members

        private string _TempDirectory = "temp";
        private string _FfmpegPath = "ffmpeg";
        private string _FfprobePath = "ffprobe";
        private long _MaxDownloadBytes = 2147483648L;
        private int _DownloadTimeoutMs = 300000;
        private int _ImageMaxEdge = 1568;
        private int _ThumbnailMaxEdge = 320;
        private int _JpegQuality = 3;
        private int _MaxParallelFfmpeg = 4;
        private int _FfmpegTimeoutMs = 600000;
        private long _MaxVideoDurationMs = 14400000L;
        private long _MaxAudioDurationMs = 14400000L;
        private long _MaxSegmentMs = 600000L;
        private int _TranscriptionChunkSeconds = 600;
        private int _ListenSampleRate = 16000;
        private int _ListenChannels = 1;
        private int _ListenBitrateKbps = 64;
        private int _MaxListenSeconds = 1200;
        private double _MeasureSilenceDb = -50.0;
        private int _MeasureMinSilenceMs = 500;
        private double _ClippingTruePeakDbtp = -0.1;
        private double _ClippingMinPeaksPerMinute = 10.0;
        private double _MonoMaxSideDb = -40.0;

        #endregion
    }
}
