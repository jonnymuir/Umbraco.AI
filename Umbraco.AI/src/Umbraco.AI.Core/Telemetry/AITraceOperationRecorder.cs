using System.Diagnostics;
using Umbraco.AI.Core.Observability;
using Umbraco.AI.Core.RuntimeContext;
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
        if (Activity.Current is { } activity && start.RuntimeContext is { } runtimeContext)
        {
            Tag(activity, runtimeContext);
        }

        // Nothing to record at the end.
        return ValueTask.FromResult<IAIOperationRecording?>(null);
    }

    private void Tag(Activity activity, AIRuntimeContext runtimeContext)
    {
        var profileId = runtimeContext.GetValue<Guid>(Constants.ContextKeys.ProfileId);
        if (profileId != default)
        {
            activity.SetTag(AITelemetry.Tags.ProfileId, profileId.ToString());
        }

        SetIfPresent(activity, AITelemetry.Tags.ProfileAlias, runtimeContext.GetValue<string>(Constants.ContextKeys.ProfileAlias));
        SetIfPresent(activity, AITelemetry.Tags.UserId, _securityAccessor.BackOfficeSecurity?.CurrentUser?.Key.ToString());
        SetIfPresent(activity, AITelemetry.Tags.EntityId, runtimeContext.GetValue<string>(Constants.ContextKeys.EntityId));
        SetIfPresent(activity, AITelemetry.Tags.EntityType, runtimeContext.GetValue<string>(Constants.ContextKeys.EntityType));
        SetIfPresent(activity, AITelemetry.Tags.FeatureType, runtimeContext.GetValue<string>(Constants.ContextKeys.FeatureType));

        var featureId = runtimeContext.GetValue<Guid>(Constants.ContextKeys.FeatureId);
        if (featureId != default)
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
