namespace Test.Shared
{
    using System;
    using System.IO;
    using System.Net;
    using System.Net.Http;
    using System.Net.Sockets;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Keyframe.Core.Serialization;
    using Keyframe.Core.Settings;
    using Keyframe.Server;
    using SyslogLogging;

    /// <summary>
    /// An in-process Keyframe server on 127.0.0.1 with a free port, SQLite in the test root, and the mock provider.
    /// </summary>
    public sealed class ServerFixture : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// Admin key.
        /// </summary>
        public const string AdminKey = "test-admin-key-1234";

        /// <summary>
        /// Data-plane key.
        /// </summary>
        public const string AccessKey = "test-access-key-5678";

        /// <summary>
        /// Server.
        /// </summary>
        public KeyframeServer Server { get; }

        /// <summary>
        /// Settings manager.
        /// </summary>
        public SettingsManager Settings { get; }

        /// <summary>
        /// HTTP client pointed at the server.
        /// </summary>
        public HttpClient Http { get; }

        #endregion

        #region Private-Members

        private static readonly SemaphoreSlim _SharedLock = new SemaphoreSlim(1, 1);
        private static ServerFixture? _Shared = null;

        #endregion

        #region Constructors-and-Factories

        private ServerFixture(KeyframeServer server, SettingsManager settings)
        {
            Server = server;
            Settings = settings;
            Http = new HttpClient { BaseAddress = new Uri(server.BaseUrl), Timeout = TimeSpan.FromMinutes(2) };
        }

        /// <summary>
        /// The shared fixture, started on first use.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Fixture.</returns>
        public static async Task<ServerFixture> SharedAsync(CancellationToken token)
        {
            await _SharedLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                if (_Shared == null) _Shared = await StartAsync(null, token).ConfigureAwait(false);
                return _Shared;
            }
            finally
            {
                _SharedLock.Release();
            }
        }

        /// <summary>
        /// Start a dedicated fixture.
        /// </summary>
        /// <param name="configure">Optional settings customization.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Fixture; dispose it when done.</returns>
        public static async Task<ServerFixture> StartAsync(Action<KeyframeSettings>? configure, CancellationToken token)
        {
            KeyframeSettings settings = TestEnvironment.CreateSettings();
            settings.Rest.Port = FreePort();
            settings.Auth.AdminApiKey = AdminKey;
            settings.Auth.AccessKeys.Clear();
            settings.Auth.AccessKeys.Add(new AccessKeySettings { Name = "tester", Key = AccessKey, Active = true });
            settings.Auth.AccessKeys.Add(new AccessKeySettings { Name = "disabled", Key = "disabled-key-0000", Active = false });
            settings.Logging.ConsoleLogging = false;
            settings.Logging.LogDirectory = null;
            settings.Telemetry.Enable = false;
            configure?.Invoke(settings);

            string file = Path.Combine(TestEnvironment.Root, "settings-" + Guid.NewGuid().ToString("N") + ".json");
            SettingsManager manager = new SettingsManager(settings, file);
            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;

            KeyframeServer server = new KeyframeServer(manager, logging);
            await server.StartAsync(token).ConfigureAwait(false);
            return new ServerFixture(server, manager);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Send a request.
        /// </summary>
        /// <param name="method">Method.</param>
        /// <param name="path">Path.</param>
        /// <param name="key">Key sent as x-api-key, or null.</param>
        /// <param name="body">JSON body object or raw string, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Response.</returns>
        public async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string? key, object? body, CancellationToken token)
        {
            using (HttpRequestMessage request = new HttpRequestMessage(method, path))
            {
                if (key != null) request.Headers.Add("x-api-key", key);
                if (body != null)
                {
                    string json = body is string s ? s : KeyframeSerializer.Serialize(body, false);
                    request.Content = new StringContent(json, Encoding.UTF8, "application/json");
                }

                return await Http.SendAsync(request, token).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Send a request and deserialize the JSON response.
        /// </summary>
        /// <typeparam name="T">Response type.</typeparam>
        /// <param name="method">Method.</param>
        /// <param name="path">Path.</param>
        /// <param name="key">Key, or null.</param>
        /// <param name="body">Body, or null.</param>
        /// <param name="expectedStatus">Expected status code.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Response body.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the status differs or the body is empty.</exception>
        public async Task<T> JsonAsync<T>(HttpMethod method, string path, string? key, object? body, HttpStatusCode expectedStatus, CancellationToken token) where T : class
        {
            using (HttpResponseMessage response = await SendAsync(method, path, key, body, token).ConfigureAwait(false))
            {
                string text = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
                if (response.StatusCode != expectedStatus)
                    throw new InvalidOperationException(method + " " + path + ": expected " + (int)expectedStatus + " but got " + (int)response.StatusCode + ": " + text);
                T? value = KeyframeSerializer.Deserialize<T>(text);
                if (value == null) throw new InvalidOperationException(method + " " + path + ": empty body.");
                return value;
            }
        }

        /// <summary>
        /// Dispose.
        /// </summary>
        public void Dispose()
        {
            try
            {
                Server.StopAsync(CancellationToken.None).GetAwaiter().GetResult();
            }
            catch (Exception)
            {
            }

            Server.Dispose();
            Http.Dispose();
        }

        #endregion

        #region Private-Methods

        private static int FreePort()
        {
            TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        #endregion
    }
}
