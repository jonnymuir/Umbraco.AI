using Umbraco.AI.Core.Analytics.Usage;
using Umbraco.AI.Core.Observability;
using Umbraco.Cms.Core.Security;

namespace Umbraco.AI.Core.Telemetry;

/// <summary>
/// Adds Umbraco AI context (profile, user, entity, feature) to the tags for a tracked call's own gen_ai span,
/// so traces can be filtered by them. The audit entry's ID is added by
/// <see cref="AuditLog.AIAuditOperationRecorder"/>, which owns that entry.
/// </summary>
/// <remarks>
/// The span doesn't exist yet when the call starts; <see cref="AIOperationActivityTags"/> puts the tags on it.
/// </remarks>
internal sealed class AITraceOperationRecorder : IAIOperationRecorder
{
    private readonly IBackOfficeSecurityAccessor _securityAccessor;

    public AITraceOperationRecorder(IBackOfficeSecurityAccessor securityAccessor)
        => _securityAccessor = securityAccessor;

    public ValueTask<IAIOperationRecording?> BeginAsync(AIOperationStart start, CancellationToken cancellationToken)
    {
        if (start.Identity is { } identity)
        {
            AddTags(start.ActivityTags, identity);
        }

        // Nothing to record at the end.
        return ValueTask.FromResult<IAIOperationRecording?>(null);
    }

    private void AddTags(Dictionary<string, string> tags, AIUsageContext identity)
    {
        // Missing IDs are read from the runtime context as Guid.Empty, so treat that as absent.
        if (identity.ProfileId is { } profileId && profileId != Guid.Empty)
        {
            tags[AITelemetry.Tags.ProfileId] = profileId.ToString();
        }

        AddIfPresent(tags, AITelemetry.Tags.ProfileAlias, identity.ProfileAlias);
        AddIfPresent(tags, AITelemetry.Tags.UserId, _securityAccessor.BackOfficeSecurity?.CurrentUser?.Key.ToString());
        AddIfPresent(tags, AITelemetry.Tags.EntityId, identity.EntityId);
        AddIfPresent(tags, AITelemetry.Tags.EntityType, identity.EntityType);
        AddIfPresent(tags, AITelemetry.Tags.FeatureType, identity.FeatureType);

        if (identity.FeatureId is { } featureId && featureId != Guid.Empty)
        {
            tags[AITelemetry.Tags.FeatureId] = featureId.ToString();
        }
    }

    private static void AddIfPresent(Dictionary<string, string> tags, string tag, string? value)
    {
        if (value is not null)
        {
            tags[tag] = value;
        }
    }
}
