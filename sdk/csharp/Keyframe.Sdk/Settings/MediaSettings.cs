namespace Keyframe.Sdk.Settings
{
    using System;

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
        private int _TranscriptionChunkSeconds = 600;

        #endregion
    }
}
