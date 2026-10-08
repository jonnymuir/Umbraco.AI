using Microsoft.Extensions.AI;
using Umbraco.AI.Core.Tools;

namespace Umbraco.AI.Agent.Core.Chat;

/// <summary>
/// Wraps a destructive backend tool so that, under <see cref="Agents.AIApprovalPolicy.DenyAll"/>
/// (or, for tools that require approval, <see cref="Agents.AIApprovalPolicy.DenyApprovalRequired"/>),
/// it is never executed. When the model calls it, the inner function is bypassed and a denial
/// result is returned instead, telling the model the action required approval that was not granted.
/// </summary>
/// <remarks>
/// <para>
/// This keeps non-interactive runs flowing: the model receives a tool result (the denial) and can
/// complete its turn, rather than the run stalling on an unresolved
/// <see cref="ApprovalRequiredAIFunction"/> with no interactive surface to approve it.
/// </para>
/// <para>
/// <see cref="DelegatingAIFunction"/> passes <see cref="AIFunction.Name"/>,
/// <see cref="AIFunction.Description"/> and the JSON schema through to the inner function, so the
/// model still sees the tool exactly as declared — only invocation is short-circuited.
/// </para>
/// <para>
/// Marked <see cref="IAINonExecutingFunction"/> so tool execution notifications are not published
/// for a tool that never runs.
/// </para>
/// </remarks>
internal sealed class ApprovalDeniedAIFunction : DelegatingAIFunction, IAINonExecutingFunction
{
    public ApprovalDeniedAIFunction(AIFunction innerFunction)
        : base(innerFunction)
    {
    }

    protected override ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments arguments,
        CancellationToken cancellationToken)
        => ValueTask.FromResult<object?>(
            $"This action requires human approval, which is not available in the current " +
            $"execution context. The '{Name}' tool was not executed.");
}
