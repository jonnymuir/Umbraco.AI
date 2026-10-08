namespace Umbraco.AI.Agent.Core.Agents.Selection;

/// <summary>
/// The outcome of agent selection: which agent was picked, which selector picked it, and why.
/// </summary>
/// <param name="Agent">The selected agent. Always one of the request's <c>CandidateAgents</c>.</param>
/// <param name="SelectorId">
/// An identifier for the selector (or built-in rule) that produced this result, e.g. <c>"llm"</c>,
/// <c>"sticky"</c>, <c>"only-candidate"</c> or <c>"fallback"</c>. Stored word-for-word in the agent
/// run's audit log metadata - keep it short and free of personal or sensitive data.
/// </param>
/// <param name="Reason">
/// An optional, human-readable explanation for the pick. Stored word-for-word in the agent run's
/// audit log metadata - keep it short and free of personal or sensitive data.
/// </param>
public sealed record AIAgentSelectionResult(AIAgent Agent, string SelectorId, string? Reason);
