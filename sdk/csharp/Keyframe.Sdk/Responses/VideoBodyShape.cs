namespace Keyframe.Sdk.Responses
{
    /// <summary>
    /// Minimal view of a POST /v1.0/video 200 body used to tell a JobResponse (which has Status) from a
    /// VideoAnalysisResponse (which does not).
    /// </summary>
    internal class VideoBodyShape
    {
        /// <summary>
        /// Job status text when the body is a JobResponse, otherwise null.
        /// </summary>
        public string? Status { get; set; } = null;
    }
}
