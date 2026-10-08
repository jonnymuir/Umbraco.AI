using System.Diagnostics;
using Umbraco.AI.Core.Observability;

namespace Umbraco.AI.Core.Telemetry;

/// <summary>
/// Puts the running tracked call's <c>umbraco.ai.*</c> tags on its gen_ai span. Called by the OpenTelemetry
/// middleware for each capability, inside the span it starts (#562).
/// </summary>
internal static class AIOperationActivityTags
{
    /// <summary>
    /// Tags <paramref name="activity"/> with the tags of the tracked call whose work is running, when it is a
    /// span from <see cref="AITelemetry.SourceName"/>. A nested call's span gets that call's own tags, since
    /// its scope is the one entered around its work.
    /// </summary>
    public static void Apply(Activity? activity)
    {
        if (activity?.Source.Name != AITelemetry.SourceName || AIOperationScope.Current is not { } scope)
        {
            return;
        }

        foreach (var (tag, value) in scope.ActivityTags)
        {
            activity.SetTag(tag, value);
        }
    }
}
