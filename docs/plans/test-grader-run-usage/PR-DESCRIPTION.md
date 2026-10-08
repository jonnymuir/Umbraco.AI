Closes #516 | [Plan folder](https://github.com/umbraco/Umbraco.AI/tree/v18/feature/test-grader-run-usage-516/docs/plans/test-grader-run-usage)

## Why the change

Test graders never saw how many tokens a run used or which model produced it, so cost, token-budget and CO2e graders were impossible; this fills `AITestOutcome.TokenUsage` with the run's summed usage plus a per-model breakdown.

## Special things to note

- **Behaviour change in usage analytics (bug fix):** analytics used to read provider, model, profile and feature from the shared runtime context when a call *finished*. Nested calls rewrite that context partway through: the LLM guardrail judge (`AIGuardrailChatMiddleware` → `IAIChatService`) and the semantic search tool's embedding call. So the outer chat call's tokens could be logged against the judge or embedding model. `BeginAsync` now captures the usage context once, as the audit log already did, and both analytics and test collection use that single capture. Dashboard numbers may shift between models compared with before. Not live-checked on the demo site; covered by `AIOperationTrackerAnalyticsIdentityTests`.
- **Needs a decision:** Prompt tests now report usage in two places. `PromptTestFeature` still writes its single call's usage into `transcript.FinalOutput.Usage`, while `outcome.TokenUsage` sums every tracked call in the run (guardrail judges and nested tool calls included). The two numbers can differ. Keep both, drop or relabel the transcript copy, or just document which one to trust?
- Grader-made calls (for example an LLM-judge grader) are deliberately **not** counted. The collection scope closes before grading (`AITestRunner.cs:270`). The live check confirmed this: the judge's 439/102 tokens were left out of the agent run's totals.
- A call that reports no usage, or a `UsageDetails` with all three counts null, adds to `CallCount` and `UnreportedCallCount` and not to the totals, so "unknown" is never shown as zero (`AIUsageCollector.cs:41`).
- All tracked capabilities are counted, not only chat. Embedding, image and speech calls get their own `Models` entry tagged with `Capability`. The model ID comes from the profile (same source as the analytics dashboard), not from `ChatResponse.ModelId`.
- No migration. The new fields ride in the existing `OutcomeTokenUsageJson` column. Runs saved before this change load with an empty `Models` list and zero counts.
- Public API changes are additive only. `IAITestGrader`, `AITestGraderBase` and the built-in graders are untouched, and graders read the data from the `outcome` they already receive. The collector types are `internal`.
- Errored runs are unchanged (status `Error`, no outcome, no usage).
- A stream that its consumer abandons partway is not collected. This matches analytics today. Test features always read streams to the end.
- Live-verified on the demo site with openai/gpt-4o. A prompt test reported 1 call with 473/34 tokens and an agent test 1 call with 5515/82, both matching the audit log. A failing prompt (OpenAI 400) reported 1 call, 1 unreported.
- v17 backport still to do (separate PR).

## Change outline

The contract graders see grows, with additions only:

```diff
 AITestOutcome
 └── TokenUsage : AITestTokenUsage?            // was always null, now populated
     ├── InputTokens / OutputTokens / TotalTokens
+    ├── CallCount
+    ├── UnreportedCallCount
+    └── Models : List<AITestModelTokenUsage>
+        ├── Capability, ProviderId?, ModelId?, ProfileId?, ProfileAlias?
+        ├── InputTokens / OutputTokens / TotalTokens
+        └── CallCount / UnreportedCallCount
```

The runner opens an ambient collection scope (`AsyncLocal`) around feature execution only:

```diff
 AITestRunner.ExecuteSingleRunAsync
-    transcript = await testFeature.ExecuteAsync(...)
+    using (var usageScope = AIUsageCollectionScope.Begin())
+    {
+        transcript = await testFeature.ExecuteAsync(...)
+        usage = usageScope.Collector.GetSnapshot()
+    }                                            // closed before grading
     outcome = new AITestOutcome {
-        TokenUsage = null
+        TokenUsage = MapTokenUsage(usage)        // null when no tracked calls
     }
     GradeOutcomeAsync(transcript, outcome, ...)
```

Every tracked AI call feeds the open scope. It needs no changes to Prompt, Agent or third-party test features:

```
ScopedProfileChatClient            (writes profile/provider/model to runtime context)
└── AITrackingChatClient
    └── AIOperationTracker.BeginAsync   + captures AIUsageContext once here
        └── AIOperationScope.CompleteAsync / FailAsync
            ├── + tracker.CollectUsage(descriptor, usageContext, usage)
            │       → AIUsageCollectionScope.Current?.RecordCall(...)   (independent of analytics)
            └──   RecordUsageAsync(descriptor, usageContext, ...)
-                     (was: read the live runtime context at completion)
```

New internal types in `Umbraco.AI.Core/Observability/`:

```diff
+AIUsageCollectionScope.cs       // AsyncLocal ambient scope, restores parent on dispose
+AIUsageCollector.cs             // thread-safe, groups by capability/provider/model/profile
+AIUsageCollectorSnapshot.cs
+AIUsageCollectorModelEntry.cs
```

Management API adds the same fields to `TestTokenUsageResponseModel` (`callCount`, `unreportedCallCount`, `models[]`) and the TS client is regenerated. The run detail view already prints `outcome.tokenUsage` as JSON, so it needs no UI code change.

🤖 Generated with [Claude Code](https://claude.com/claude-code)
