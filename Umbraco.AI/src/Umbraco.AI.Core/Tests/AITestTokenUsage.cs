namespace Umbraco.AI.Core.Tests;

/// <summary>
/// Token usage statistics for a test execution.
/// </summary>
public sealed class AITestTokenUsage
{
    /// <summary>
    /// Number of input tokens consumed.
    /// </summary>
    public int InputTokens { get; set; }

    /// <summary>
    /// Number of output tokens generated.
    /// </summary>
    public int OutputTokens { get; set; }

    /// <summary>
    /// Total tokens (input + output).
    /// </summary>
    public int TotalTokens { get; set; }

    /// <summary>
    /// Number of tracked AI calls made during the run, including calls that reported no usage.
    /// </summary>
    public int CallCount { get; set; }

    /// <summary>
    /// Number of tracked calls that returned no usage details. When greater than zero, the token
    /// totals are a lower bound because those calls contributed nothing to them.
    /// </summary>
    public int UnreportedCallCount { get; set; }

    /// <summary>
    /// Breakdown of the usage, one entry per capability, provider, model, profile and feature.
    /// Graders can sum just the entries they care about (for example only the target prompt's own call,
    /// or only guardrail judge calls). The top-level totals cover every tracked call in the run.
    /// Never null; empty when no breakdown was recorded (for example, usage persisted before the breakdown existed).
    /// </summary>
    public List<AITestTokenUsageEntry> Breakdown { get; set; } = [];
}
