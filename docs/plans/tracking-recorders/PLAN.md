# Plan

One PR per task, v18 first, ported to v17 once the v18 PR is approved. Refactor PRs change
structure only; behaviour changes are separate commits and called out in the PR.

Tracked on [#528](https://github.com/umbraco/Umbraco.AI/issues/528).

## Done

- [x] **T0. Fix audit parent links** (#529): #532 (v18), #533 (v17).
- [x] **Dashboard gap** (#530, not part of the refactor): #534 (v18), #535 (v17).

## Tasks

- [x] **T1. Recorder contracts, analytics and test usage as recorders.** #537 (v18), #538 (v17).
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
  - `AIAuditOperationRecorder` owns entry creation, the parent lookup, `EnterScope`, and
    start/end status. `AIOperationScope` stops reaching into `tracker.AuditLogService`; the
    tracker stops referencing `AIAuditScope`. The service's own parent fallback in
    `QueueStartAuditLogAsync` stays: it is public behaviour (see decision log).
  - `AITraceOperationRecorder` replaces `AIActivityEnricher`. The audit recorder links entry and
    trace both ways (`TraceId` on the entry, audit ID tag on the Activity).
  - Mark the unused synchronous `AIAuditLogService` write methods obsolete (the interface is
    public, so they can't simply be removed).
  - Recorder failures are isolated (decision 1). The guard already exists from T1, so moving audit
    behind it is what changes the behaviour; a separate commit adds the test for it.

- [ ] **T3. One outcome for every recorder.**
  - `AIOperationStatus.Blocked` when the call fails with `AIGuardrailBlockedException` (the same
    check `AIAuditLogService` makes today), shared by audit and analytics (decision 5).
  - Same provider-error check for streaming and non-streaming chat (non-streaming never checks
    for streamed error content today).

- [ ] **T4. Smaller duplicates from #528** (can run in any order after T2): five copies of
  `PopulateProfileMetadata`; the error category worked out twice; two context extractors with
  different field sets; out-of-date docs on `RecordUsageWhenEmpty` and `AITrackedOperationResult`.

- [ ] **T5. Final review of what the refactor left behind** (last, after T1 to T4 have merged).
  Moving responsibilities out leaves some types thinner than their names and interfaces suggest.
  Look at each one and decide, in one PR, whether to keep it, merge it into its caller, or
  replace it with something shared:
  - Services reduced to "wrap a save in a background job and queue it":
    `AIUsageRecordingService` after T1, and the `Queue*` methods of `AIAuditLogService` after T2.
    If both end up the same shape, one shared internal helper for queued repository saves.
    Merging into the recorders is ruled out: a recorder must not reach a repository directly.
  - Names that no longer fit what a type does (e.g. `AIUsageRecordingService` only queues).
  - Members left without callers (e.g. `IAIUsageRecordRepository.GetLastRecordTimestampAsync`
    since #534, and anything T1 to T4 strand).
  - Doc comments and `docs/reference/` that still describe the old tracker.
  - The recorder contracts themselves: anything added "for later" that no recorder ended up using.

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
