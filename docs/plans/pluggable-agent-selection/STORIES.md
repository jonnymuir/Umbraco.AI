# Stories

Feature: pluggable agent selection for Copilot's `auto` mode. Design: `ARCHITECTURE.md`,
contract: `SPEC.md`.

## Definition of Ready

- Role, capability and value are stated, and the value isn't hollow.
- Given/When/Then acceptance criteria cover the happy path, with sad paths listed separately.
- Out of scope is explicit (see `BRIEF.md` Non-goals).
- Every criterion traces to a numbered guarantee in `SPEC.md`.

## Definition of Done

- Every acceptance criterion passes as an executable spec (xUnit + Moq + Shouldly,
  `Umbraco.AI.Agent.Tests.Unit`), sad paths included.
- `dotnet build Umbraco.AI.Agent/Umbraco.AI.Agent.slnx` and `dotnet test` are green. Frontend
  changes build with `npm run build:agent` and `npm run build:agent-ui`.
- Anything on a real entry point (controller, composer registration, frontend transport) is
  proven once against the running demo site, not only by unit tests.
- Public API compatibility kept: `SelectAgentForPromptAsync` still works, and is marked
  `[Obsolete("... Will be removed in v20")]`.

> Frontend criteria (S5 AC3-AC5) have no unit-test harness in `Umbraco.AI.Agent.UI`. They are
> verified on the demo site through the S5 wire task, not by `bdd-specs`.

---

## Epic: Pluggable agent selection

### S1 - Plug in my own agent selection rule

As a **developer integrating Umbraco.AI into a site**,
I want to register my own agent selector that decides which agent `auto` mode uses,
so that agent choice follows my business rules instead of only an LLM's reading of agent
descriptions.

Size: L (core of the feature: interfaces, collection, service, controller wiring).

**Happy path**

```
AC1 - Custom selector decides
  Given a custom selector registered before the LLM selector
  And   it returns candidate agent B
  When  an auto request is selected
  Then  agent B is selected

AC2 - Collection order is respected
  Given selectors X then Y are registered, and both return a different candidate
  When  an auto request is selected
  Then  X's agent is selected

AC3 - Later selectors don't run once one decides
  Given selectors X then Y are registered, and X returns a candidate
  When  an auto request is selected
  Then  Y is never called

AC4 - "No opinion" passes to the next selector
  Given selector X returns null and selector Y returns candidate C
  When  an auto request is selected
  Then  agent C is selected

AC5 - Default behaviour is unchanged
  Given no custom selectors are registered (only LLMAgentSelector)
  And   the classifier replies with the ID of candidate B
  When  an auto request is selected
  Then  agent B is selected

AC6 - LLM selector keeps today's input
  Given only LLMAgentSelector is registered
  When  an auto request with several messages is selected
  Then  the classifier prompt contains only the last user message text, plus every candidate's ID, name and description

AC7 - Single candidate short-circuits
  Given exactly one candidate agent is available
  When  an auto request is selected
  Then  that agent is selected with SelectorId "only-candidate"

AC8 - No selector runs for a single candidate
  Given exactly one candidate agent is available
  When  an auto request is selected
  Then  no selector is called

AC9 - Obsolete method still works
  Given selectors that pick agent B
  When  IAIAgentService.SelectAgentForPromptAsync is called
  Then  it returns agent B

AC10 - Registered through the builder
  Given a composer calls builder.AIAgentSelectors().InsertBefore<LLMAgentSelector, MySelector>()
  When  the AIAgentSelectorCollection is resolved
  Then  MySelector comes before LLMAgentSelector
```

**Sad path / edge**

```
AC11 - Selector returns a non-candidate
  Given selector X returns an agent that is not a candidate (out of scope or inactive)
  And   selector Y returns candidate C
  When  an auto request is selected
  Then  agent C is selected

AC12 - Scope is never bypassed
  Given every selector returns an agent outside the candidates
  When  an auto request is selected
  Then  the selected agent is a candidate

AC13 - Throwing selector is skipped
  Given selector X throws InvalidOperationException and selector Y returns candidate C
  When  an auto request is selected
  Then  agent C is selected

AC14 - Cancellation is not swallowed
  Given selector X throws OperationCanceledException
  When  an auto request is selected
  Then  OperationCanceledException propagates

AC15 - Nobody decides
  Given every selector returns null
  When  an auto request is selected
  Then  the first candidate is selected with SelectorId "fallback"

AC16 - LLM failure falls through
  Given only LLMAgentSelector is registered and no classifier/default chat profile exists
  When  an auto request is selected
  Then  the first candidate is selected with SelectorId "fallback"

AC17 - LLM unparseable reply falls through
  Given only LLMAgentSelector is registered and the classifier replies with no GUID
  When  an auto request is selected
  Then  the first candidate is selected with SelectorId "fallback"

AC18 - No candidates
  Given no active, in-scope agents for the surface
  When  the auto stream endpoint is called
  Then  it returns 404 "No active agents found"

AC19 - Missing surface
  Given an auto request with no surface context item
  When  the auto stream endpoint is called
  Then  it returns 400 "Surface is required for auto agent selection"

AC20 - Explicit agents are untouched
  Given a request for an explicit agent ID (not "auto")
  When  the stream endpoint is called
  Then  no selector is called
```

### S2 - My rules can see the full request context

As a **developer writing a selection rule**,
I want my selector to receive the whole conversation, all context items, the current user's
groups and the frontend tools,
so that I can base rules on context without the package guessing which fields I need.

Size: S.

```
AC1 - Full conversation
  Given an auto request with three messages, one with an image attachment
  When  a selector is called
  Then  request.Messages holds all three messages, including the attachment content

AC2 - All context items
  Given an auto request whose context includes an entity key item
  When  a selector is called
  Then  request.ContextItems contains that entity key item

AC3 - Availability context
  Given an auto request from the "content" section for a "document" entity
  When  a selector is called
  Then  request.AvailabilityContext has Section "content" and EntityType "document"

AC4 - User groups
  Given the current backoffice user is in groups G1 and G2
  When  a selector is called
  Then  request.UserGroupIds is [G1, G2]

AC5 - Frontend tools
  Given an auto request with two frontend tools
  When  a selector is called
  Then  request.FrontendTools holds both tools

AC6 - Candidates only
  Given agents A (in scope), B (out of scope) and C (inactive)
  When  a selector is called
  Then  request.CandidateAgents is [A]
```

### S3 - I can see why an agent was picked

As a **developer debugging selection rules**,
I want the selector and reason for each pick in the `agent_selected` event and the audit log,
so that I can tell which rule fired, and spot silent fallbacks.

Size: M.

```
AC1 - Event carries selector ID
  Given selector "my-rule" picks agent B with reason "editing products"
  When  the auto stream starts
  Then  the first event is agent_selected with selectorId "my-rule"

AC2 - Event carries reason
  Given selector "my-rule" picks agent B with reason "editing products"
  When  the auto stream starts
  Then  the agent_selected event has reason "editing products"

AC3 - Event keeps existing fields
  Given any auto pick of agent B
  When  the auto stream starts
  Then  the agent_selected event still has agentId, agentName and agentAlias of B

AC4 - Audit metadata: selector
  Given selector "my-rule" picks agent B
  When  the agent run is audited
  Then  AIAuditLog.Metadata["Umbraco.AI.Agent.SelectorId"] is "my-rule"

AC5 - Audit metadata: reason
  Given selector "my-rule" picks agent B with reason "editing products"
  When  the agent run is audited
  Then  AIAuditLog.Metadata["Umbraco.AI.Agent.SelectionReason"] is "editing products"

AC6 - Fallback is recorded
  Given every selector returns null
  When  the auto stream starts
  Then  the agent_selected event has selectorId "fallback"
```

**Sad path / edge**

```
AC7 - Null reason is omitted from audit
  Given a selector picks agent B with a null reason
  When  the agent run is audited
  Then  AIAuditLog.Metadata has no "Umbraco.AI.Agent.SelectionReason" key

AC8 - Explicit runs have no selection metadata
  Given a request for an explicit agent ID
  When  the agent run is audited
  Then  AIAuditLog.Metadata has no "Umbraco.AI.Agent.SelectorId" key

AC9 - Run options otherwise unchanged
  Given an auto pick
  When  the controller starts the run
  Then  the AIAgentExecutionOptions passed differ from a default instance only by Selection
```

### S4 - I can react to picks without writing a selector

As a **developer adding logging or analytics**,
I want an `AIAgentSelectedNotification` after every `auto` pick,
so that I can watch selection without changing it.

Size: S.

```
AC1 - Published once per pick
  Given a selector picks agent B
  When  an auto request is selected
  Then  exactly one AIAgentSelectedNotification is published

AC2 - Carries the result
  Given selector "my-rule" picks agent B with reason R
  When  the notification is handled
  Then  notification.Selection is (B, "my-rule", R)

AC3 - Carries the request the selectors saw
  Given an auto request
  When  the notification is handled
  Then  notification.Request is the same AIAgentSelectionRequest the selectors received

AC4 - Single candidate still notifies
  Given exactly one candidate agent
  When  an auto request is selected
  Then  one notification is published with SelectorId "only-candidate"

AC5 - Fallback still notifies
  Given every selector returns null
  When  an auto request is selected
  Then  one notification is published with SelectorId "fallback"
```

**Sad path / edge**

```
AC6 - No candidates, no notification
  Given no candidate agents
  When  selection runs
  Then  no AIAgentSelectedNotification is published

AC7 - Explicit agents, no notification
  Given a request for an explicit agent ID
  When  the stream endpoint is called
  Then  no AIAgentSelectedNotification is published
```

### S5 - Rules can keep the same agent across a conversation

As a **developer who wants a conversation to stay with one agent**,
I want selectors to know the previous turn's agent, and an opt-in sticky selector,
so that I can stop `auto` switching agents mid-chat without writing that logic myself.

Size: M (backend + frontend transport).

**Happy path**

```
AC1 - Previous agent is resolved
  Given an auto request with forwardedProps.previousAgentId = candidate A's ID
  When  a selector is called
  Then  request.PreviousAgent is agent A

AC2 - Sticky keeps the previous agent
  Given StickyAgentSelector is registered before LLMAgentSelector
  And   previousAgentId is candidate A
  When  an auto request is selected
  Then  agent A is selected with SelectorId "sticky"

AC3 - Browser sends the previous pick (demo-site verified)
  Given an auto conversation where the first turn selected agent A
  When  the user sends a second message
  Then  the request's forwardedProps.previousAgentId is A's ID

AC4 - New chat starts fresh (demo-site verified)
  Given an auto conversation that selected agent A
  When  the user starts a new conversation and sends a message
  Then  the request has no forwardedProps.previousAgentId

AC5 - Resume entries survive (demo-site verified)
  Given an auto conversation resuming after a tool approval
  When  the resume request is sent
  Then  forwardedProps still carries the resume entries alongside previousAgentId
```

**Sad path / edge**

```
AC6 - Previous agent no longer allowed
  Given previousAgentId is an agent that is not a candidate
  When  a selector is called
  Then  request.PreviousAgent is null

AC7 - Garbage previous ID
  Given previousAgentId is "not-a-guid"
  When  the auto stream endpoint is called
  Then  the request succeeds and request.PreviousAgent is null

AC8 - Sticky with no previous pick
  Given StickyAgentSelector is registered and there is no previousAgentId
  When  an auto request is selected
  Then  StickyAgentSelector returns null and the next selector decides

AC9 - Sticky is off by default
  Given the default composer registrations
  When  the AIAgentSelectorCollection is resolved
  Then  it does not contain StickyAgentSelector

AC10 - Explicit agents ignore previousAgentId
  Given an explicit agent request carrying previousAgentId
  When  the stream endpoint is called
  Then  the explicit agent runs and no selector is called
```

### S6 - Developers can find the extension point (placeholder)

As a developer, I want an Umbraco.Docs page for agent selectors, sticky selection and the
notification, so that I can use this without reading the source. Lives in the Umbraco.Docs repo,
not this one. Detailed later.

## Epic: Copilot Workspace

### S7 - Workspace auto mode uses my selection rules

As a **developer integrating Umbraco.AI**, I want Copilot Workspace's auto mode to run my agent
selectors, record the pick and keep the previous agent available to them, so that my rules behave
the same in Workspace as in the Copilot sidebar.

Size: M.

```
AC1 - Auto runs the selection service
  Given a Workspace conversation set to auto and a custom selector that picks agent B
  When  the user sends a message
  Then  agent B runs

AC2 - Explicit agent skips selection
  Given a Workspace conversation set to agent A (active)
  When  the user sends a message
  Then  no selection runs

AC3 - Event sent
  Given an auto Workspace conversation
  When  the stream starts
  Then  the first event is agent_selected with the selector ID

AC4 - Audit metadata
  Given an auto Workspace pick by selector "my-rule"
  When  the run is audited
  Then  the run options carry the Selection

AC5 - Previous pick from history
  Given an auto Workspace conversation whose newest assistant message was produced by agent A
  When  the user sends a message
  Then  the selection input's PreviousAgentId is A

AC6 - No previous pick in a new chat
  Given a new auto Workspace conversation with no assistant messages
  When  the user sends a message
  Then  the selection input's PreviousAgentId is null

AC7 - No candidates
  Given an auto Workspace conversation and no available agents
  When  the user sends a message
  Then  the endpoint returns 404 "No agent available"
```

### S8 - Workspace remembers which agent answered

As a **backoffice user reopening a Workspace chat**, I want each reply to show which agent wrote it,
so that I can tell who said what in a multi-agent conversation.

Size: M.

```
AC1 - Assistant messages store the agent
  Given a Workspace run by agent A
  When  the assistant reply is persisted
  Then  the stored message's AgentId is A

AC2 - Other roles store none
  Given a Workspace run
  When  the user message is persisted
  Then  the stored message's AgentId is null

AC3 - History API returns it
  Given a persisted assistant message by agent A
  When  the conversation messages are fetched
  Then  that message's agentId is A

AC4 - Reopened chat shows the name (demo-site verified)
  Given a conversation whose replies came from agent A
  When  the user reopens it
  Then  each reply shows agent A's name

AC5 - Legacy rows
  Given an assistant message stored before this change (null AgentId)
  When  the conversation is reopened
  Then  the reply shows no agent name and nothing errors
```
