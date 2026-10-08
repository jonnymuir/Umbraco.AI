using System.Diagnostics;
using Microsoft.Extensions.AI;
using Umbraco.AI.Core.RuntimeContext;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;

namespace Umbraco.AI.Core.Tools;

/// <summary>
/// Marks an <see cref="AIFunction"/> that wraps an Umbraco <see cref="IAITool"/>, so tool
/// notifications can expose the tool.
/// </summary>
internal interface IAIToolBackedFunction
{
    IAITool Tool { get; }
}

/// <summary>
/// Marks an <see cref="AIFunction"/> that never runs the tool it stands in for (for example a
/// destructive tool denied by an approval policy). Tool notifications are not published for it.
/// </summary>
internal interface IAINonExecutingFunction
{
}

/// <summary>
/// The <see cref="FunctionInvokingChatClient.FunctionInvoker"/> used by the chat pipeline. It
/// publishes <see cref="AIToolExecutingNotification"/> and <see cref="AIToolExecutedNotification"/>
/// around each tool call.
/// </summary>
internal sealed class AIToolNotificationInvoker
{
    private readonly IEventAggregator _eventAggregator;
    private readonly IAIRuntimeContextAccessor _runtimeContextAccessor;

    public AIToolNotificationInvoker(
        IEventAggregator eventAggregator,
        IAIRuntimeContextAccessor runtimeContextAccessor)
    {
        _eventAggregator = eventAggregator;
        _runtimeContextAccessor = runtimeContextAccessor;
    }

    public async ValueTask<object?> InvokeAsync(
        FunctionInvocationContext context,
        CancellationToken cancellationToken)
    {
        var function = context.Function;

        if (function is IAINonExecutingFunction)
        {
            return await function.InvokeAsync(context.Arguments, cancellationToken);
        }

        var tool = FindTool(function);
        var callId = context.CallContent.CallId;
        IReadOnlyDictionary<string, object?> arguments = context.Arguments;
        var runtimeContext = _runtimeContextAccessor.Context;

        var eventMessages = new EventMessages();
        var executing = new AIToolExecutingNotification(
            function, tool, callId, arguments, runtimeContext, eventMessages);
        await _eventAggregator.PublishAsync(executing, cancellationToken);

        if (executing.Cancel)
        {
            return BuildBlockedResult(function.Name, eventMessages);
        }

        // A frontend tool hands the call to the browser by setting Terminate, rather than running
        // anything here. Its result arrives in a later request, so there is nothing to report.
        var terminateBefore = context.Terminate;
        var stopwatch = Stopwatch.StartNew();
        object? result = null;
        Exception? exception = null;

        try
        {
            result = await function.InvokeAsync(context.Arguments, cancellationToken);
            return result;
        }
        catch (Exception ex)
        {
            exception = ex;
            throw;
        }
        finally
        {
            stopwatch.Stop();

            var handedToClient = exception is null && context.Terminate && !terminateBefore;
            if (!handedToClient)
            {
                // A typed tool that throws returns a ToolInvocationError (so the model gets a
                // readable error) instead of throwing. Report it as the failure it is.
                var invocationError = exception is null ? result as ToolInvocationError : null;

                var executed = new AIToolExecutedNotification(
                    function,
                    tool,
                    callId,
                    arguments,
                    runtimeContext,
                    stopwatch.Elapsed,
                    isSuccess: exception is null && invocationError is null,
                    eventMessages)
                {
                    Result = result,
                    Exception = exception ?? invocationError?.Exception,
                }
                .WithStateFrom(executing);

                // Not the call's token: a cancelled call should still be reported.
                await _eventAggregator.PublishAsync(executed, CancellationToken.None);
            }
        }
    }

    internal static string BuildBlockedResult(string toolName, EventMessages messages)
    {
        var reasons = string.Join(" ", messages.GetAll()
            .Select(m => m.Message)
            .Where(m => !string.IsNullOrWhiteSpace(m)));

        return string.IsNullOrEmpty(reasons)
            ? $"The '{toolName}' tool was blocked and was not run."
            : $"The '{toolName}' tool was blocked and was not run. Reason: {reasons}";
    }

    // GetService walks through DelegatingAIFunction wrappers (e.g. ApprovalRequiredAIFunction)
    // to the function underneath.
    private static IAITool? FindTool(AIFunction function)
        => (function.GetService(typeof(IAIToolBackedFunction)) as IAIToolBackedFunction)?.Tool;
}
