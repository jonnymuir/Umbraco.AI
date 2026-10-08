using System.Diagnostics;
using Umbraco.AI.Core.Analytics.Usage;
using Umbraco.AI.Core.Observability;
using Umbraco.Cms.Core.Security;

namespace Umbraco.AI.Core.Telemetry;

/// <summary>
/// Tags <see cref="Activity.Current"/> with Umbraco AI context (profile, user, entity, feature) when a
/// tracked call starts, so traces can be filtered by them. The audit entry's ID is tagged by
/// <see cref="AuditLog.AIAuditOperationRecorder"/>, which owns that entry.
/// </summary>
internal sealed class AITraceOperationRecorder : IAIOperationRecorder
{
    private readonly IBackOfficeSecurityAccessor _securityAccessor;

    public AITraceOperationRecorder(IBackOfficeSecurityAccessor securityAccessor)
        => _securityAccessor = securityAccessor;

    public ValueTask<IAIOperationRecording?> BeginAsync(AIOperationStart start, CancellationToken cancellationToken)
    {
        if (Activity.Current is { } activity && start.Identity is { } identity)
        {
            Tag(activity, identity);
        }

        // Nothing to record at the end.
        return ValueTask.FromResult<IAIOperationRecording?>(null);
    }

    private void Tag(Activity activity, AIUsageContext identity)
    {
        // Missing IDs are read from the runtime context as Guid.Empty, so treat that as absent.
        if (identity.ProfileId is { } profileId && profileId != Guid.Empty)
        {
            activity.SetTag(AITelemetry.Tags.ProfileId, profileId.ToString());
        }

        SetIfPresent(activity, AITelemetry.Tags.ProfileAlias, identity.ProfileAlias);
        SetIfPresent(activity, AITelemetry.Tags.UserId, _securityAccessor.BackOfficeSecurity?.CurrentUser?.Key.ToString());
        SetIfPresent(activity, AITelemetry.Tags.EntityId, identity.EntityId);
        SetIfPresent(activity, AITelemetry.Tags.EntityType, identity.EntityType);
        SetIfPresent(activity, AITelemetry.Tags.FeatureType, identity.FeatureType);

        if (identity.FeatureId is { } featureId && featureId != Guid.Empty)
        {
            activity.SetTag(AITelemetry.Tags.FeatureId, featureId.ToString());
        }
    }

    private static void SetIfPresent(Activity activity, string tag, string? value)
    {
        if (value is not null)
        {
            activity.SetTag(tag, value);
        }
    }
}
