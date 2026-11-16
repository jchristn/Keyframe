namespace Keyframe.Core.Observability
{
    using System.Diagnostics;

    /// <summary>
    /// Application activity source for spans. Spans nest under Watson's request span automatically.
    /// Thread safety: safe for concurrent use.
    /// </summary>
    public static class KeyframeTelemetry
    {
        #region Public-Members

        /// <summary>
        /// Activity source. Starting an activity with no listener returns null, so callers use the null-conditional operator.
        /// </summary>
        public static ActivitySource Source
        {
            get
            {
                return _Source;
            }
        }

        #endregion

        #region Private-Members

        private static readonly ActivitySource _Source = new ActivitySource(TelemetryNames.SourceName);

        #endregion
    }
}
