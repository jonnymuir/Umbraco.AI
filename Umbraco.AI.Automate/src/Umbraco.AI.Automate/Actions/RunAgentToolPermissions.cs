using Umbraco.AI.Agent.Core.Agents;

namespace Umbraco.AI.Automate.Actions;

/// <summary>
/// Which tool calls a <see cref="RunAgentAction"/> step lets the agent make. Nobody is watching an
/// automation run, so these only ever narrow what the agent may do; none of them asks a human.
/// </summary>
/// <remarks>
/// Stored on <see cref="RunAgentSettings.ToolPermissions"/> by name, so new options can be added
/// without changing what existing automations have saved.
/// </remarks>
public enum RunAgentToolPermissions
{
    /// <summary>
    /// The agent can look things up but can't make any changes: every destructive tool is denied.
    /// </summary>
    ReadOnly,

    /// <summary>
    /// The agent can also make changes that don't need approval (e.g. editing drafts). Changes that
    /// need approval (e.g. publishing or deleting) are denied.
    /// </summary>
    NoApprovalRequired,
}

/// <summary>
/// Maps <see cref="RunAgentToolPermissions"/> onto the agent's <see cref="AIApprovalPolicy"/>.
/// </summary>
internal static class RunAgentToolPermissionsExtensions
{
    /// <summary>
    /// Resolves a stored <see cref="RunAgentSettings.ToolPermissions"/> value to an approval policy.
    /// Anything missing or unrecognised falls back to <see cref="RunAgentToolPermissions.ReadOnly"/>.
    /// </summary>
    public static AIApprovalPolicy ToApprovalPolicy(string? toolPermissions)
        => Enum.TryParse<RunAgentToolPermissions>(toolPermissions, ignoreCase: true, out var parsed)
            && Enum.IsDefined(parsed)
            ? parsed.ToApprovalPolicy()
            : AIApprovalPolicy.DenyAll;

    /// <summary>
    /// Maps a tool permissions option to the approval policy that enforces it.
    /// </summary>
    public static AIApprovalPolicy ToApprovalPolicy(this RunAgentToolPermissions toolPermissions)
        => toolPermissions switch
        {
            RunAgentToolPermissions.NoApprovalRequired => AIApprovalPolicy.DenyApprovalRequired,
            _ => AIApprovalPolicy.DenyAll,
        };
}
