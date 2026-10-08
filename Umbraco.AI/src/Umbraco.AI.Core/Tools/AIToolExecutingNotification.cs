using Microsoft.Extensions.AI;
using Umbraco.AI.Core.RuntimeContext;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;

namespace Umbraco.AI.Core.Tools;

/// <summary>
/// Published before a tool the model called is run (cancelable).
/// </summary>
/// <remarks>
/// <para>
/// Fires for every tool call made through the chat pipeline: backend tools, any other
/// <see cref="AIFunction"/> passed in the chat options, and frontend tools (before they are handed
/// to the browser). It also fires when a tool that needed human approval is run after being
/// approved, so a handler can still block it at that point.
/// </para>
/// <para>
/// Setting <see cref="CancelableNotification.Cancel"/> blocks the call. The tool is not run, and the
/// model receives a tool result saying the call was blocked, so the run carries on rather than
/// failing. Messages added to <see cref="CancelableNotification.Messages"/> are included in that
/// result as the reason, so they are <strong>sent to the model</strong>: keep them free of
/// sensitive detail.
/// </para>
/// <para>
/// Not published for a destructive tool that an approval policy denied without running it.
/// </para>
/// </remarks>
public sealed class AIToolExecutingNotification : CancelableNotification
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AIToolExecutingNotification"/> class.
    /// </summary>
    /// <param name="function">The function the model called.</param>
    /// <param name="tool">The Umbraco tool behind the function, if any.</param>
    /// <param name="callId">The model's ID for this tool call.</param>
    /// <param name="arguments">The arguments the model supplied.</param>
    /// <param name="runtimeContext">The runtime context of the current AI operation, if any.</param>
    /// <param name="messages">Event messages for cancellation reasons.</param>
    public AIToolExecutingNotification(
        AIFunction function,
        IAITool? tool,
        string callId,
        IReadOnlyDictionary<string, object?> arguments,
        AIRuntimeContext? runtimeContext,
        EventMessages messages)
        : base(messages)
    {
        Function = function;
        Tool = tool;
        CallId = callId;
        Arguments = arguments;
        RuntimeContext = runtimeContext;
    }

    /// <summary>
    /// Gets the name of the tool the model called (the tool ID for Umbraco tools).
    /// </summary>
    public string ToolName => Function.Name;

    /// <summary>
    /// Gets the function the model called.
    /// </summary>
    public AIFunction Function { get; }

    /// <summary>
    /// Gets the Umbraco tool behind the function, or null when the function is not an
    /// <see cref="IAITool"/> (for example a frontend tool).
    /// </summary>
    public IAITool? Tool { get; }

    /// <summary>
    /// Gets the model's ID for this tool call.
    /// </summary>
    public string CallId { get; }

    /// <summary>
    /// Gets the arguments the model supplied.
    /// </summary>
    public IReadOnlyDictionary<string, object?> Arguments { get; }

    /// <summary>
    /// Gets the runtime context of the current AI operation (feature, profile, user and request
    /// context), or null when the call happens outside one.
    /// </summary>
    public AIRuntimeContext? RuntimeContext { get; }
}
