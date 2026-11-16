namespace Keyframe.Sdk.Responses
{
    using System;

    /// <summary>
    /// Outcome of POST /v1.0/audio. Exactly one of Job or Result is set.
    /// The server answers 202 with a job, 200 with the full result when Wait was true and the job succeeded in time,
    /// or 200 with the job when Wait was true and the job finished without succeeding.
    /// </summary>
    public class AudioSubmitResult
    {
        /// <summary>
        /// HTTP status code (200 or 202).
        /// </summary>
        public int StatusCode { get; set; } = 0;

        /// <summary>
        /// Job identifier. Always set: from the job body, or from the x-keyframe-job-id header for a synchronous result.
        /// </summary>
        public string JobId { get; set; } = String.Empty;

        /// <summary>
        /// The job, when the server returned one (202, or 200 for a finished job that did not succeed). Null otherwise.
        /// </summary>
        public JobResponse? Job { get; set; } = null;

        /// <summary>
        /// The full result, when Wait was true and the job succeeded within the wait. Null otherwise.
        /// </summary>
        public AudioAnalysisResponse? Result { get; set; } = null;

        /// <summary>
        /// Whether the full result is present.
        /// </summary>
        public bool HasResult
        {
            get
            {
                return Result != null;
            }
        }
    }
}
