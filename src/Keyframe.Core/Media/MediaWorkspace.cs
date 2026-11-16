namespace Keyframe.Core.Media
{
    using System;
    using System.IO;

    /// <summary>
    /// A temporary working directory for one request or job. Deleted on dispose.
    /// Thread safety: path generation is safe for concurrent use; dispose once.
    /// </summary>
    public class MediaWorkspace : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// Absolute path of the workspace directory.
        /// </summary>
        public string Directory
        {
            get
            {
                return _Directory;
            }
        }

        #endregion

        #region Private-Members

        private readonly string _Directory;
        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Create a new workspace under a root directory.
        /// </summary>
        /// <param name="rootDirectory">Root temp directory. Created when missing.</param>
        /// <exception cref="ArgumentNullException">Thrown when rootDirectory is null or empty.</exception>
        public MediaWorkspace(string rootDirectory)
        {
            if (String.IsNullOrWhiteSpace(rootDirectory)) throw new ArgumentNullException(nameof(rootDirectory));
            string root = Path.GetFullPath(rootDirectory);
            _Directory = Path.Combine(root, "kf-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss") + "-" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(_Directory);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build a path for a file inside the workspace.
        /// </summary>
        /// <param name="fileName">File name. Directory separators are not allowed.</param>
        /// <returns>Absolute path.</returns>
        /// <exception cref="ArgumentException">Thrown when the file name is empty or contains a path separator.</exception>
        public string PathFor(string fileName)
        {
            if (String.IsNullOrWhiteSpace(fileName) || fileName.IndexOfAny(new[] { '/', '\\' }) >= 0 || fileName.Contains(".."))
                throw new ArgumentException("Invalid workspace file name '" + fileName + "'.", nameof(fileName));
            return Path.Combine(_Directory, fileName);
        }

        /// <summary>
        /// Create and return a subdirectory inside the workspace.
        /// </summary>
        /// <param name="name">Subdirectory name.</param>
        /// <returns>Absolute path.</returns>
        public string CreateSubdirectory(string name)
        {
            string path = PathFor(name);
            System.IO.Directory.CreateDirectory(path);
            return path;
        }

        /// <summary>
        /// Delete workspace directories under a root that are older than a cutoff. Best effort.
        /// </summary>
        /// <param name="rootDirectory">Root temp directory.</param>
        /// <param name="olderThan">Age threshold.</param>
        /// <returns>Directories deleted.</returns>
        public static int CleanupOrphans(string rootDirectory, TimeSpan olderThan)
        {
            int deleted = 0;
            try
            {
                string root = Path.GetFullPath(rootDirectory);
                if (!System.IO.Directory.Exists(root)) return 0;
                DateTime cutoff = DateTime.UtcNow - olderThan;
                foreach (string dir in System.IO.Directory.GetDirectories(root, "kf-*"))
                {
                    try
                    {
                        if (System.IO.Directory.GetCreationTimeUtc(dir) < cutoff)
                        {
                            System.IO.Directory.Delete(dir, true);
                            deleted++;
                        }
                    }
                    catch (IOException)
                    {
                    }
                    catch (UnauthorizedAccessException)
                    {
                    }
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }

            return deleted;
        }

        /// <summary>
        /// Delete the workspace directory.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion

        #region Private-Methods

        /// <summary>
        /// Dispose pattern implementation.
        /// </summary>
        /// <param name="disposing">True when called from Dispose.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (_Disposed) return;
            _Disposed = true;
            try
            {
                if (System.IO.Directory.Exists(_Directory)) System.IO.Directory.Delete(_Directory, true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        #endregion
    }
}
