using Umbraco.AI.Agent.Core.Surfaces;

namespace Umbraco.AI.Agent.Core.Agents;

/// <summary>
/// Validates agent scope rules, following the same pattern as Prompt scope validation.
/// </summary>
/// <remarks>
/// This validator is surface-aware: it only checks scope dimensions that the requesting
/// surface declares it cares about via <see cref="IAIAgentSurface.SupportedScopeDimensions"/>.
/// </remarks>
public class AIAgentScopeValidator
{
    /// <summary>
    /// Checks if an agent can run on a specific surface in the given context: it must be active, opted
    /// in to the surface via <see cref="AIAgent.SurfaceIds"/>, and pass the surface's scope rules
    /// (<see cref="IsAgentAvailable"/>).
    /// </summary>
    /// <remarks>
    /// This is the one rule for "can this agent run here". Auto-selection, explicitly named agents and
    /// any save-time checks all go through it, so the rule cannot drift between those paths.
    /// </remarks>
    /// <param name="agent">The agent to check.</param>
    /// <param name="surfaceId">The surface the agent is being run on.</param>
    /// <param name="context">The current context.</param>
    /// <param name="surfaces">The registered surfaces, used to resolve the surface's scope dimensions.</param>
    /// <returns>True if the agent is available on the surface, false otherwise.</returns>
    internal bool IsAgentAvailableOnSurface(
        AIAgent agent,
        string surfaceId,
        AgentAvailabilityContext context,
        AIAgentSurfaceCollection surfaces)
    {
        if (!agent.IsActive)
        {
            return false;
        }

        // An empty SurfaceIds list means the agent is on no surface at all.
        if (!agent.SurfaceIds.Contains(surfaceId, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        return IsAgentAvailable(agent, context, surfaces.GetById(surfaceId));
    }

    /// <summary>
    /// Checks if an agent is available in the given context for a specific surface.
    /// </summary>
    /// <param name="agent">The agent to check.</param>
    /// <param name="context">The current context.</param>
    /// <param name="surface">The surface requesting the agent (determines which dimensions to check).</param>
    /// <returns>True if agent is available, false otherwise.</returns>
    public bool IsAgentAvailable(AIAgent agent, AgentAvailabilityContext context, IAIAgentSurface? surface)
    {
        // No scope = available everywhere (backwards compatible)
        if (agent.Scope == null)
        {
            return true;
        }

        // Get the dimensions this surface cares about
        var relevantDimensions = surface?.SupportedScopeDimensions ?? Array.Empty<string>();

        // A broad/unscoped surface (no relevant dimensions — e.g. copilot-workspace, or a null
        // surface) cannot meaningfully apply dimension-based allow/deny rules: with no dimensions to
        // check, every rule matches vacuously (IsRuleMatched falls through to true), which would deny
        // every deny-scoped agent everywhere. Treat the agent as available; availability on such a
        // surface is governed solely by SurfaceIds opt-in. This matches the IAIAgentSurface contract
        // ("empty list means the surface doesn't perform scope-based filtering").
        if (relevantDimensions.Count == 0)
        {
            return true;
        }

        // Check deny rules first (they take precedence)
        if (IsAnyRuleMatched(agent.Scope.DenyRules, context, relevantDimensions))
        {
            return false;
        }

        // No allow rules = available everywhere (unless denied above)
        if (agent.Scope.AllowRules.Count == 0)
        {
            return true;
        }

        // Check if any allow rule matches
        return IsAnyRuleMatched(agent.Scope.AllowRules, context, relevantDimensions);
    }

    /// <summary>
    /// Checks if any rule in the list matches the current context.
    /// OR logic between rules.
    /// </summary>
    /// <param name="rules">The rules to check.</param>
    /// <param name="context">The current context.</param>
    /// <param name="relevantDimensions">The dimensions the requesting surface cares about.</param>
    private bool IsAnyRuleMatched(
        IReadOnlyList<AIAgentScopeRule> rules,
        AgentAvailabilityContext context,
        IReadOnlyList<string> relevantDimensions)
    {
        // No rules = no match
        if (rules.Count == 0)
        {
            return false;
        }

        // Check if any rule matches (OR logic)
        return rules.Any(rule => IsRuleMatched(rule, context, relevantDimensions));
    }

    /// <summary>
    /// Checks if a single rule matches the current context.
    /// AND logic between properties, OR logic within arrays.
    /// Only checks dimensions that the surface cares about.
    /// </summary>
    /// <param name="rule">The rule to check.</param>
    /// <param name="context">The current context.</param>
    /// <param name="relevantDimensions">The dimensions the requesting surface cares about.</param>
    private bool IsRuleMatched(
        AIAgentScopeRule rule,
        AgentAvailabilityContext context,
        IReadOnlyList<string> relevantDimensions)
    {
        // Check section (if specified AND surface cares about it)
        if (rule.Sections?.Count > 0 &&
            relevantDimensions.Contains("section", StringComparer.OrdinalIgnoreCase))
        {
            // No current section = doesn't match
            if (string.IsNullOrEmpty(context.Section))
            {
                return false;
            }

            // Check if current section is in the list (OR logic)
            if (!rule.Sections.Contains(context.Section, StringComparer.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        // Check entity type (if specified AND surface cares about it)
        if (rule.EntityTypes?.Count > 0 &&
            relevantDimensions.Contains("entityType", StringComparer.OrdinalIgnoreCase))
        {
            // No current entity type = doesn't match
            if (string.IsNullOrEmpty(context.EntityType))
            {
                return false;
            }

            // Check if current entity type is in the list (OR logic)
            if (!rule.EntityTypes.Contains(context.EntityType, StringComparer.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        // All relevant specified constraints satisfied (AND logic)
        return true;
    }
}
