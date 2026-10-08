using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Umbraco.AI.Core.RuntimeContext;
using Umbraco.AI.Core.Tools;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Core.Events;

namespace Umbraco.AI.Core.Chat.Middleware;

/// <summary>
/// Middleware that adds automatic function/tool invocation support to chat clients.
/// </summary>
/// <remarks>
/// <para>
/// This middleware wraps the chat client with <see cref="FunctionInvokingChatClient"/>
/// which automatically invokes tools when the model requests them and feeds results
/// back to the model.
/// </para>
/// <para>
/// Each tool call publishes <see cref="AIToolExecutingNotification"/> (cancelable) and
/// <see cref="AIToolExecutedNotification"/>.
/// </para>
/// <para>
/// When no tools are configured in <see cref="ChatOptions.Tools"/>, this middleware
/// is effectively a no-op passthrough.
/// </para>
/// </remarks>
public sealed class AIFunctionInvokingChatMiddleware : IAIChatMiddleware
{
    private readonly ILoggerFactory? _loggerFactory;
    private readonly AIToolNotificationInvoker _invoker;

    /// <summary>
    /// Initializes a new instance of the <see cref="AIFunctionInvokingChatMiddleware"/> class.
    /// </summary>
    /// <param name="eventAggregator">Publishes the tool execution notifications.</param>
    /// <param name="runtimeContextAccessor">Supplies the runtime context for the notifications.</param>
    /// <param name="loggerFactory">Optional logger factory for function invocation logging.</param>
    [ActivatorUtilitiesConstructor]
    public AIFunctionInvokingChatMiddleware(
        IEventAggregator eventAggregator,
        IAIRuntimeContextAccessor runtimeContextAccessor,
        ILoggerFactory? loggerFactory = null)
    {
        _loggerFactory = loggerFactory;
        _invoker = new AIToolNotificationInvoker(eventAggregator, runtimeContextAccessor);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AIFunctionInvokingChatMiddleware"/> class.
    /// </summary>
    /// <param name="loggerFactory">Optional logger factory for function invocation logging.</param>
    [Obsolete("Use the constructor that accepts IEventAggregator and IAIRuntimeContextAccessor. Will be removed in v20")]
    public AIFunctionInvokingChatMiddleware(ILoggerFactory? loggerFactory = null)
        : this(
            StaticServiceProvider.Instance.GetRequiredService<IEventAggregator>(),
            StaticServiceProvider.Instance.GetRequiredService<IAIRuntimeContextAccessor>(),
            loggerFactory)
    {
    }

    /// <inheritdoc />
    public IChatClient Apply(IChatClient client)
    {
        return client.AsBuilder()
            .UseFunctionInvocation(_loggerFactory, functionInvoking =>
                functionInvoking.FunctionInvoker = _invoker.InvokeAsync)
            .Build();
    }
}
