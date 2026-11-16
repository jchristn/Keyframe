namespace Keyframe.Core.Helpers
{
    using System;
    using System.IO;
    using System.Security.Cryptography;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Hashing helpers.
    /// Thread safety: stateless and safe for concurrent use.
    /// </summary>
    public static class HashHelper
    {
        /// <summary>
        /// SHA-256 of a byte array as lowercase hex.
        /// </summary>
        /// <param name="data">Data.</param>
        /// <returns>Hex digest.</returns>
        /// <exception cref="ArgumentNullException">Thrown when data is null.</exception>
        public static string Sha256Hex(byte[] data)
        {
            ArgumentNullException.ThrowIfNull(data);
            return Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
        }

        /// <summary>
        /// SHA-256 of a file as lowercase hex.
        /// </summary>
        /// <param name="path">File path.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Hex digest.</returns>
        /// <exception cref="ArgumentNullException">Thrown when path is null or empty.</exception>
        public static async Task<string> Sha256HexFileAsync(string path, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(path)) throw new ArgumentNullException(nameof(path));
            using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true))
            {
                byte[] hash = await SHA256.HashDataAsync(fs, token).ConfigureAwait(false);
                return Convert.ToHexString(hash).ToLowerInvariant();
            }
        }
    }
}
