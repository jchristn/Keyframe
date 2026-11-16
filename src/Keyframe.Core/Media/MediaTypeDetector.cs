namespace Keyframe.Core.Media
{
    using System;
    using System.IO;
    using System.Text;
    using Keyframe.Core.Enums;

    /// <summary>
    /// Detects media types from magic bytes.
    /// Thread safety: stateless and safe for concurrent use.
    /// </summary>
    public static class MediaTypeDetector
    {
        #region Public-Members

        /// <summary>
        /// Bytes read from the start of a file for detection.
        /// </summary>
        public static int HeaderLength { get; } = 64;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Detect the media type of a file.
        /// </summary>
        /// <param name="path">File path.</param>
        /// <returns>Detected type. MediaKind is Unknown when not recognized.</returns>
        /// <exception cref="ArgumentNullException">Thrown when path is null or empty.</exception>
        public static DetectedMediaType DetectFile(string path)
        {
            if (String.IsNullOrEmpty(path)) throw new ArgumentNullException(nameof(path));
            byte[] header = new byte[HeaderLength];
            int read;
            using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                read = fs.Read(header, 0, header.Length);
            }

            if (read < header.Length) Array.Resize(ref header, read);
            return Detect(header);
        }

        /// <summary>
        /// Detect the media type of a byte prefix.
        /// </summary>
        /// <param name="header">At least the first 12 bytes of the media; 64 recommended.</param>
        /// <returns>Detected type. MediaKind is Unknown when not recognized.</returns>
        /// <exception cref="ArgumentNullException">Thrown when header is null.</exception>
        public static DetectedMediaType Detect(byte[] header)
        {
            ArgumentNullException.ThrowIfNull(header);

            if (StartsWith(header, 0, 0xFF, 0xD8, 0xFF)) return Image("image/jpeg", ".jpg");
            if (StartsWith(header, 0, 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A)) return Image("image/png", ".png");
            if (AsciiAt(header, 0, "GIF87a") || AsciiAt(header, 0, "GIF89a")) return Image("image/gif", ".gif");
            if (AsciiAt(header, 0, "RIFF") && AsciiAt(header, 8, "WEBP")) return Image("image/webp", ".webp");
            if (AsciiAt(header, 0, "BM") && header.Length >= 14) return Image("image/bmp", ".bmp");
            if (StartsWith(header, 0, 0x49, 0x49, 0x2A, 0x00) || StartsWith(header, 0, 0x4D, 0x4D, 0x00, 0x2A)) return Image("image/tiff", ".tif");

            if (AsciiAt(header, 4, "ftyp") && header.Length >= 12)
            {
                string brand = Encoding.ASCII.GetString(header, 8, 4);
                switch (brand)
                {
                    case "heic":
                    case "heix":
                    case "heim":
                    case "heis":
                    case "mif1":
                    case "msf1":
                        return Image("image/heic", ".heic");
                    case "avif":
                    case "avis":
                        return Image("image/avif", ".avif");
                    case "qt  ":
                        return Video("video/quicktime", ".mov");
                    case "M4A ":
                    case "M4B ":
                        return Audio("audio/mp4", ".m4a");
                    default:
                        return Video("video/mp4", ".mp4");
                }
            }

            if (StartsWith(header, 0, 0x1A, 0x45, 0xDF, 0xA3))
            {
                string text = Encoding.ASCII.GetString(header);
                if (text.Contains("webm", StringComparison.Ordinal)) return Video("video/webm", ".webm");
                return Video("video/x-matroska", ".mkv");
            }

            if (AsciiAt(header, 0, "RIFF") && AsciiAt(header, 8, "AVI ")) return Video("video/x-msvideo", ".avi");
            if (AsciiAt(header, 0, "RIFF") && AsciiAt(header, 8, "WAVE")) return Audio("audio/wav", ".wav");
            if (StartsWith(header, 0, 0x00, 0x00, 0x01, 0xBA)) return Video("video/mpeg", ".mpg");

            if (AsciiAt(header, 0, "fLaC")) return Audio("audio/flac", ".flac");
            if (AsciiAt(header, 0, "OggS")) return Audio("audio/ogg", ".ogg");
            if (AsciiAt(header, 0, "ID3") || StartsWith(header, 0, 0xFF, 0xFB) || StartsWith(header, 0, 0xFF, 0xF3) || StartsWith(header, 0, 0xFF, 0xF2))
                return Audio("audio/mpeg", ".mp3");
            if (StartsWith(header, 0, 0xFF, 0xF1) || StartsWith(header, 0, 0xFF, 0xF9)) return Audio("audio/aac", ".aac");

            return new DetectedMediaType();
        }

        #endregion

        #region Private-Methods

        private static DetectedMediaType Image(string contentType, string extension)
        {
            return new DetectedMediaType { MediaKind = MediaKindEnum.Image, ContentType = contentType, Extension = extension };
        }

        private static DetectedMediaType Video(string contentType, string extension)
        {
            return new DetectedMediaType { MediaKind = MediaKindEnum.Video, ContentType = contentType, Extension = extension };
        }

        private static DetectedMediaType Audio(string contentType, string extension)
        {
            return new DetectedMediaType { MediaKind = MediaKindEnum.Audio, ContentType = contentType, Extension = extension };
        }

        private static bool StartsWith(byte[] data, int offset, params byte[] signature)
        {
            if (data.Length < offset + signature.Length) return false;
            for (int i = 0; i < signature.Length; i++)
            {
                if (data[offset + i] != signature[i]) return false;
            }

            return true;
        }

        private static bool AsciiAt(byte[] data, int offset, string text)
        {
            if (data.Length < offset + text.Length) return false;
            for (int i = 0; i < text.Length; i++)
            {
                if (data[offset + i] != (byte)text[i]) return false;
            }

            return true;
        }

        #endregion
    }
}
