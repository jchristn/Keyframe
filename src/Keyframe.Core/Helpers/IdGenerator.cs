namespace Keyframe.Core.Helpers
{
    /// <summary>
    /// Generates prefixed, K-sortable identifiers.
    /// Thread safety: safe for concurrent use.
    /// </summary>
    public static class IdGenerator
    {
        #region Private-Members

        private static readonly PrettyId.IdGenerator _Generator = new PrettyId.IdGenerator();
        private static readonly int _IdLength = 32;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Generate an analysis identifier (prefix "kfa_").
        /// </summary>
        /// <returns>Identifier.</returns>
        public static string GenerateAnalysisId()
        {
            return _Generator.GenerateKSortable(Constants.AnalysisIdPrefix, _IdLength);
        }

        /// <summary>
        /// Generate a job identifier (prefix "kfj_").
        /// </summary>
        /// <returns>Identifier.</returns>
        public static string GenerateJobId()
        {
            return _Generator.GenerateKSortable(Constants.JobIdPrefix, _IdLength);
        }

        /// <summary>
        /// Generate a request history identifier (prefix "req_").
        /// </summary>
        /// <returns>Identifier.</returns>
        public static string GenerateRequestHistoryId()
        {
            return _Generator.GenerateKSortable(Constants.RequestHistoryIdPrefix, _IdLength);
        }

        /// <summary>
        /// Generate a request identifier for logs and traces (prefix "rqi_").
        /// </summary>
        /// <returns>Identifier.</returns>
        public static string GenerateRequestId()
        {
            return _Generator.GenerateKSortable(Constants.RequestIdPrefix, _IdLength);
        }

        /// <summary>
        /// Generate an analytics model call identifier (prefix "kfc_").
        /// </summary>
        /// <returns>Identifier.</returns>
        public static string GenerateModelCallId()
        {
            return _Generator.GenerateKSortable(Constants.ModelCallIdPrefix, _IdLength);
        }

        #endregion
    }
}
