namespace Test.Shared
{
    /// <summary>
    /// A request recorded by <see cref="StubProviderServer"/>.
    /// </summary>
    public sealed class StubRequest
    {
        /// <summary>
        /// Request path.
        /// </summary>
        public string Path { get; set; } = "/";

        /// <summary>
        /// Content type header.
        /// </summary>
        public string? ContentType { get; set; } = null;

        /// <summary>
        /// Body as UTF-8 text.
        /// </summary>
        public string Body { get; set; } = string.Empty;
    }
}
