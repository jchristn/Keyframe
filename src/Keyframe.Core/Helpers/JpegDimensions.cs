namespace Keyframe.Core.Helpers
{
    using System;

    /// <summary>
    /// Reads pixel dimensions from a JPEG header without decoding it.
    /// Thread safety: stateless and safe for concurrent use.
    /// </summary>
    public static class JpegDimensions
    {
        /// <summary>
        /// Read width and height from a JPEG start-of-frame marker.
        /// </summary>
        /// <param name="data">JPEG bytes.</param>
        /// <param name="width">Width in pixels, or 0 when not found.</param>
        /// <param name="height">Height in pixels, or 0 when not found.</param>
        /// <returns>True when dimensions were found.</returns>
        /// <exception cref="ArgumentNullException">Thrown when data is null.</exception>
        public static bool TryRead(byte[] data, out int width, out int height)
        {
            ArgumentNullException.ThrowIfNull(data);
            width = 0;
            height = 0;
            if (data.Length < 4 || data[0] != 0xFF || data[1] != 0xD8) return false;

            int i = 2;
            while (i + 9 < data.Length)
            {
                if (data[i] != 0xFF)
                {
                    i++;
                    continue;
                }

                byte marker = data[i + 1];
                if (marker == 0xD8 || marker == 0x01 || (marker >= 0xD0 && marker <= 0xD7))
                {
                    i += 2;
                    continue;
                }

                if (marker == 0xFF)
                {
                    i++;
                    continue;
                }

                int length = (data[i + 2] << 8) | data[i + 3];
                bool isStartOfFrame = marker >= 0xC0 && marker <= 0xCF && marker != 0xC4 && marker != 0xC8 && marker != 0xCC;
                if (isStartOfFrame)
                {
                    height = (data[i + 5] << 8) | data[i + 6];
                    width = (data[i + 7] << 8) | data[i + 8];
                    return width > 0 && height > 0;
                }

                if (length < 2) return false;
                i += 2 + length;
            }

            return false;
        }
    }
}
