using Microsoft.Extensions.AI;
using Umbraco.AI.Core.RuntimeContext;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;

namespace Umbraco.AI.Core.Tools;

/// <summary>
/// Published after a tool the model called has run on the server (not cancelable).
/// </summary>
/// <remarks>
/// <para>
/// Not published when <see cref="AIToolExecutingNotification"/> was cancelled, nor for frontend
/// tools: those are handed to the browser and their result arrives in a later request.
/// </para>
/// </remarks>
public sealed class AIToolExecutedNotification : StatefulNotification
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AIToolExecutedNotification"/> class.
    /// </summary>
    /// <param name="function">The function the model called.</param>
    /// <param name="tool">The Umbraco tool behind the function, if any.</param>
    /// <param name="callId">The model's ID for this tool call.</param>
    /// <param name="arguments">The arguments the model supplied.</param>
    /// <param name="runtimeContext">The runtime context of the current AI operation, if any.</param>
    /// <param name="duration">How long the tool took to run.</param>
    /// <param name="isSuccess">Whether the tool completed without throwing.</param>
    /// <param name="messages">Event messages from the execution.</param>
    public AIToolExecutedNotification(
        AIFunction function,
        IAITool? tool,
        string callId,
        IReadOnlyDictionary<string, object?> arguments,
        AIRuntimeContext? runtimeContext,
        TimeSpan duration,
        bool isSuccess,
        EventMessages messages)
    {
        Function = function;
        Tool = tool;
        CallId = callId;
        Arguments = arguments;
        RuntimeContext = runtimeContext;
        Duration = duration;
        IsSuccess = isSuccess;
        Messages = messages;
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
    /// <see cref="IAITool"/>.
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
    /// Gets the runtime context of the current AI operation, or null when the call happens
    /// outside one.
    /// </summary>
    public AIRuntimeContext? RuntimeContext { get; }

    /// <summary>
    /// Gets how long the tool took to run.
    /// </summary>
    public TimeSpan Duration { get; }

    /// <summary>
    /// Gets whether the tool completed without throwing.
    /// </summary>
    public bool IsSuccess { get; }

    /// <summary>
    /// Gets the tool's result, or null when it failed or returned nothing.
    /// </summary>
    public object? Result { get; init; }

    /// <summary>
    /// Gets the exception the tool threw, or null when it succeeded.
    /// </summary>
    public Exception? Exception { get; init; }

    /// <summary>
    /// Gets the event messages.
    /// </summary>
    public EventMessages Messages { get; }
}
