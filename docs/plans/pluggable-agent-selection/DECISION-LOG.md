# Decision Log

- **01-10-2026** - Profile routing split out of this feature. It picks the model, not the agent,
  and sits at a different layer (`IAIChatClientFactory`). Kept as Part 2 of
  `docs/ideas/pluggable-routing.md`.
- **01-10-2026** - Audience is developers only (code extension point, no backoffice UI). Smallest
  scope that meets the customer/partner ask for their own business rules.
- **01-10-2026** - Stickiness is decided per rule: selection is given the previous pick. The
  default keeps today's re-pick-every-turn behaviour, so nothing changes for existing sites.
- **01-10-2026** - MVP = extension point + LLM classifier as the default + 1-2 simple built-in
  rules, to prove the extension point is enough for real deterministic rules.
- **01-10-2026** - Record the selection reason in the `agent_selected` event and the audit log.
  Cheap to add, and it fixes today's silent fallback to the first agent.
- **01-10-2026** - Ship on v18 first, then backport to v17 (both lines in active support).
- **01-10-2026** - No concrete customer rules available. Selectors get the full context (raw
  request context items, availability context, user groups, conversation), not a curated subset.
- **01-10-2026** - Ordered collection builder (`AIAgentSelectors()`), modelled on guardrail
  resolvers. Rejected single DI-replaceable selector (can't compose) and notifications (no ordering).
- **01-10-2026** - Chain lives in a new `IAIAgentSelectionService`. `SelectAgentForPromptAsync`
  proxies to it, obsolete for v20. Keeps `AIAgentService` from growing.
- **01-10-2026** - Previous pick sent by the browser in AG-UI `forwardedProps.previousAgentId`,
  treated as an untrusted hint. The server stores no conversations on this line. Context items rejected
  (can reach the model's prompt).
- **01-10-2026** - Built-ins: `LLMAgentSelector` (default, unchanged prompt/input) + opt-in
  `StickyAgentSelector`. appsettings rule map and @mention selectors cut.
- **01-10-2026** - Throwing or out-of-candidate selector results are skipped, not fatal. Safe
  because scope filtering runs first.
- **01-10-2026** - Selection reaches the audit log via typed `AIAgentExecutionOptions.Selection`
  -> runtime context `LogKeys` -> `AIAuditLog.Metadata`. No schema change.
- **01-10-2026** - Add `AIAgentSelectedNotification` (observe only) after every `auto` pick.
  Reverses the earlier "no notifications" cut. Rejected a cancelable `Selecting` notification:
  it would be a second way to change the pick, with unclear precedence against selectors.
- **01-10-2026** - Keep orchestration in `IAIAgentSelectionService`, not a method on
  `AIAgentSelectorCollection`. Repo precedent: collection methods are dependency-free lookups or
  loops (`AIEntityAdapterCollection.GetAdapter`, `AIRuntimeContextContributorCollection.Populate`).
  Orchestration that needs other services lives in a service (`AIGuardrailResolutionService` over
  `AIGuardrailResolverCollection`). Selection needs agent lookup, the scope validator, user groups,
  a logger, and `IEventAggregator`.
- **01-10-2026** - Plan: `abortRun()` keeps the previous pick; only `resetConversation()` clears it.
  A cancelled turn is still the same conversation, so sticky shouldn't forget it.
- **01-10-2026** - Plan: the notification publish (T8) is split from the selection service (T7), so the
  chain logic gets reviewed on its own before the side effect is layered on.
- **01-10-2026** - Plan: frontend transport (T2) has no dependencies and runs in group A, because its
  contract is fixed by SPEC.md. It is only proven live in T12, after the backend wire task (T11).
- **01-10-2026** - Plan: v17 backport and the Umbraco.Docs page are post-merge follow-ups, not loop tasks.
- **01-10-2026** - Pending specs are gated with `#if PENDING_AGENT_SELECTION_SPECS` per file (never defined), not `[Fact(Skip=...)]`: the types they use don't exist yet, and Skip can't help code that doesn't compile. Each builder deletes the `#if`/`#endif` in the same commit as the code that makes that file pass. Shared construction lives in `AgentSelectionTestHarness.cs` so signature changes are fixed in one place.
- **01-10-2026** - Build T1: `AIAgentSelectionRequest` docs mention `IAIAgentSelectionService` in plain
  text, not a `<see cref>`, because that type doesn't exist until T7 (it would be a broken-link warning).
- **01-10-2026** - Build: per-task demo-site smoke is skipped for type-only tasks (T1, T3). Their DI
  wiring is proven live in the T11 wire task instead of booting the site for code with no behaviour.
- **01-10-2026** - Build T2: `previousAgentId` is an optional 5th positional parameter on
  `UaiAgentClient.sendMessage` (matching `resume`), not an options bag. The only caller is
  `run.controller.ts`.
- **01-10-2026** - Build T2 note for T12: a persisted Copilot Workspace conversation reopened from
  history has no saved pick, so its first turn sends no `previousAgentId`. Accepted for now.
- **01-10-2026** - Build T4: `LLMAgentSelector` returns `Reason: null` (no reason string is specified for
  the LLM pick). A public `SelectorId` const on built-in selectors was suggested by review and not done
  (out of task scope); revisit at PR time.
- **01-10-2026** - Build: the gated spec harness is one `#if` block that also needs T6/T9/T10 types, so no
  spec can switch on before T10. T7 splits the harness per area so specs switch on with their own task.
- **01-10-2026** - Build T6: a null reason means the SelectionReason key is left out of LogKeys entirely
  (not written as empty). `SelectorId`/`Reason` docs warn they are stored word-for-word in audit
  metadata, so selectors must keep them short and free of personal or sensitive data.
- **01-10-2026** - Build T6: specs needing only today's types moved into an un-gated
  `AgentSelectionTestBuilders.cs` (`AgentServiceAuditHarness` uses today's real `AIAgentService` ctor).
  T7 must merge its duplicate `CreateAgent`/`EmptyEventStream` with the gated harness to avoid CS0121.
- **01-10-2026** - Build T7: `AIAgentSelectionService`'s constructor does **not** take `IEventAggregator`.
  "No notification yet" means no publish call exists, so an unused dependency would just be dead
  weight; T8 adds the parameter (and DI resolves it for free - no registration change needed) when it
  adds the publish call.
- **01-10-2026** - Build T7: `OnlyCandidateSelectorId`/`FallbackSelectorId` are public `const string`s on
  the internal `AIAgentSelectionService` itself, not a separate constants class - nothing outside this
  assembly (and its `InternalsVisibleTo` test project) needs them yet, and `LLMAgentSelector`/
  `StickyAgentSelector` already use bare literals (`"llm"`, `"sticky"`) for the same kind of ID.
- **01-10-2026** - Build T7: request-building order follows ARCHITECTURE.md literally - user groups and
  `PreviousAgent` are resolved, and the `AIAgentSelectionRequest` is built, *before* the one-candidate
  check, not after. The single-candidate short-circuit only skips the selector loop, not request
  construction, because T8 needs `Request` on the notification for that case too (never null).
- **01-10-2026** - Build T7: selector-chain exception handling uses `catch (Exception ex) when (ex is not
  OperationCanceledException)` rather than a separate `catch (OperationCanceledException) { throw; }`
  block - same effect (cancellation always propagates unobserved), fewer catch blocks, and it reads as
  "skip anything that isn't a cancellation" rather than "rethrow, then catch the rest".
- **01-10-2026** - Build T7: split the gated `AgentSelectionTestHarness.cs` in two. Everything that
  compiles today (the selection types, `AIAgentSelectionService`, `LLMAgentSelector`,
  `StickyAgentSelector`) moved into the un-gated `AgentSelectionTestBuilders.cs`, merged with its
  existing `AgentServiceAuditHarness`/`EmptyEventStream` so there is one `CreateAgent` (the richer
  5-parameter version, with `Scope`/`SurfaceIds`/`Description`) and one `EmptyEventStream`. What's left
  gated is only `AgentServiceHarness` (still constructs `AIAgentService` with an
  `IAIAgentSelectionService` constructor parameter that the real ctor deliberately doesn't have - T9's
  job to fix, per the circular-DI constraint) and `ControllerHarness`/`CreateRunRequest`/
  `ReadFirstEventValueAsync` (T10). `AgentSelectionChainTests.cs`, `LLMAgentSelectorTests.cs` and
  `StickyAgentSelectorTests.cs` un-gated and switched their `using static` to
  `AgentSelectionTestBuilders`. `AgentSelectionRequestTests.cs` (one scenario reads
  `PublishedNotifications`) and `AgentSelectedNotificationTests.cs` stay gated for T8;
  `AgentSelectionRegistrationTests.cs` and `StreamAgentAGUIControllerAutoSelectionTests.cs` stay gated
  for T9/T10 untouched.
- **01-10-2026** - Build T7: `IAIAgentSelectionService` registration pulled forward from T9 into T7 (no DI
  cycle; `AIAgentService` does not take it in its ctor, the T9 obsolete proxy resolves it via
  `StaticServiceProvider`). T9 text updated.
- **01-10-2026** - Build T7 review: the service returns the matched candidate instance
  (`result with { Agent = candidate }`), never the selector's own object, so a stale copy with a
  candidate's Id can't leak to callers, handlers or the event.
- **01-10-2026** - Build T7: an `OperationCanceledException` thrown without real cancellation (e.g. an
  HTTP timeout in a selector) propagates and fails the request, per SPEC guarantee 5 and today's
  behaviour. Flagged for the human; not changed.
- **01-10-2026** - Human decision: built-in selector IDs live in a public static `AIAgentSelectorIds`
  class (`Llm`, `Sticky`, `OnlyCandidate`, `Fallback`), added in T8. Handlers and audit queries use the
  constants instead of copying strings. Replaces the consts that sat on the internal service.
- **01-10-2026** - Build T9: `[Obsolete]` sits on the interface method only; the implementation uses
  `#pragma warning disable CS0618`, matching `AIChatService`/`AIEmbeddingService`. The controller call
  site is pragma-wrapped with a TODO that T10 must remove.
- **01-10-2026** - Build T9: a side effect of the proxy: the obsolete path now publishes
  `AIAgentSelectedNotification`, and a throwing classifier is logged and falls back instead of bubbling
  up. Both follow from SPEC; same agent is picked.
- **01-10-2026** - Build T9: tests that swap `StaticServiceProvider.Instance` run in a serialised xUnit
  collection (`StaticServiceProviderCollection`). First use of that pattern in this repo.
- **01-10-2026** - Build T10: the controller converts messages for selection with the same
  `IAGUIMessageConverter` the run uses (pure mapping, no file storage), so `auto` decodes base64
  attachments one extra time. A malformed attachment on an `auto` request now fails before streaming
  (500) instead of inside the run; it already failed before T10.
- **01-10-2026** - Build T10: on follow-up turns attachments arrive as a server URL + file ID, so selectors
  see `UriContent` links, not bytes. Meets SPEC guarantee 9; document for selector authors.
- **01-10-2026** - Build T10: the controller's two old public ctors are `[Obsolete]` (v20) and resolve
  `IAIAgentSelectionService`/`IAGUIMessageConverter` via `StaticServiceProvider`; DI uses the new ctor.
- **01-10-2026** - Smoke T11: the live `agent_selected` event omits the `reason` key when the reason is null
  (the AG-UI serializer skips nulls), instead of sending `"reason": null` as SPEC shows. The frontend
  type has `reason` optional, so nothing breaks. Accepted; SPEC example is loose here.
- **01-10-2026** - Smoke T12: Copilot has no "new conversation" button in the sidebar; S5 AC4 was checked by
  switching the agent away from Auto and back, which runs the same `resetConversation()`.
- **01-10-2026** - Human decision (PR #463): a selector that throws `OperationCanceledException` while the
  request is NOT cancelled (e.g. an HTTP timeout) is now skipped like any other failure; only a real
  request cancellation propagates. SPEC guarantee 5 updated. Supersedes the T7 entry that left it propagating.
- **01-10-2026** - Human decision (PR #463): selector `Reason`/`SelectorId` stay in `AIAuditLog.Metadata`
  without redaction; the XML-doc warning to keep them free of personal data is enough.
- **01-10-2026** - Human decision: Copilot Workspace (now on both `v18/dev` and `v17/dev`) is in scope.
  Its stream endpoint was still on the obsolete `SelectAgentForPromptAsync`. Workspace's previous pick
  comes from a new nullable `AgentId` on each assistant message, not a `LastAgentId` on the conversation.
  Reopened chats show agent names from it. Added S7/S8 and T13-T17.
- **01-10-2026** - Human decision: pickers seeing the persisted conversation history in Workspace is a
  follow-up. For now Workspace selectors get only this turn's messages.
- **01-10-2026** - Plan: T13 merges `origin/v18/dev` rather than rebasing, because PR #463 is pushed
  and force-push is off-limits.
- **01-10-2026** - Specs for S7/S8 (pending, per-task gates): `Conversations/ConversationMessageAgentAttributionTests.cs`
  (T14, 4), `Conversations/AIConversationServiceLastAssistantAgentTests.cs` (T14, 3),
  `Api/Management/Conversations/MessageAgentIdMappingTests.cs` (T16, 2),
  `Api/Management/Stream/StreamConversationAutoSelectionTests.cs` (T15, 12; own harness), and vitest
  `conversation/message-mapper.agent-name.test.ts` (T16, 3, `describe.skip`). The repository's newest-assistant
  query has no unit spec (EF repo tests need the NUnit Umbraco harness); T17 checks it live.
- **01-10-2026** - Build T14: `IAIConversationService` (public; Workspace 18.0.0 shipped) gains
  `GetLastAssistantAgentIdAsync`. This breaks only third-party implementers; it matches the earlier
  `TruncateAfterLastUserMessageAsync` addition. Pending human confirmation; flagged in the PR.
- **01-10-2026** - Build T14: the provider reads the agent with `TryGetValue<Guid>`. `GetValue<Guid>` returns
  `Guid.Empty` for a missing key, which was stamped as a real agent ID (caught by a fail-first spec).
- **01-10-2026** - Build T15: Workspace passes `ContextItems = []` (its client sends no request context;
  grounding goes through runtime `AdditionalProperties`). The `agent_selected` helper is a private copy
  of the plain endpoint's, not a shared public helper. Approval-resume turns re-run selection; T17 checks
  they keep the same agent.
- **01-10-2026** - Build T16: regenerating the Workspace client also picked up older drift (https base URL,
  removed `GetFile*`/`StreamService.getFile`). Kept, as its own chore commit; it matches the other
  packages' checked-in clients and nothing calls the removed types.
- **01-10-2026** - Build T16: the Workspace chat context itself can't be unit-tested yet (the vitest alias for
  `@umbraco-ai/agent` resolves `src/index.ts`, which lacks `UaiAgentRepository`). The ordering is covered by a
  strategy-level spec plus T17's live first-open check. Fixing the alias is a separate follow-up.
- **02-10-2026** - Backport: v17 draft PR #468. v17 needed CMS 17's `IEfCoreScope<T>` spelling, regenerated
  Workspace migrations, and a v17 client regen with its pinned `@hey-api/openapi-ts` 0.96.0.
