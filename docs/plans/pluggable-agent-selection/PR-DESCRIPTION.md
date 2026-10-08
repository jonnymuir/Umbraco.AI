[Plan folder](https://github.com/umbraco/Umbraco.AI/blob/v18/feature/pluggable-agent-selection/docs/plans/pluggable-agent-selection/README.md) | [Idea doc](https://github.com/umbraco/Umbraco.AI/blob/v18/dev/docs/ideas/pluggable-routing.md)

## Why the change

Copilot's "Auto" agent pick was one hard-coded LLM classifier that only saw the last message's text. This makes it an ordered, pluggable chain of selectors, so developers can drive the pick with their own business rules while today's behaviour stays the default.

## Special things to note

- A selector that times out is skipped like any other failure. That means an `OperationCanceledException` thrown while the request wasn't cancelled, such as an HTTP timeout. Only a real cancellation of the request propagates. This was decided on review, and it changes today's behaviour: before, a classifier timeout failed the whole Copilot request. The user still waits for the timeout itself, because there is no per-selector time limit.
- A selector's `Reason` and `SelectorId` are written word for word into `AIAuditLog.Metadata`, without redaction. This was decided on review. The XML docs tell selector authors to keep both short and free of personal data (`AIAgentSelectionResult.cs:12`).
- The obsolete `IAIAgentService.SelectAgentForPromptAsync` now proxies to the new service (`AIAgentService.cs:246`, via `StaticServiceProvider` to avoid a DI cycle). It picks the same agent for the same input. Two side effects follow from the new design: it now publishes `AIAgentSelectedNotification`, and a throwing classifier now falls back to the first candidate instead of throwing.
- `AIAgentService` (internal) lost three constructor parameters that only the old classifier used. `StreamAgentAGUIController` gained a new DI constructor, and its two old public constructors are `[Obsolete]` for v20. They resolve the new dependencies through `StaticServiceProvider`.
- The live `agent_selected` event leaves the `reason` key out when it is null (the AG-UI serializer skips nulls). SPEC's example shows `"reason": null`. The frontend type treats it as optional.
- On `auto`, the controller converts messages a second time for selection. The converter is a pure mapping with no file storage, so this only costs one more base64 decode of attachments. A malformed attachment on an `auto` request now fails before streaming starts instead of inside the run. It already failed before this PR.
- On follow-up turns, attachments reach selectors as `UriContent` links, not bytes.
- **Needs a decision:** `IAIConversationService` is public, and Workspace 18.0.0 has shipped. This PR adds `GetLastAssistantAgentIdAsync` to it, which breaks any third-party class that implements the interface. Callers are unaffected. This follows the same precedent as `TruncateAfterLastUserMessageAsync`. The alternative is a default interface implementation.
- **Copilot Workspace is included.** `v18/dev` gained Workspace after this branch was cut, so it is merged in (merge commit, no rebase). Workspace's own stream endpoint now uses the selection service too:
  - Its previous pick is the agent on the newest assistant message, read on the server. The browser never sends it.
  - The run carries the selection, so audit metadata is recorded.
  - It sends `agent_selected`, so Workspace now shows which agent answered, live.
- **Database migration:** a nullable `AgentId` column on `umbracoAIConversationsMessage` (`UmbracoAIConversations_MessageAgentId`, SQLite and SQL Server). Existing rows stay null and show no agent name.
- **Workspace API and client:** the messages response gains `agentId`, and the Workspace client is regenerated. The regeneration also picked up older drift (https base URL, and the removal of the `GetFile*` types for an endpoint deleted earlier). That drift is in its own `chore` commit.
- Reopened Workspace chats show the agent name on each reply. To make that work on first open, a saved conversation now waits for the agent list before loading its history. Draft chats don't wait.
- In Workspace, selectors only see this turn's messages, not the saved history. This is a known follow-up.
- Cancelling a run now keeps the previous pick (`run.controller.ts`). Only a conversation reset clears it.
- **Live-verified on the v18 demo site:**
  - **Copilot sidebar:** a custom selector driving the pick; `selectorId`/`reason` in the event and the audit row; the notification firing; the LLM default; sticky off by default; sticky keeping the agent across turns and after Cancel; a fresh conversation sending no previous pick.
  - **Workspace:** `agent_selected` first in the stream; `AgentId` stored on assistant rows; sticky keeping the agent from saved history; `SelectorId` in audit metadata; agent names live and after a full page reload; explicit-agent chats unchanged.
  - **Not live-verified:** resuming after a tool approval, on either surface, because no demo agent has an approval-gated tool.
- No new Management API routes.
- **Backport:** the v17 port is #468 (draft, into `v17/dev`). The Umbraco.Docs "Extending > Agent selection" page is a follow-up.

## Change outline

The new extension point and its types, all public, in `Umbraco.AI.Agent.Core/Agents/Selection/`:

```csharp
public interface IAIAgentSelector
{
    // null = "no opinion", the next selector decides
    Task<AIAgentSelectionResult?> SelectAgentAsync(AIAgentSelectionRequest request, CancellationToken ct = default);
}

public sealed record AIAgentSelectionResult(AIAgent Agent, string SelectorId, string? Reason);

public sealed class AIAgentSelectionRequest
{
    IReadOnlyList<AIAgent> CandidateAgents;      // active + in scope, already filtered
    IReadOnlyList<ChatMessage> Messages;         // full conversation incl. attachments
    AgentAvailabilityContext AvailabilityContext;
    IReadOnlyList<AIRequestContextItem> ContextItems;
    string SurfaceId;
    IReadOnlyList<Guid> UserGroupIds;
    IReadOnlyList<AIFrontendTool> FrontendTools;
    AIAgent? PreviousAgent;                      // previous turn's pick, only if still a candidate
}

public static class AIAgentSelectorIds { Llm = "llm", Sticky = "sticky", OnlyCandidate = "only-candidate", Fallback = "fallback" }

public sealed class AIAgentSelectedNotification : StatefulNotification   // observe only
{
    AIAgentSelectionResult Selection; AIAgentSelectionRequest Request; EventMessages Messages;
}
```

Registration. `LLMAgentSelector` is the only default entry. `StickyAgentSelector` ships but is opt-in:

```diff
 // UmbracoBuilderExtensions.AddUmbracoAIAgentCore
+builder.Services.AddSingleton<IAIAgentSelectionService, AIAgentSelectionService>();
+builder.AIAgentSelectors()
+    .Append<LLMAgentSelector>();

 // opt-in, in a site's own composer:
+builder.AIAgentSelectors().InsertBefore<LLMAgentSelector, StickyAgentSelector>();
```

What `AIAgentSelectionService.SelectAgentAsync` does. Scope filtering always runs first, so a selector can never reach a ruled-out agent:

```
candidates = agents for surface where IsActive && scope allows      (moved from SelectAgentForPromptAsync)
if none            -> return null                                    (no notification)
request            = { candidates, messages, context, user groups, previous pick if still a candidate, ... }
if one candidate   -> pick it, "only-candidate"
else for each selector in AIAgentSelectors() order:
       result null           -> next
       agent not a candidate -> log warning, next
       throws                -> log error, next     (OperationCanceledException propagates)
       otherwise             -> pick the matching candidate instance
if nobody decided  -> first candidate, "fallback"
publish AIAgentSelectedNotification(selection, request)
```

Call chain for an `auto` request:

```diff
 POST /agents/auto/stream-agui
   StreamAgentAGUIController.StreamAgentAGUI
-    IAIAgentService.SelectAgentForPromptAsync(lastUserMessageText, surface, context)
+    IAIAgentSelectionService.SelectAgentAsync(input)          // full messages, context items, tools,
+                                                              // forwardedProps.previousAgentId (bad -> null)
-    IAIAgentService.StreamAgentAGUIAsync(agentId, request, tools)
+    IAIAgentService.StreamAgentAGUIAsync(agentId, request, tools,
+        new AIAgentExecutionOptions { Selection = selection })  // -> SelectorId/SelectionReason in LogKeys
+                                                                //    -> AIAuditLog.Metadata
     prepend CUSTOM agent_selected
```

`agent_selected` event value:

```diff
 { "agentId": "...", "agentName": "...", "agentAlias": "...",
+  "selectorId": "llm | sticky | only-candidate | fallback | <custom>",
+  "reason": "..." }    // omitted when null
```

Frontend (`Umbraco.AI.Agent.UI` + transport). In Auto mode the browser echoes back the previous pick:

```diff
 UaiRunController (run.controller.ts)
   sendUserMessage / regenerate / both resume paths
-    client.sendMessage(messages, tools, context, resume)
+    client.sendMessage(messages, tools, context, resume, previousAgentIdForRequest())  // only when agent is "auto"
   onCustomEvent("agent_selected") -> resolvedAgent (+ selectorId, reason)
   resetConversation() -> clears the pick
-  abortRun()         -> cleared the pick
+  abortRun()         -> keeps the pick

 UaiAgentClient.sendMessage
-  forwardedProps: resume?.length ? { resume } : undefined
+  forwardedProps: { resume?, previousAgentId? } or undefined when both are empty
```

Copilot Workspace (call chain for an auto turn in a saved conversation):

```diff
 POST /conversations/{id}/stream-agui
   StreamConversationAGUIController.StreamAgentAGUI
-    IAIAgentService.SelectAgentForPromptAsync(lastUserMessageText, "copilot-workspace", ...)
+    previous = IAIConversationService.GetLastAssistantAgentIdAsync(conversationId)
+    IAIAgentSelectionService.SelectAgentAsync({ this turn's messages, frontend tools, previousAgentId: previous })
     IAIAgentService.StreamAgentAGUIAsync(agentId, request, tools,
-        new AIAgentExecutionOptions { ConversationHistory, AdditionalProperties })
+        new AIAgentExecutionOptions { ConversationHistory, AdditionalProperties, Selection })
+    prepend CUSTOM agent_selected

 ConversationChatHistoryProvider (persisting a run's messages)
+  assistant rows: AgentId = runtime context AgentId (TryGetValue, null if absent)
```

```diff
 umbracoAIConversationsMessage
   Id, ConversationId, Sequence, Role, ContentJson, ContentText, ...
+  AgentId  uniqueidentifier / TEXT  NULL
```

Tests: 77 new Agent specs and 28 new Workspace specs (23 C#, 5 vitest). Suites on the final commit: Agent 355 (unit + integration), Workspace 84 unit + 2 integration, Workspace vitest 45, Automate 76. Automate and Agent.Deploy still build.

🤖 Generated with [Claude Code](https://claude.com/claude-code)
