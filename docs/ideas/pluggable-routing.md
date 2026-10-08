# Pluggable Routing: Agent Selection and Profile Routing

## Status: Under Consideration

Two related ideas for giving developers and editors more control over *what* handles a request:

1. **Pluggable agent selection** - make Copilot's "auto" agent mode an extension point instead of a
   single hard-coded LLM classifier.
2. **Profile routing** - let a profile pick the actual model/connection per request, based on the
   request itself (size, attachments, tools, caller).

They solve different problems and sit at different layers. Agent selection picks *who* answers
(instructions, tools, persona). Profile routing picks *which model* runs the answer. Profile
routing is the bigger idea, because it would benefit every caller (agents, prompts, inline chat,
Automate), not just Copilot's auto mode.

---

## Part 1: Pluggable Agent Selection

### How it works today

- `StreamAgentAGUIController` handles the `auto` alias. It takes only the **text of the last user
  message** and calls `IAIAgentService.SelectAgentForPromptAsync`.
- `AIAgentService.SelectAgentForPromptAsync`:
  1. Gets agents for the surface, filters to active + scope-available (`AIAgentScopeValidator`).
  2. 0 agents -> null, 1 agent -> that agent (no LLM call).
  3. Otherwise builds a classification prompt (agent ID, name, description + user message) and
     sends it to the classifier profile (`GetClassifierProfileAsync`, falls back to default chat).
  4. Regex-parses a GUID from the reply. On any failure, silently returns the first agent.

Limitations:

- Only the message text is visible to selection. Attachments, conversation history, context
  items, frontend tools, and message size are all ignored.
- No way to plug in cheap deterministic rules (e.g. "has an image -> vision agent").
- No reason is recorded for the choice, and the "fall back to first agent" path is silent.

### Proposal

Introduce an ordered, pluggable selector chain:

```csharp
public interface IAIAgentSelector
{
    // Return null for "no opinion" so the next selector in the chain decides.
    Task<AIAgentSelectionResult?> SelectAgentAsync(
        AIAgentSelectionRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class AIAgentSelectionRequest
{
    public required IReadOnlyList<AIAgent> CandidateAgents { get; init; } // already scope-filtered
    public required IReadOnlyList<ChatMessage> Messages { get; init; }    // full history incl. attachments
    public required AgentAvailabilityContext Context { get; init; }
    public required string SurfaceId { get; init; }
    public IReadOnlyList<AITool> FrontendTools { get; init; } = [];
}

public sealed record AIAgentSelectionResult(AIAgent Agent, string? Reason);
```

- Registered via a collection builder (`AIAgentSelectorCollectionBuilder`), so packages can
  `Append` / `InsertBefore` like the existing chat middleware collection.
- The current LLM classifier becomes `LLMAgentSelector`, appended last by default.
- **Scope filtering stays in the service and runs before the chain.** Selectors can only choose
  from `CandidateAgents`. This keeps scope rules un-bypassable by third-party code.
- Keep the single-candidate short-circuit.
- Include `Reason` in the `agent_selected` AG-UI event and the audit log.
- `SelectAgentForPromptAsync` stays, proxies to the new method, marked
  `[Obsolete("Will be removed in v20")]`.

Example selectors this unlocks: image attachment -> vision agent, long conversation -> summariser
agent, current entity is media -> media agent, user group -> restricted agent.

---

## Part 2: Profile Routing

### Why this matters more

When someone says "route by request size", they usually don't want a different *agent*. They want
the *same* agent (same instructions, same tools) to run on a different *model*:

- Short, simple questions -> small, cheap, fast model.
- Very long input -> long-context model.
- Images / documents attached -> vision-capable model.
- Structured output or many tools -> a model that handles those well.
- Primary provider down or rate-limited -> fallback provider.

That decision belongs at the profile layer, not the agent layer.

### The good news: there is one chokepoint

Every chat path builds its client through `IAIChatClientFactory.CreateClientAsync(AIProfile)`:

- `AIChatService` (inline chat, prompts) - `AIChatService.cs:235`, `:268`, `:297`
- `AIAgentFactory` (agent runs) - `AIAgentFactory.cs:181`
- `AIAgentService.SelectAgentForPromptAsync` (the classifier itself) - `AIAgentService.cs:288`

`AIChatClientFactory` builds this pipeline, outermost first:

```
ScopedProfileChatClient          <- sets ProfileId/ModelId/ProviderId in runtime context
  └─ middleware collection       <- function invocation, guardrails, tracking, options overrides...
       └─ AIErrorClassifyingChatClient
            └─ provider client (bound to profile.Model.ModelId + capability settings)
```

The client is bound to **one profile at creation time**, before any messages exist. So routing
can't be a pre-step that picks a profile up front. It has to be a client that decides *when it is
called*, because only then does it see the messages.

### Proposal: a routing chat client

```
RoutingChatClient                       <- evaluates rules on each GetResponseAsync call
  ├─ (cached) pipeline for profile A    <- full normal pipeline, incl. function invocation
  ├─ (cached) pipeline for profile B
  └─ (cached) pipeline for fallback
```

- When the factory is asked for a **router profile**, it returns a `RoutingChatClient` instead of a
  provider pipeline.
- On each call, the router evaluates its rules against the messages, `ChatOptions`, and runtime
  context, picks a target profile, and delegates to that profile's full normal pipeline (built
  lazily via the same factory and cached for the life of the client).
- Because the router sits **outside** each target's function-invocation middleware, a whole tool
  loop stays on one model. Routing happens once per user turn, not once per model round-trip.
  This avoids switching models mid tool loop (reasoning/thinking content, provider-specific
  message shapes, and prompt-cache hits all assume one model).
- Each target's own `ScopedProfileChatClient` sets the real `ProfileId`/`ModelId`, so usage,
  analytics, and audit logs record the model that actually ran. Add one new context key
  (e.g. `RoutedFromProfileId`) so you can still see the router that made the call.

Agents, prompts, and settings just point at the router profile like any other profile. Nothing
upstream needs to know routing happened.

### Rules: copy the guardrail model

Guardrails already have the right shape, and reusing it keeps the product consistent:

| Guardrails today | Profile routing |
|------------------|-----------------|
| `AIGuardrail` entity with ordered `AIGuardrailRule`s | Router profile with ordered route rules |
| `IAIGuardrailEvaluator` discovered via `[AIGuardrailEvaluator(id, name)]` | `IAIRouteCondition` discovered via `[AIRouteCondition(id, name)]` |
| `GetConfigSchema()` drives the backoffice editor | Same, so conditions are configurable in the UI |
| Code-based (`Regex`, `Contains`) and LLM-based evaluators | Code-based (size, attachments) and LLM-based ("is this complex?") conditions |

Each rule = condition + config + target profile. First matching rule wins. A required default
target catches everything else.

```csharp
public interface IAIRouteCondition : IDiscoverable
{
    string Id { get; }
    string Name { get; }
    string Description { get; }
    Type? ConfigType { get; }
    AIEditableModelSchema? GetConfigSchema();

    Task<bool> MatchesAsync(AIRouteContext context, object? config, CancellationToken cancellationToken);
}

public sealed class AIRouteContext
{
    public required IReadOnlyList<ChatMessage> Messages { get; init; }
    public ChatOptions? Options { get; init; }
    public AIRuntimeContext? RuntimeContext { get; init; } // feature type/alias, user, entity
    public int EstimatedInputTokens { get; init; }          // computed once, shared by all conditions
}
```

Built-in conditions worth shipping:

- **Input size** - estimated tokens above/below N.
- **Has attachments** - optionally filtered by media type (image, PDF, audio).
- **Tool count** / **has structured output** - from `ChatOptions`.
- **Caller** - feature type (agent / prompt / inline chat / Automate) and alias.
- **User group** - route admins or specific teams to a different model.
- **LLM classifier** (opt-in) - ask a small model "simple or complex?". Costs a call per turn.

Plus **fallback on error**: if the chosen target fails with a classified provider error
(`AIErrorClassifyingChatClient` already does the classifying), retry on the next eligible target.
This gives provider failover almost for free.

### Things to solve

- **Where a router lives in the data model.** `AIProfile` has `required Guid ConnectionId` and an
  `AIModelRef Model`. A router has neither. Options:
  - A new profile kind (e.g. `AIProfileKind.Router`) with nullable connection/model. Touches a lot
    of code that assumes those exist.
  - A new settings type (`AIRouterProfileSettings`) on a profile pointing at a built-in "router"
    pseudo-provider. Less churn, but feels like a hack.
  - A separate entity ("Model Router") that profile pickers also accept. Cleanest model, biggest
    UI change.
- **Options read too early.** `AIAgentFactory` copies `Temperature` / `MaxOutputTokens` from
  `profile.Settings` into `ChatOptions` when the agent is built. With a router, that would read the
  *router's* settings, not the target's. Target-specific settings need to be applied inside each
  target pipeline, or the router needs to rewrite them per call.
- **Capability differences between targets.** A rule could send a request with tools or images to
  a model that doesn't support them. The router should validate targets on save, or skip
  ineligible targets at runtime.
- **Conversation history across models.** In a multi-turn chat, turn 1 might run on model A and
  turn 2 on model B. History messages can carry provider-specific `RawRepresentation` or reasoning
  content. Needs testing with mixed providers, and maybe an option to stick to the first model for
  a conversation.
- **Token estimation.** M.E.AI has no general tokenizer. A chars/4 estimate is probably good
  enough for routing, but should be documented as an estimate.
- **Deploy.** A router references other profiles. `Umbraco.AI.Deploy` needs to treat those as
  dependencies.
- **Cycles.** Router -> router -> router. Either forbid routers as targets or cap the depth.
- **Default profile slots.** Should a router be allowed as the default chat profile or the
  classifier profile? Probably yes for default chat, and the classifier is a great candidate for
  a cheap model anyway.

### How the two parts fit together

```
User message
   │
   ▼
Agent selection (Part 1)        picks WHO answers: instructions, tools, persona
   │
   ▼
Agent's profile
   │
   ▼
Profile routing (Part 2)        picks WHICH MODEL runs it: size, attachments, cost, failover
   │
   ▼
Provider
```

They are independent. Either can ship without the other. The agent classifier itself could also
use a router profile, so selection runs on the cheapest model that can do it.

---

## Suggested order

1. **Pluggable agent selection.** Small, fits the current code, mostly a refactor of
   `SelectAgentForPromptAsync` into a chain.
2. **Profile routing, code-only first.** `RoutingChatClient` + `IAIRouteCondition` + a code-defined
   router, to prove the per-turn routing and the options/history concerns above.
3. **Profile routing in the backoffice.** Router editor modelled on the guardrail rule editor, once
   the data-model question is decided.
