using Microsoft.Extensions.Logging;
using Umbraco.AI.Core.Analytics.Usage;
using Umbraco.AI.Core.RuntimeContext;

namespace Umbraco.AI.Core.Observability;

/// <inheritdoc cref="IAIOperationTracker" />
internal sealed class AIOperationTracker : IAIOperationTracker
{
    private readonly IAIRuntimeContextAccessor _contextAccessor;
    private readonly IReadOnlyList<IAIOperationRecorder> _recorders;
    private readonly ILogger<AIOperationTracker> _logger;

    public AIOperationTracker(
        IAIRuntimeContextAccessor contextAccessor,
        IEnumerable<IAIOperationRecorder> recorders,
        ILogger<AIOperationTracker> logger)
    {
        _contextAccessor = contextAccessor;
        _recorders = recorders.ToList();
        _logger = logger;
    }

    public async Task<AITrackedOperationResult<TResult>> TrackAsync<TResult>(
        AIOperationDescriptor descriptor,
        Func<CancellationToken, Task<AITrackedOperationResult<TResult>>> operation,
        CancellationToken cancellationToken)
    {
        var scope = await BeginAsync(descriptor, cancellationToken);
        AITrackedOperationResult<TResult> result;
        try
        {
            using (scope.EnterScope())
            {
                result = await operation(cancellationToken);
            }
        }
        catch (Exception ex)
        {
            await scope.FailAsync(ex);
            throw;
        }

        if (result.Failure is { } failure)
        {
            await scope.FailAsync(failure, result.Usage);
        }
        else
        {
            await scope.CompleteAsync(result.Usage, result.ResponseData);
        }

        return result;
    }

    public async Task<AIOperationScope> BeginAsync(AIOperationDescriptor descriptor, CancellationToken cancellationToken)
    {
        var runtimeContext = _contextAccessor.Context;

        // Captured now, while the context still belongs to this call: nested AI calls (guardrail judge,
        // semantic search embeddings) overwrite these keys before this call completes, so recorders
        // never re-read the live context.
        var identity = runtimeContext is not null
            ? AIUsageContext.ExtractFromRuntimeContext(descriptor.Capability, runtimeContext)
            : null;

        var recordings = await BeginRecordingsAsync(
            new AIOperationStart(descriptor, identity, runtimeContext.GetLogValues()), cancellationToken);

        return new AIOperationScope(this, recordings);
    }

    private async Task<IReadOnlyList<IAIOperationRecording>> BeginRecordingsAsync(
        AIOperationStart start,
        CancellationToken cancellationToken)
    {
        var recordings = new List<IAIOperationRecording>(_recorders.Count);
        foreach (var recorder in _recorders)
        {
            try
            {
                if (await recorder.BeginAsync(start, cancellationToken) is { } recording)
                {
                    recordings.Add(recording);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{Recorder} failed to start recording {Capability}",
                    recorder.GetType().FullName, start.Descriptor.Capability);
            }
        }

        return recordings;
    }

    /// <summary>
    /// Hands a finished call's outcome to each recording, in recorder order. Never throws into the AI call.
    /// </summary>
    internal async Task EndRecordingsAsync(IReadOnlyList<IAIOperationRecording> recordings, AIOperationOutcome outcome)
    {
        foreach (var recording in recordings)
        {
            try
            {
                await recording.EndAsync(outcome);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{Recording} failed to record the end of an AI call", recording.GetType().FullName);
            }
        }
    }
}
