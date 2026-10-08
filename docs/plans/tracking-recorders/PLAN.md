# Plan

One PR per task, v18 first, ported to v17 once the v18 PR is approved. Refactor PRs change
structure only; behaviour changes are separate commits and called out in the PR.

Tracked on [#528](https://github.com/umbraco/Umbraco.AI/issues/528).

## Done

- [x] **T0. Fix audit parent links** (#529): #532 (v18), #533 (v17).
- [x] **Dashboard gap** (#530, not part of the refactor): #534 (v18), #535 (v17).

## Tasks

- [ ] **T1. Recorder contracts, analytics and test usage as recorders.**
  - First commit: fill the test gaps below, against today's code, so they pin current behaviour.
  - Add `IAIOperationRecorder`, `IAIOperationRecording`, `AIOperationStart`,
    `AIOperationOutcome`. The tracker builds start and outcome once and calls an ordered list of
    recorders.
  - Move `CollectUsage` into `AITestUsageOperationRecorder` and `RecordUsageAsync` into
    `AIAnalyticsOperationRecorder`. Remove `AIUsageObservation` and `ReportUsage`.
  - Remove the second analytics on/off check in `AIUsageRecordingService.QueueRecordUsageAsync`,
    and its unused synchronous `RecordUsageAsync`.
  - Behaviour change, own commit: end writes use `CancellationToken.None` (decision 2). Fixes
    #531.

- [ ] **T2. Audit and tracing as recorders.**
  - `AIAuditOperationRecorder` owns entry creation, the parent lookup (one place, not two),
    `EnterScope`, and start/end status. `AIOperationScope` stops reaching into
    `tracker.AuditLogService`; the tracker stops referencing `AIAuditScope`.
  - `AITraceOperationRecorder` takes the `AIActivityEnricher` call and the audit entry's
    `TraceId` assignment.
  - Remove the unused synchronous `AIAuditLogService` write methods.
  - Behaviour change, own commit: recorder failures are isolated (decision 1).

- [ ] **T3. One outcome for every recorder.**
  - `AIOperationStatus.Blocked` when the call fails with `AIGuardrailBlockedException` (the same
    check `AIAuditLogService` makes today), shared by audit and analytics (decision 5).
  - Same provider-error check for streaming and non-streaming chat (non-streaming never checks
    for streamed error content today).

- [ ] **T4. Smaller duplicates from #528** (can run in any order after T2): five copies of
  `PopulateProfileMetadata`; the error category worked out twice; two context extractors with
  different field sets; out-of-date docs on `RecordUsageWhenEmpty` and `AITrackedOperationResult`.

## Test gaps to fill in T1

Existing coverage is strong: about 60 tests across `AIOperationTrackerTests`,
`AIOperationTrackerUsageCollectionTests`, `AIOperationTrackerAnalyticsIdentityTests`,
`AIUsageCollectorTests` and the chat, embedding and speech-to-text tracking client tests. Not
covered today:

- No runtime context: no audit entry, no usage record, the call still runs.
- Activity tags are set at the start of a call, with and without auditing.
- The audit entry's `TraceId` comes from `Activity.Current`.
- The audit start is queued before the operation runs.
- A failed call's usage record carries the exception message.
- Image generation tracking: same shape as the other clients (check
  `ImageGeneration/` tests first; add only what's missing).
