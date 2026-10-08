# Spec

## Management API surface

No new routes or DTOs. One existing endpoint changes behaviour when the agent is `auto`.

### `POST .../{agentIdOrAlias}/stream-agui` with `auto` (`StreamAgentAGUIController.StreamAgentAGUI`)

**Request** - unchanged shape (`AGUIRunRequest`). New optional read:

```json
{
  "forwardedProps": { "previousAgentId": "<guid>" }
}
```

Guarantees:

1. **No custom selectors registered, no `previousAgentId`:** the selected agent is the same one
   today's code picks for the same request.
2. **Scope is never bypassed.** The selected agent is always active and passes
   `AIAgentScopeValidator.IsAgentAvailable` for the request's surface and context, whatever any
   selector returns.
3. **Order.** Selectors run in `AIAgentSelectorCollection` order. The first non-null result that
   is a candidate wins. Later selectors don't run.
4. **Invalid selector result.** A selector returning an agent that isn't a candidate is ignored
   (warning logged), and the next selector runs.
5. **Throwing selector.** A selector that throws is skipped (error logged), and the next selector
   runs. This includes an `OperationCanceledException` raised while the request was not
   cancelled, such as an HTTP timeout. Only a real cancellation of the request propagates.
6. **Single candidate.** No selector runs. `selectorId` is `"only-candidate"`.
7. **Nobody decides.** The first candidate is used. `selectorId` is `"fallback"`.
8. **`previousAgentId`** is given to selectors as `PreviousAgent` only if it parses as a GUID and
   matches a candidate. Otherwise `PreviousAgent` is `null`. It never causes an error response.
9. **Selector input.** Selectors receive the full converted conversation (all messages, including
   attachments), all request context items, the availability context, the surface ID, the
   current user's group IDs, and the frontend tools.
10. **Unchanged errors.** No surface -> 400. No candidates -> 404. Same titles and details as
    today.
11. **Non-`auto` agents** behave exactly as today and ignore `previousAgentId`.
12. **Notification.** Every `auto` pick publishes exactly one `AIAgentSelectedNotification`,
    before the agent run starts, whose `Selection` matches the `agent_selected` event. That
    includes `only-candidate` and `fallback`. None is published when there are no candidates
    (404) or for non-`auto` agents.

**Response** - the first event stays the `agent_selected` custom event, with two added fields:

```json
{
  "name": "agent_selected",
  "value": {
    "agentId": "<guid>",
    "agentName": "...",
    "agentAlias": "...",
    "selectorId": "llm | sticky | only-candidate | fallback | <custom>",
    "reason": "<string or null>"
  }
}
```

**Audit log** - the agent run's audit entry (`AIAuditLog.Metadata`) contains
`Umbraco.AI.Agent.SelectorId` and, when non-null, `Umbraco.AI.Agent.SelectionReason`. They are
absent for non-`auto` runs.

### C# extension surface (public, `Umbraco.AI.Agent.Core`)

- `IAIAgentSelector`, `AIAgentSelectionRequest`, `AIAgentSelectionResult`
- `AIAgentSelectorCollection`, `AIAgentSelectorCollectionBuilder`, `builder.AIAgentSelectors()`
- `IAIAgentSelectionService.SelectAgentAsync`, `AIAgentSelectionInput`
- `LLMAgentSelector` (registered), `StickyAgentSelector` (not registered)
- `AIAgentSelectorIds` (public static): `Llm`, `Sticky`, `OnlyCandidate`, `Fallback` - the built-in
  selector ID strings, so handlers and audit queries don't copy literals
- `AIAgentExecutionOptions.Selection` (new optional property)
- `AIAgentSelectedNotification` (`StatefulNotification`, not cancelable): `Selection`,
  `Request`, `Messages`
- `IAIAgentService.SelectAgentForPromptAsync` - still works, returns the same agent the new service
  selects, marked `[Obsolete("Use IAIAgentSelectionService.SelectAgentAsync. Will be removed in v20")]`

**`StickyAgentSelector`:** returns `PreviousAgent` with `SelectorId = "sticky"` when it is set,
otherwise `null`.

**`LLMAgentSelector`:** same classifier prompt and input as today (last user message text). On no
classifier profile, an unparseable reply, or a GUID that isn't a candidate, it returns `null`
instead of picking the first agent.

## Frontend components

All in `Umbraco.AI.Agent.UI` (chat library) plus its transport in
`Umbraco.AI.Agent.Web.StaticAssets`. No new components, no visual changes.

- **`run.controller.ts`** - when the current agent is `auto` and `resolvedAgent$` holds a value,
  each run sends `forwardedProps.previousAgentId = resolvedAgent.agentId`. On `agent_selected`,
  it stores `selectorId` and `reason` along with the existing fields. `resetConversation()` clears
  the previous pick, so a new chat starts fresh. `abortRun()` keeps it, so the turn after a
  cancelled run still sends `previousAgentId`.
- **`uai-agent-client.ts`** - merges `previousAgentId` into `forwardedProps` next to the existing
  `resume` entries. Must not drop either one.
- **`uai-http-agent.ts`** - still strips `resume` only. `previousAgentId` passes through to the
  server unchanged.
- **`resolvedAgent$` type** (`chat/context.ts`) - gains optional `selectorId?: string` and
  `reason?: string | null`. Optional, so existing consumers (Copilot) still compile.
- **Explicit (non-`auto`) agents** send no `previousAgentId`.

## Copilot Workspace

Applies to `StreamConversationAGUIController` (Workspace's per-conversation stream endpoint).

1. **Explicit agents unchanged.** When `conversation.AgentIdOrAlias` names an active agent, it runs as
   today. No selection runs, no `agent_selected` is sent, and there is no selection audit metadata.
2. **Auto uses the selection service.** When it is `auto` (or the named agent is missing or inactive,
   as today), the endpoint calls `IAIAgentSelectionService.SelectAgentAsync` with
   surface `copilot-workspace`, this turn's converted messages (on regenerate, the last persisted
   user message text, as today), the frontend tools, and the previous pick. It no longer calls the
   obsolete `SelectAgentForPromptAsync`.
3. **Previous pick.** `PreviousAgentId` is the `AgentId` of the newest assistant message in the
   conversation, or null when there is none (new chat, or only legacy rows with no agent ID).
4. **Run options.** The run gets `AIAgentExecutionOptions.Selection`, so `SelectorId`/`SelectionReason`
   reach the audit log as on the plain endpoint. `ConversationHistory` and `AdditionalProperties`
   are unchanged.
5. **Event.** The stream starts with the same `agent_selected` event as the plain endpoint (agentId,
   agentName, agentAlias, selectorId, reason).
6. **No candidates.** Returns the existing 404 "No agent available", word for word.
7. **Agent stamped on messages.** Every persisted assistant message from a Workspace run (explicit
   or auto) stores the ID of the agent that produced it. User, tool and system messages store null.
8. **History API.** Each message in the Workspace conversation messages response carries `agentId`
   (null when unknown).
9. **Reopened chats show names.** When a conversation is reopened, each assistant message with a
   known `agentId` shows that agent's name. If the ID is unknown to the client's agent list, no name
   is shown.
10. **Migrations.** SQLite and SQL Server migrations add the nullable column. Existing data is kept.
