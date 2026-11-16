namespace Keyframe.McpServer.Tools
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Voltaic.Core;

    /// <summary>
    /// Registers one tool on a transport.
    /// </summary>
    /// <param name="name">Tool name.</param>
    /// <param name="description">Description shown to agents.</param>
    /// <param name="inputSchema">JSON Schema object for the arguments.</param>
    /// <param name="handler">Handler returning the tool result.</param>
    public delegate void RegisterToolDelegate(string name, string description, object inputSchema, Func<RpcParameters?, CancellationToken, Task<object>> handler);
}
