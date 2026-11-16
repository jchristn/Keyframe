namespace Keyframe.Core.Media
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Net.Sockets;
    using System.Threading;
    using System.Threading.Tasks;
    using Keyframe.Core.Enums;
    using Keyframe.Core.Exceptions;
    using Keyframe.Core.Helpers;
    using Keyframe.Core.Requests;
    using Keyframe.Core.Settings;

    /// <summary>
    /// Writes request media (URL or base64) to a workspace file, enforcing size limits and SSRF rules.
    /// Thread safety: safe for concurrent use.
    /// </summary>
    public class MediaResolver : IDisposable
    {
        #region Private-Members

        private readonly MediaSettings _Settings;
        private readonly HttpClient _Http;
        private readonly bool _OwnsHttp;
        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate a resolver with its own guarded HTTP client.
        /// </summary>
        /// <param name="settings">Media settings. Read on every call, so live changes apply.</param>
        /// <exception cref="ArgumentNullException">Thrown when settings is null.</exception>
        public MediaResolver(MediaSettings settings)
        {
            ArgumentNullException.ThrowIfNull(settings);
            _Settings = settings;
            _Http = CreateGuardedHttpClient(settings);
            _OwnsHttp = true;
        }

        /// <summary>
        /// Instantiate a resolver with a caller-supplied HTTP client. The caller is responsible for SSRF protection.
        /// </summary>
        /// <param name="settings">Media settings.</param>
        /// <param name="http">HTTP client. Not disposed by the resolver.</param>
        /// <exception cref="ArgumentNullException">Thrown when settings or http is null.</exception>
        public MediaResolver(MediaSettings settings, HttpClient http)
        {
            ArgumentNullException.ThrowIfNull(settings);
            ArgumentNullException.ThrowIfNull(http);
            _Settings = settings;
            _Http = http;
            _OwnsHttp = false;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Create an HTTP client whose connections refuse restricted addresses unless private URLs are allowed.
        /// The check runs at connect time, so it also covers redirects and DNS rebinding.
        /// </summary>
        /// <param name="settings">Media settings.</param>
        /// <returns>HTTP client.</returns>
        /// <exception cref="ArgumentNullException">Thrown when settings is null.</exception>
        public static HttpClient CreateGuardedHttpClient(MediaSettings settings)
        {
            ArgumentNullException.ThrowIfNull(settings);

            SocketsHttpHandler handler = new SocketsHttpHandler
            {
                AllowAutoRedirect = true,
                MaxAutomaticRedirections = 5,
                AutomaticDecompression = DecompressionMethods.None,
                ConnectTimeout = TimeSpan.FromSeconds(30),
                ConnectCallback = async (context, cancellationToken) =>
                {
                    IPAddress[] addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, cancellationToken).ConfigureAwait(false);
                    List<IPAddress> allowed = addresses.Where(a => settings.AllowPrivateNetworkUrls || !UrlGuard.IsRestricted(a)).ToList();
                    if (allowed.Count < 1)
                        throw new HttpRequestException("Media URL host '" + context.DnsEndPoint.Host + "' resolves to a restricted network address.");

                    Socket socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                    try
                    {
                        await socket.ConnectAsync(allowed.ToArray(), context.DnsEndPoint.Port, cancellationToken).ConfigureAwait(false);
                        return new NetworkStream(socket, true);
                    }
                    catch
                    {
                        socket.Dispose();
                        throw;
                    }
                }
            };

            HttpClient client = new HttpClient(handler, true) { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Keyframe/0.1");
            return client;
        }

        /// <summary>
        /// Resolve request media into a workspace file.
        /// </summary>
        /// <param name="source">Media source.</param>
        /// <param name="expected">Expected media kind. A detected mismatch throws.</param>
        /// <param name="workspace">Workspace.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Resolved media.</returns>
        /// <exception cref="ArgumentNullException">Thrown when source or workspace is null.</exception>
        /// <exception cref="MediaResolutionException">Thrown when the media cannot be downloaded or decoded, or exceeds limits.</exception>
        /// <exception cref="UnsupportedMediaException">Thrown when the detected media kind does not match the expected kind.</exception>
        /// <exception cref="OperationCanceledException">Thrown when canceled.</exception>
        public async Task<ResolvedMedia> ResolveAsync(MediaSource source, MediaKindEnum expected, MediaWorkspace workspace, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(workspace);
            ObjectDisposedException.ThrowIf(_Disposed, this);

            string tempPath = workspace.PathFor("input.download");
            ResolvedMedia resolved = new ResolvedMedia();
            string? reportedType = source.MediaType;

            if (!String.IsNullOrWhiteSpace(source.Data))
            {
                await WriteBase64Async(source.Data, tempPath, token).ConfigureAwait(false);
            }
            else if (!String.IsNullOrWhiteSpace(source.Url))
            {
                resolved.SourceUrl = source.Url;
                string? serverType = await DownloadAsync(source.Url, tempPath, token).ConfigureAwait(false);
                if (reportedType == null) reportedType = serverType;
            }
            else
            {
                throw new MediaResolutionException("Media must specify Url or Data.");
            }

            FileInfo info = new FileInfo(tempPath);
            if (!info.Exists || info.Length < 1) throw new MediaResolutionException("Media is empty.");

            DetectedMediaType detected = MediaTypeDetector.DetectFile(tempPath);
            if (detected.MediaKind != MediaKindEnum.Unknown && expected != MediaKindEnum.Unknown && detected.MediaKind != expected)
            {
                throw new UnsupportedMediaException(
                    "Expected " + expected.ToString().ToLowerInvariant() + " media but received " +
                    detected.MediaKind.ToString().ToLowerInvariant() + " (" + detected.ContentType + ").");
            }

            string finalPath = workspace.PathFor("input" + detected.Extension);
            File.Move(tempPath, finalPath, true);

            resolved.Path = finalPath;
            resolved.SizeBytes = info.Length;
            resolved.MediaKind = detected.MediaKind;
            resolved.ContentType = detected.ContentType ?? reportedType;
            resolved.Sha256 = await HashHelper.Sha256HexFileAsync(finalPath, token).ConfigureAwait(false);
            return resolved;
        }

        /// <summary>
        /// Dispose the resolver and its HTTP client when owned.
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
            if (disposing && _OwnsHttp) _Http.Dispose();
        }

        private async Task WriteBase64Async(string data, string path, CancellationToken token)
        {
            string payload = data.Trim();
            if (payload.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                int comma = payload.IndexOf(',');
                if (comma < 0) throw new MediaResolutionException("Data URL is missing its payload.");
                payload = payload.Substring(comma + 1);
            }

            long estimated = (long)payload.Length * 3 / 4;
            if (estimated > _Settings.MaxDownloadBytes)
                throw new MediaResolutionException("Media exceeds the maximum size of " + _Settings.MaxDownloadBytes + " bytes.");

            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(payload);
            }
            catch (FormatException ex)
            {
                throw new MediaResolutionException("Data is not valid base64.", ex);
            }

            await File.WriteAllBytesAsync(path, bytes, token).ConfigureAwait(false);
        }

        private async Task<string?> DownloadAsync(string url, string path, CancellationToken token)
        {
            using (CancellationTokenSource timeout = new CancellationTokenSource(_Settings.DownloadTimeoutMs))
            using (CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(token, timeout.Token))
            {
                try
                {
                    using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, url))
                    using (HttpResponseMessage response = await _Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linked.Token).ConfigureAwait(false))
                    {
                        if (!response.IsSuccessStatusCode)
                            throw new MediaResolutionException("Downloading media failed with HTTP " + (int)response.StatusCode + " from '" + url + "'.");

                        long? length = response.Content.Headers.ContentLength;
                        if (length.HasValue && length.Value > _Settings.MaxDownloadBytes)
                            throw new MediaResolutionException("Media at '" + url + "' is " + length.Value + " bytes, over the limit of " + _Settings.MaxDownloadBytes + ".");

                        using (Stream input = await response.Content.ReadAsStreamAsync(linked.Token).ConfigureAwait(false))
                        using (FileStream output = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
                        {
                            byte[] buffer = new byte[81920];
                            long total = 0;
                            int read;
                            while ((read = await input.ReadAsync(buffer, 0, buffer.Length, linked.Token).ConfigureAwait(false)) > 0)
                            {
                                total += read;
                                if (total > _Settings.MaxDownloadBytes)
                                    throw new MediaResolutionException("Media at '" + url + "' exceeds the limit of " + _Settings.MaxDownloadBytes + " bytes.");
                                await output.WriteAsync(buffer, 0, read, linked.Token).ConfigureAwait(false);
                            }
                        }

                        return response.Content.Headers.ContentType?.MediaType;
                    }
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested)
                {
                    throw new MediaResolutionException("Downloading media from '" + url + "' timed out after " + _Settings.DownloadTimeoutMs + "ms.");
                }
                catch (HttpRequestException ex)
                {
                    throw new MediaResolutionException("Downloading media from '" + url + "' failed: " + ex.Message, ex);
                }
            }
        }

        #endregion
    }
}
