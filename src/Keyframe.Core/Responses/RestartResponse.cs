namespace Keyframe.Core.Responses
{
    /// <summary>
    /// Acknowledgement of a restart request.
    /// </summary>
    public class RestartResponse
    {
        /// <summary>
        /// Always true; the process exits shortly after responding.
        /// </summary>
        public bool Accepted { get; set; } = true;

        /// <summary>
        /// Human readable note.
        /// </summary>
        public string Message { get; set; } = "Restarting. The container runtime relaunches the server and settings are reloaded from disk.";
    }
}
